using System.Collections.Immutable;
using System.Globalization;

namespace DeliveryQuotes;

// A request that has passed every business rule. The constructor is private and the properties
// have no setters, so the only way to get one is ValidatedRequest.Create.
public sealed record ValidatedRequest
{
    public const int MaxWeightGrams = 30_000;

    public string Id { get; }
    public string Customer { get; }
    public decimal DistanceKm { get; }
    public int WeightGrams { get; }
    public Zone Zone { get; }

    private ValidatedRequest(string id, string customer, decimal distanceKm, int weightGrams, Zone zone)
    {
        Id = id;
        Customer = customer;
        DistanceKm = distanceKm;
        WeightGrams = weightGrams;
        Zone = zone;
    }

    // Failure: ErrorCode.InvalidRequest, the message names the request and lists every broken rule.
    public static Result<ValidatedRequest> Create(
        DeliveryRequest? request,
        IReadOnlyDictionary<string, Zone> zones,
        DeliveryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(limits);

        if (request is null)
        {
            return Result<ValidatedRequest>.Fail(ErrorCode.InvalidRequest, "The request is missing.");
        }

        Zone? zone = zones.Lookup(request.ZoneCode ?? string.Empty) switch
        {
            Maybe<Zone>.Some some => some.Value,
            _ => null
        };

        var problems = FindProblems(request, zone, limits).ToImmutableArray();
        if (zone is null || !problems.IsEmpty)
        {
            return Result<ValidatedRequest>.Fail(
                ErrorCode.InvalidRequest,
                $"Request '{request.Id}' rejected: {string.Join(" ", problems)}");
        }

        return Result<ValidatedRequest>.Ok(
            new ValidatedRequest(request.Id.Trim(), request.Customer.Trim(), request.DistanceKm, request.WeightGrams, zone));
    }

    private static IEnumerable<string> FindProblems(DeliveryRequest r, Zone? zone, DeliveryLimits limits)
    {
        if (string.IsNullOrWhiteSpace(r.Id))
        {
            yield return "Request id is required.";
        }

        if (string.IsNullOrWhiteSpace(r.Customer))
        {
            yield return "Customer name is required.";
        }

        if (r.DistanceKm < limits.MinDistanceKm || r.DistanceKm > limits.MaxDistanceKm)
        {
            yield return $"Distance {Fmt(r.DistanceKm)} km is outside the allowed range {Fmt(limits.MinDistanceKm)}-{Fmt(limits.MaxDistanceKm)} km.";
        }

        if (r.WeightGrams < 1 || r.WeightGrams > MaxWeightGrams)
        {
            yield return $"Weight {r.WeightGrams} g is outside the allowed range 1-{MaxWeightGrams} g.";
        }

        if (zone is null)
        {
            yield return $"Zone '{r.ZoneCode}' does not exist.";
        }
        else if (r.WeightGrams > zone.CapacityGrams)
        {
            yield return $"Parcel of {r.WeightGrams} g is heavier than the whole daily capacity of zone {zone.Code} ({zone.CapacityGrams} g).";
        }
    }

    private static string Fmt(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
