using System.Collections.Immutable;

namespace DeliveryQuotes;

// The safe workflow: raw request -> validated request -> quote -> accepted order.
// Each step takes the output of the previous one, so a caller cannot skip or reorder them.
public static class OrderDesk
{
    public static Placement Place(Ledger ledger, DeliveryRequest request, PricePolicy policy, DeliveryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(policy);

        return ValidatedRequest.Create(request, ledger.Zones, limits) switch
        {
            RequestValidation.Invalid invalid => new Placement.Rejected(invalid.Reasons),
            RequestValidation.Valid valid => IssueQuote(ledger, valid.Request, policy),
            _ => Unknown()
        };
    }

    private static Placement IssueQuote(Ledger ledger, ValidatedRequest request, PricePolicy policy)
    {
        var price = policy.PriceForDistance(request.DistanceKm);

        return ledger.Issue(request, price) switch
        {
            Transition.Refused refused => new Placement.Rejected(refused.Reasons),
            Transition.Applied { State: QuoteState.Quoted quoted } issued => AcceptQuote(issued.Next, quoted),
            _ => Unknown()
        };
    }

    private static Placement AcceptQuote(Ledger issuedLedger, QuoteState.Quoted quoted) =>
        issuedLedger.Accept(quoted.Request.Id) switch
        {
            Transition.Applied { State: QuoteState.Accepted accepted } done => new Placement.Accepted(done.Next, accepted),
            Transition.Refused refused => new Placement.QuoteOnly(issuedLedger, quoted, refused.Reasons),
            _ => Unknown()
        };

    private static Placement Unknown() =>
        new Placement.Rejected(ImmutableArray.Create("Unexpected outcome from the ledger."));
}
