using System.Collections.Immutable;

namespace DeliveryQuotes;

// Result of asking the ledger to change state: either a new ledger plus the new order state,
// or a refusal with reasons (and the old ledger is untouched).
public abstract record Transition
{
    private Transition()
    {
    }

    public sealed record Applied(Ledger Next, QuoteState State) : Transition;

    public sealed record Refused(ImmutableArray<string> Reasons) : Transition;
}
