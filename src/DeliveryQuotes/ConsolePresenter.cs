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

    public static string FormatError(Error error) => $"[{error.Code}] {error.Message}";

    public static string FormatProcessed(ProcessedOrder processed) =>
        processed.Result.Match(
            receipt => $"  {processed.RequestId,-4} {receipt.Status.ToUpperInvariant(),-9} {receipt.Customer}, zone {receipt.ZoneCode}, price {FormatNumber(receipt.Price)}",
            error => $"  {processed.RequestId,-4} FAILED    {FormatError(error)}");

    public static string FormatStep(string title, Result<Placed> result) =>
        result.Match(
            placed => $"  {title}: done -> {placed.State.GetType().Name}",
            error => $"  {title}: refused -> {FormatError(error)}");

    public static string FormatLookup(string title, Maybe<QuoteState> found) =>
        found switch
        {
            Maybe<QuoteState>.Some some => $"  {title}: found, state {some.Value.GetType().Name}",
            _ => $"  {title}: not found"
        };

    public static string FormatGrams(string zone, Maybe<int> grams) =>
        grams switch
        {
            Maybe<int>.Some some => $"  Accepted weight in zone {zone}: {some.Value} g",
            _ => $"  Zone {zone} does not exist"
        };

    private static string FormatNumber(decimal value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture);
}
