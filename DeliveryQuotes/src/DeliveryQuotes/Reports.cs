using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace DeliveryQuotes;

// Report 1 - Dispatch list
//   User:    dispatcher planning vehicles
//   Input:   accepted orders (OrderLine), a minimum weight, a scoring function
//   Filter:  weight >= minimum
//   Output:  rank, id, customer, zone, weight, distance, score
//   Order:   best score first, then id
//
// Report 2 - Revenue by zone
//   User:    manager
//   Input:   accepted orders
//   Output:  per zone: number of orders, total weight, revenue, largest order
//   Order:   biggest revenue first, then zone code
//
// Both are pure: they only read the lines they are given.
public static class Reports
{
    public static ImmutableArray<DispatchRow> DispatchList(
        IEnumerable<OrderLine> lines,
        int minWeightGrams,
        Func<OrderLine, decimal> score)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(score);

        return lines
            .Where(l => l.WeightGrams >= minWeightGrams)
            .RankedBy(score)
            .Select((s, index) => new DispatchRow(
                index + 1, s.Line.Id, s.Line.Customer, s.Line.ZoneCode,
                s.Line.WeightGrams, s.Line.DistanceKm, s.Score))
            .ToImmutableArray();
    }

    public static ImmutableArray<ZoneSummary> RevenueByZone(IEnumerable<OrderLine> lines) =>
        lines.SummarizeByZone();

    // Pure text for the export; writing it to disk is DataFiles' job.
    public static string ZoneRevenueCsv(IEnumerable<ZoneSummary> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var text = new StringBuilder("zone,orders,totalWeightGrams,revenue,largestOrder\n");
        foreach (var row in rows)
        {
            text.Append(string.Create(CultureInfo.InvariantCulture,
                $"{row.ZoneCode},{row.Orders},{row.TotalWeightGrams},{row.Revenue},{row.LargestOrder}\n"));
        }

        return text.ToString();
    }
}
