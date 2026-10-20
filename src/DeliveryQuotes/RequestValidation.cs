using System.Collections.Immutable;

namespace DeliveryQuotes;

// Outcome of the factory. Exactly one of two cases:
//   Valid   - carries the checked value and nothing else
//   Invalid - carries every reason the request was rejected
public abstract record RequestValidation
{
    private RequestValidation()
    {
    }

    public sealed record Valid(ValidatedRequest Request) : RequestValidation;

    public sealed record Invalid(ImmutableArray<string> Reasons) : RequestValidation;
}
