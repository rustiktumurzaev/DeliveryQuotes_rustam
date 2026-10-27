using System.Collections.Immutable;

namespace DeliveryQuotes;

// Immutable book of quotes and orders. Every operation returns a Result: either the new ledger
// (inside Placed) or an Error. A failed operation cannot change anything, because the old ledger
// is never touched.
//
// Legitimate business order: Issue -> Accept -> Cancel. The order is enforced by QuoteState.
public sealed class Ledger
{
    public ImmutableDictionary<string, Zone> Zones { get; }
    public ImmutableDictionary<string, QuoteState> Orders { get; }

    private Ledger(ImmutableDictionary<string, Zone> zones, ImmutableDictionary<string, QuoteState> orders)
    {
        Zones = zones;
        Orders = orders;
    }

    public static Ledger Create(IEnumerable<Zone> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        return new Ledger(zones.ToImmutableDictionary(z => z.Code), ImmutableDictionary<string, QuoteState>.Empty);
    }

    // Lookups return Maybe, because "not there" is a normal answer.
    public Maybe<QuoteState> Find(string id) => Orders.Lookup(id);

    // None = the zone does not exist. Some(0) = the zone exists and nothing is accepted in it yet.
    public Maybe<int> AcceptedGrams(string zoneCode) =>
        Zones.ContainsKey(zoneCode)
            ? Maybe.Some(Orders.Values
                .OfType<QuoteState.Accepted>()
                .Where(a => a.Request.Zone.Code == zoneCode)
                .Sum(a => a.Request.WeightGrams))
            : Maybe.None<int>();

    public Result<Placed> Issue(ValidatedRequest request, decimal price)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (price < 0)
        {
            return Result<Placed>.Fail(ErrorCode.InvalidRequest, $"The price of quote '{request.Id}' cannot be negative.");
        }

        if (Orders.ContainsKey(request.Id))
        {
            return Result<Placed>.Fail(ErrorCode.InvalidState, $"A quote for '{request.Id}' already exists.");
        }

        return Apply(request.Id, new QuoteState.Quoted(request, price));
    }

    public Result<Placed> Accept(string id) =>
        Find(id)
            .ToResult(() => new Error(ErrorCode.NotFound, $"There is no quote '{id}'. Issue a quote first."))
            .Bind(current => current switch
            {
                QuoteState.Quoted quoted => AcceptQuoted(id, quoted),
                QuoteState.Accepted => Result<Placed>.Fail(ErrorCode.InvalidState, $"Order '{id}' is already accepted."),
                QuoteState.Cancelled => Result<Placed>.Fail(ErrorCode.InvalidState, $"Order '{id}' was cancelled and cannot be accepted."),
                _ => Result<Placed>.Fail(ErrorCode.InvalidState, $"Order '{id}' is in an unknown state.")
            });

    public Result<Placed> Cancel(string id) =>
        Find(id)
            .ToResult(() => new Error(ErrorCode.NotFound, $"There is no order '{id}' to cancel."))
            .Bind(current => current switch
            {
                QuoteState.Accepted accepted => Apply(id, new QuoteState.Cancelled(accepted.Request, accepted.Price)),
                QuoteState.Quoted => Result<Placed>.Fail(ErrorCode.InvalidState, $"Order '{id}' is only quoted, nothing was accepted that could be cancelled."),
                QuoteState.Cancelled => Result<Placed>.Fail(ErrorCode.InvalidState, $"Order '{id}' is already cancelled."),
                _ => Result<Placed>.Fail(ErrorCode.InvalidState, $"Order '{id}' is in an unknown state.")
            });

    private Result<Placed> AcceptQuoted(string id, QuoteState.Quoted quoted)
    {
        var zone = quoted.Request.Zone;

        return AcceptedGrams(zone.Code)
            .ToResult(() => new Error(ErrorCode.NotFound, $"Zone '{zone.Code}' does not exist."))
            .Bind(used =>
            {
                var free = zone.CapacityGrams - used;
                return quoted.Request.WeightGrams > free
                    ? Result<Placed>.Fail(
                        ErrorCode.CapacityExceeded,
                        $"Zone {zone.Code} has {free} g free, but {quoted.Request.WeightGrams} g are needed for '{id}'.")
                    : Apply(id, new QuoteState.Accepted(quoted.Request, quoted.Price));
            });
    }

    private Result<Placed> Apply(string id, QuoteState state) =>
        Result<Placed>.Ok(new Placed(new Ledger(Zones, Orders.SetItem(id, state)), id, state));
}
