using System.Collections.Immutable;

namespace DeliveryQuotes;

// What happened when a raw request went through the whole order desk.
public abstract record Placement
{
    private Placement()
    {
    }

    public sealed record Rejected(ImmutableArray<string> Reasons) : Placement;

    public sealed record QuoteOnly(Ledger Next, QuoteState.Quoted Quote, ImmutableArray<string> Reasons) : Placement;

    public sealed record Accepted(Ledger Next, QuoteState.Accepted Order) : Placement;
}
