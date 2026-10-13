using System.Collections.Immutable;

namespace DeliveryQuotes;

public sealed record DeliveryRequest(string Id, string Customer, decimal DistanceKm);

public sealed record DeliveryLimits(decimal MinDistanceKm, decimal MaxDistanceKm);

public sealed record PricedRequest(DeliveryRequest Request, decimal Price);

public sealed record Quote(string Id, string Customer, decimal DistanceKm, decimal Price);

public sealed record QuoteSummary(ImmutableArray<Quote> Quotes, int Count, decimal TotalPrice);

public sealed record ParsedDataset(ImmutableArray<DeliveryRequest> Requests, ImmutableArray<string> Errors);

public sealed record PricePolicy(string Name, Func<decimal, decimal> PriceForDistance);
