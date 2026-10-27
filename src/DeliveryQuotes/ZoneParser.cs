using System.Collections.Immutable;
using System.Globalization;

namespace DeliveryQuotes;

public static class ZoneParser
{
    // Format: code,name,capacityGrams (first line is the header).
    public static ParsedZones ParseCsvLines(IReadOnlyList<string>? lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var zones = ImmutableArray.CreateBuilder<Zone>();
        var errors = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = lines[i].Split(',');
            if (fields.Length != 3)
            {
                errors.Add($"Line {i + 1}: expected 3 fields but found {fields.Length}.");
                continue;
            }

            var code = fields[0].Trim();
            var name = fields[1].Trim();
            var capacityText = fields[2].Trim();

            if (code.Length == 0 || name.Length == 0)
            {
                errors.Add($"Line {i + 1}: zone code and name are required.");
            }
            else if (!int.TryParse(capacityText, NumberStyles.None, CultureInfo.InvariantCulture, out var capacity) || capacity <= 0)
            {
                errors.Add($"Line {i + 1}: capacity must be a positive whole number: '{capacityText}'.");
            }
            else if (!seen.Add(code))
            {
                errors.Add($"Line {i + 1}: zone '{code}' is listed twice.");
            }
            else
            {
                zones.Add(new Zone(code, name, capacity));
            }
        }

        return new ParsedZones(zones.ToImmutable(), errors.ToImmutable());
    }
}
