using System.Collections.Immutable;

namespace DeliveryQuotes;

public static class QuotePipeline
{
    public static QuoteSummary Run(
        IReadOnlyList<DeliveryRequest> requests,
        DeliveryLimits limits,
        PricePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(policy);

        var deliverable = SelectDeliverable(requests, limits);
        var priced = PriceRequests(deliverable, policy.PriceForDistance);
        return BuildSummary(priced);
    }

    public static ImmutableArray<DeliveryRequest> SelectDeliverable(
        IReadOnlyList<DeliveryRequest> requests,
        DeliveryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(limits);

        return requests
            .Where(r => r.DistanceKm >= limits.MinDistanceKm && r.DistanceKm <= limits.MaxDistanceKm)
            .ToImmutableArray();
    }

    public static ImmutableArray<PricedRequest> PriceRequests(
        IReadOnlyList<DeliveryRequest> requests,
        Func<decimal, decimal> priceForDistance)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(priceForDistance);

        return requests
            .Select(r => new PricedRequest(r, priceForDistance(r.DistanceKm)))
            .ToImmutableArray();
    }

    public static QuoteSummary BuildSummary(IReadOnlyList<PricedRequest> priced)
    {
        ArgumentNullException.ThrowIfNull(priced);

        var quotes = priced
            .OrderBy(p => p.Price)
            .ThenBy(p => p.Request.Id, StringComparer.Ordinal)
            .Select(p => new Quote(p.Request.Id, p.Request.Customer, p.Request.DistanceKm, p.Price))
            .ToImmutableArray();

        return new QuoteSummary(quotes, quotes.Length, quotes.Sum(q => q.Price));
    }
}
