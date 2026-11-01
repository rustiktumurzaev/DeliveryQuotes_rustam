using System.Collections.Immutable;

namespace DeliveryQuotes;

// The main Result chain of the project:
//   validate -> native weight surcharge -> issue quote -> accept -> receipt
// Bind is used where the next step returns a Result, Map where it is a plain function.
// If any step fails, the later steps are skipped and the caller gets that first Error.
public static class OrderWorkflow
{
    public static Result<OrderOutcome> Place(
        Ledger ledger,
        DeliveryRequest request,
        PricePolicy policy,
        DeliveryLimits limits,
        IWeightSurcharge surcharge)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(surcharge);

        return ValidatedRequest.Create(request, ledger.Zones, limits)
            .Bind(valid => surcharge.For(valid.WeightGrams)
                .Map(extra => (Valid: valid, Price: policy.PriceForDistance(valid.DistanceKm) + extra)))
            .Bind(priced => ledger.Issue(priced.Valid, priced.Price))
            .Bind(issued => issued.Ledger.Accept(issued.Id))
            .Map(accepted => new OrderOutcome(accepted.Ledger, ToReceipt(accepted)));
    }

    // Lazy version: nothing runs until the caller enumerates. Each step pulls one request from the
    // source, processes it and yields the result. The ledger is a local variable of this iterator and
    // moves forward only after a success, so a failed request leaves it as it was.
    //
    // Enumerating the returned sequence twice processes the requests twice (and pulls from the
    // source twice). Call ToImmutableArray() once when a stable snapshot is needed.
    public static IEnumerable<ProcessedOrder> ProcessLazily(
        Ledger ledger,
        IEnumerable<DeliveryRequest> requests,
        PricePolicy policy,
        DeliveryLimits limits,
        IWeightSurcharge surcharge)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(surcharge);

        return Iterate(ledger, requests, policy, limits, surcharge);
    }

    private static IEnumerable<ProcessedOrder> Iterate(
        Ledger ledger,
        IEnumerable<DeliveryRequest> requests,
        PricePolicy policy,
        DeliveryLimits limits,
        IWeightSurcharge surcharge)
    {
        var current = ledger;

        foreach (var request in requests)
        {
            var result = Place(current, request, policy, limits, surcharge);
            current = result.Match(outcome => outcome.Ledger, _ => current);
            yield return new ProcessedOrder(request.Id, result.Map(o => o.Receipt), current);
        }
    }

    // Deliberate materialisation point.
    public static ImmutableArray<ProcessedOrder> ProcessAll(
        Ledger ledger,
        IEnumerable<DeliveryRequest> requests,
        PricePolicy policy,
        DeliveryLimits limits,
        IWeightSurcharge surcharge) =>
        ProcessLazily(ledger, requests, policy, limits, surcharge).ToImmutableArray();

    public static Receipt ToReceipt(Placed placed) =>
        placed.State switch
        {
            QuoteState.Quoted q => new Receipt(placed.Id, q.Request.Customer, q.Request.Zone.Code, "Quoted", q.Price),
            QuoteState.Accepted a => new Receipt(placed.Id, a.Request.Customer, a.Request.Zone.Code, "Accepted", a.Price),
            QuoteState.Cancelled c => new Receipt(placed.Id, c.Request.Customer, c.Request.Zone.Code, "Cancelled", c.Refund),
            _ => new Receipt(placed.Id, string.Empty, string.Empty, "Unknown", 0m)
        };
}
