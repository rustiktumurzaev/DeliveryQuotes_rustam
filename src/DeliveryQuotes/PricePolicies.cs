using System.Collections.Immutable;

namespace DeliveryQuotes;

public static class PricePolicies
{
    public static readonly PricePolicy Standard = new("Standard", km => 500m + 100m * km);

    public static readonly PricePolicy Express = new("Express", km => 800m + 150m * km);

    public static ImmutableArray<PricePolicy> All => ImmutableArray.Create(Standard, Express);
}
