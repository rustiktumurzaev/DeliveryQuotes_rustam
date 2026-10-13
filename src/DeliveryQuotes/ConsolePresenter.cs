using System.Collections.Immutable;
using System.Globalization;

namespace DeliveryQuotes;

public static class ConsolePresenter
{
    public static string FormatSummary(string policyName, QuoteSummary summary)
    {
        var lines = new List<string> { $"Policy: {policyName}" };

        if (summary.Quotes.IsEmpty)
        {
            lines.Add("  No quotes.");
        }
        else
        {
            lines.AddRange(summary.Quotes.Select(q =>
                $"  {q.Id,-3} {q.Customer,-8} {FormatNumber(q.DistanceKm),6} km -> {FormatNumber(q.Price),8}"));
        }

        lines.Add($"  Quotes: {summary.Count}, total: {FormatNumber(summary.TotalPrice)}");
        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatErrors(ImmutableArray<string> errors)
    {
        var lines = new List<string> { $"Input rejected: {errors.Length} invalid line(s)." };
        lines.AddRange(errors.Select(e => $"  {e}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatNumber(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
