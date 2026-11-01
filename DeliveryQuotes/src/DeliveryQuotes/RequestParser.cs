using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace DeliveryQuotes;

// Boundary parsing only: text in, typed records out. Business rules live in ValidatedRequest.
public static class RequestParser
{
    public static ParsedDataset ParseCsvLines(IReadOnlyList<string>? lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

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
        if (fields.Length != 5)
        {
            error = $"Expected 5 fields but found {fields.Length}.";
            return false;
        }

        var id = fields[0].Trim();
        var customer = fields[1].Trim();
        var distanceText = fields[2].Trim();
        var weightText = fields[3].Trim();
        var zone = fields[4].Trim();

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

        if (!int.TryParse(weightText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var weight))
        {
            error = $"Field 'weightGrams' is not a whole number: '{weightText}'.";
            return false;
        }

        if (weight < 0)
        {
            error = $"Field 'weightGrams' must not be negative: '{weightText}'.";
            return false;
        }

        if (zone.Length == 0)
        {
            error = "Field 'zone' is missing.";
            return false;
        }

        request = new DeliveryRequest(id, customer, distance, weight, zone);
        error = string.Empty;
        return true;
    }
}
