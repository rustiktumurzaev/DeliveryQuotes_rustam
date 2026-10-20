using System.Collections.Immutable;

namespace DeliveryQuotes;

// Immutable book of all quotes and orders. Every operation returns a new Ledger,
// so a refused operation cannot leave a half-changed book behind.
//
// Legitimate business order: Issue -> Accept -> Cancel. The order is enforced here, by the
// states, not by the caller remembering it. Repeating an operation is refused with a reason.
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

    public int AcceptedGrams(string zoneCode) =>
        Orders.Values
            .OfType<QuoteState.Accepted>()
            .Where(a => a.Request.Zone.Code == zoneCode)
            .Sum(a => a.Request.WeightGrams);

    public Transition Issue(ValidatedRequest request, decimal price)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (price < 0)
        {
            return Refuse("The price of a quote cannot be negative.");
        }

        if (Orders.ContainsKey(request.Id))
        {
            return Refuse($"A quote for '{request.Id}' already exists.");
        }

        var quoted = new QuoteState.Quoted(request, price);
        return new Transition.Applied(new Ledger(Zones, Orders.SetItem(request.Id, quoted)), quoted);
    }

    public Transition Accept(string id)
    {
        if (!Orders.TryGetValue(id, out var current))
        {
            return Refuse($"There is no quote '{id}'. Issue a quote first.");
        }

        return current switch
        {
            QuoteState.Quoted quoted => AcceptQuoted(id, quoted),
            QuoteState.Accepted => Refuse($"Order '{id}' is already accepted."),
            QuoteState.Cancelled => Refuse($"Order '{id}' was cancelled and cannot be accepted."),
            _ => Refuse($"Order '{id}' is in an unknown state.")
        };
    }

    public Transition Cancel(string id)
    {
        if (!Orders.TryGetValue(id, out var current))
        {
            return Refuse($"There is no order '{id}' to cancel.");
        }

        return current switch
        {
            QuoteState.Accepted accepted =>
                Applied(id, new QuoteState.Cancelled(accepted.Request, accepted.Price)),
            QuoteState.Quoted => Refuse($"Order '{id}' is only quoted, nothing was accepted that could be cancelled."),
            QuoteState.Cancelled => Refuse($"Order '{id}' is already cancelled."),
            _ => Refuse($"Order '{id}' is in an unknown state.")
        };
    }

    private Transition AcceptQuoted(string id, QuoteState.Quoted quoted)
    {
        var zone = quoted.Request.Zone;
        var free = zone.CapacityGrams - AcceptedGrams(zone.Code);

        if (quoted.Request.WeightGrams > free)
        {
            return Refuse($"Zone {zone.Code} has {free} g free, but {quoted.Request.WeightGrams} g are needed.");
        }

        return Applied(id, new QuoteState.Accepted(quoted.Request, quoted.Price));
    }

    private Transition Applied(string id, QuoteState state) =>
        new Transition.Applied(new Ledger(Zones, Orders.SetItem(id, state)), state);

    private static Transition Refuse(string reason) =>
        new Transition.Refused(ImmutableArray.Create(reason));
}
