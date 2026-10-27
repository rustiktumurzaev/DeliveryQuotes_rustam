namespace DeliveryQuotes;

// Life of one order. The cases carry different data on purpose:
//   Quoted    - a price was offered, no capacity is reserved yet
//   Accepted  - the customer confirmed it, the weight counts against the zone capacity
//   Cancelled - an accepted order was withdrawn, the full price is refunded
public abstract record QuoteState
{
    private QuoteState()
    {
    }

    public sealed record Quoted(ValidatedRequest Request, decimal Price) : QuoteState;

    public sealed record Accepted(ValidatedRequest Request, decimal Price) : QuoteState;

    public sealed record Cancelled(ValidatedRequest Request, decimal Refund) : QuoteState;
}
