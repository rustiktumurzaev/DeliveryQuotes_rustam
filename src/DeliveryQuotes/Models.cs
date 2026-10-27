using System.Collections.Immutable;

namespace DeliveryQuotes;

// Raw input as it comes out of the CSV file. Nothing here is checked yet.
public sealed record DeliveryRequest(string Id, string Customer, decimal DistanceKm, int WeightGrams, string ZoneCode);

// A delivery zone with the total weight the couriers can carry there in one day.
public sealed record Zone(string Code, string Name, int CapacityGrams);

public sealed record DeliveryLimits(decimal MinDistanceKm, decimal MaxDistanceKm);

public sealed record PricedRequest(DeliveryRequest Request, decimal Price);

public sealed record Quote(string Id, string Customer, decimal DistanceKm, decimal Price);

public sealed record QuoteSummary(ImmutableArray<Quote> Quotes, int Count, decimal TotalPrice);

public sealed record ParsedDataset(ImmutableArray<DeliveryRequest> Requests, ImmutableArray<string> Errors);

public sealed record ParsedZones(ImmutableArray<Zone> Zones, ImmutableArray<string> Errors);

public sealed record PricePolicy(string Name, Func<decimal, decimal> PriceForDistance);
