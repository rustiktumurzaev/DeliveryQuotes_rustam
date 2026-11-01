using System.Collections.Immutable;

namespace DeliveryQuotes;

// Flat, read-only view of one accepted order. Reports work on these, not on the ledger.
public sealed record OrderLine(string Id, string Customer, string ZoneCode, decimal DistanceKm, int WeightGrams, decimal Price);

public sealed record ScoredLine(OrderLine Line, decimal Score);

public sealed record DispatchRow(int Rank, string Id, string Customer, string ZoneCode, int WeightGrams, decimal DistanceKm, decimal Score);

public sealed record ZoneSummary(string ZoneCode, int Orders, int TotalWeightGrams, decimal Revenue, decimal LargestOrder);

// Custom extension methods. Each one has a real job in the reports.
public static class OrderQueries
{
    // Receiver: Ledger. The ledger holds orders in three states, but every report only cares
    // about accepted ones, so the translation lives next to the type that knows the states.
    // Lazy; ordered by id so the result does not depend on dictionary order.
    public static IEnumerable<OrderLine> AcceptedLines(this Ledger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        return ledger.Orders.Values
            .OfType<QuoteState.Accepted>()
            .Select(a => new OrderLine(a.Request.Id, a.Request.Customer, a.Request.Zone.Code,
                a.Request.DistanceKm, a.Request.WeightGrams, a.Price))
            .OrderBy(l => l.Id, StringComparer.Ordinal);
    }

    // Receiver: IEnumerable<OrderLine>. A scoring function (see Scoring) decides the order:
    // best score first, and when scores are equal the smaller id first, so the order is always the same.
    // Lazy: nothing is scored until the result is enumerated.
    public static IEnumerable<ScoredLine> RankedBy(this IEnumerable<OrderLine> lines, Func<OrderLine, decimal> score)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(score);

        return lines
            .Select(l => new ScoredLine(l, score(l)))
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Line.Id, StringComparer.Ordinal);
    }

    // Receiver: IEnumerable<OrderLine>. One row per zone with count, weight, revenue and the largest order.
    // Biggest revenue first; equal revenue goes by zone code. Returns a snapshot (ImmutableArray).
    public static ImmutableArray<ZoneSummary> SummarizeByZone(this IEnumerable<OrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines
            .GroupBy(l => l.ZoneCode)
            .Select(g => new ZoneSummary(
                g.Key,
                g.Count(),
                g.Sum(l => l.WeightGrams),
                g.Sum(l => l.Price),
                g.Aggregate(0m, (largest, l) => Math.Max(largest, l.Price))))
            .OrderByDescending(z => z.Revenue)
            .ThenBy(z => z.ZoneCode, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
