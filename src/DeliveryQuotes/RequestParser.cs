using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DeliveryQuotes;

public static class RequestParser
{
    public const string ExpectedHeader = "id,customer,distanceKm";

    // A header-only file is a valid EMPTY dataset. A file with no lines at all, or with a different
    // first line, is not: that is missing or wrong input, so it is reported instead of giving
    // an empty "success".
    public static ParsedDataset ParseCsvLines(IReadOnlyList<string>? lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            return HeaderProblem("Line 1: the input is empty, the header line is missing.");
        }

        if (!string.Equals(lines[0]?.Trim(), ExpectedHeader, StringComparison.OrdinalIgnoreCase))
        {
            return HeaderProblem($"Line 1: expected the header '{ExpectedHeader}' but found '{lines[0]?.Trim()}'.");
        }

        var requests = ImmutableArray.CreateBuilder<DeliveryRequest>();
        var errors = ImmutableArray.CreateBuilder<string>();

        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            if (TryParseRequest(lines[i], out var request, out var error))
            {
                requests.Add(request);
            }
            else
            {
                errors.Add($"Line {i + 1}: {error}");
            }
        }

        return new ParsedDataset(requests.ToImmutable(), errors.ToImmutable());
    }

    private static ParsedDataset HeaderProblem(string message) =>
        new(ImmutableArray<DeliveryRequest>.Empty, ImmutableArray.Create(message));

    public static bool TryParseRequest(
        string? line,
        [NotNullWhen(true)] out DeliveryRequest? request,
        out string error)
    {
        request = null;

        if (string.IsNullOrWhiteSpace(line))
        {
            error = "Line is missing or empty.";
            return false;
        }

        var fields = line.Split(',');
        if (fields.Length != 3)
        {
            error = $"Expected 3 fields but found {fields.Length}.";
            return false;
        }

        var id = fields[0].Trim();
        var customer = fields[1].Trim();
        var distanceText = fields[2].Trim();

        if (id.Length == 0)
        {
            error = "Field 'id' is missing.";
            return false;
        }

        if (customer.Length == 0)
        {
            error = "Field 'customer' is missing.";
            return false;
        }

        var numberStyle = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!decimal.TryParse(distanceText, numberStyle, CultureInfo.InvariantCulture, out var distance))
        {
            error = $"Field 'distanceKm' is not a number: '{distanceText}'.";
            return false;
        }

        if (distance < 0)
        {
            error = $"Field 'distanceKm' must not be negative: '{distanceText}'.";
            return false;
        }

        request = new DeliveryRequest(id, customer, distance);
        error = string.Empty;
        return true;
    }
}
