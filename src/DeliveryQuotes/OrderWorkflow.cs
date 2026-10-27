using System.Collections.Immutable;

namespace DeliveryQuotes;

// The main Result chain of the project:
//   validate (can fail) -> issue quote (can fail) -> accept (can fail) -> build receipt (cannot fail)
// Bind is used where the next step returns a Result, Map where the next step is a plain function.
// If any step fails, the later steps are skipped and the caller gets that first Error.
public static class OrderWorkflow
{
    public static Result<OrderOutcome> Place(
        Ledger ledger,
        DeliveryRequest request,
        PricePolicy policy,
        DeliveryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(policy);

        return ValidatedRequest.Create(request, ledger.Zones, limits)
            .Bind(valid => ledger.Issue(valid, policy.PriceForDistance(valid.DistanceKm)))
            .Bind(issued => issued.Ledger.Accept(issued.Id))
            .Map(accepted => new OrderOutcome(accepted.Ledger, ToReceipt(accepted)));
    }

    // Runs requests one after another. The ledger moves forward only after a success.
    public static ImmutableArray<ProcessedOrder> ProcessAll(
        Ledger ledger,
        IEnumerable<DeliveryRequest> requests,
        PricePolicy policy,
        DeliveryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var results = ImmutableArray.CreateBuilder<ProcessedOrder>();
        var current = ledger;

        foreach (var request in requests)
        {
            var result = Place(current, request, policy, limits);
            var next = result.Match(outcome => outcome.Ledger, _ => current);
            results.Add(new ProcessedOrder(request.Id, result.Map(o => o.Receipt), next));
            current = next;
        }

        return results.ToImmutable();
    }

    public static Receipt ToReceipt(Placed placed) =>
        placed.State switch
        {
            QuoteState.Quoted q => new Receipt(placed.Id, q.Request.Customer, q.Request.Zone.Code, "Quoted", q.Price),
            QuoteState.Accepted a => new Receipt(placed.Id, a.Request.Customer, a.Request.Zone.Code, "Accepted", a.Price),
            QuoteState.Cancelled c => new Receipt(placed.Id, c.Request.Customer, c.Request.Zone.Code, "Cancelled", c.Refund),
            _ => new Receipt(placed.Id, string.Empty, string.Empty, "Unknown", 0m)
        };
}
