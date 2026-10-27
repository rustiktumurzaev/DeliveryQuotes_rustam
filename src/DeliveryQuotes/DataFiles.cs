using System.Collections.Immutable;
using System.Text.Json;

namespace DeliveryQuotes;

// Input/output boundary. This is the only place that touches the disk and the only place that
// catches operational exceptions. Everything it returns is a Result, so a missing or broken file
// becomes a clear Error and the domain code never sees an exception.
//
// Expected domain failures (invalid quantity, unknown zone, capacity) are NOT exceptions anywhere.
public static class DataFiles
{
    public const string RequestsHeader = "id,customer,distanceKm,weightGrams,zone";
    public const string ZonesHeader = "code,name,capacityGrams";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Result<ImmutableArray<DeliveryRequest>> LoadRequests(string path) =>
        ReadLines(path)
            .Bind(lines => CheckHeader(path, lines, RequestsHeader))
            .Bind(lines =>
            {
                var parsed = RequestParser.ParseCsvLines(lines);
                return parsed.Errors.IsEmpty
                    ? Result<ImmutableArray<DeliveryRequest>>.Ok(parsed.Requests)
                    : Malformed<ImmutableArray<DeliveryRequest>>(path, parsed.Errors);
            });

    public static Result<ImmutableArray<Zone>> LoadZones(string path) =>
        ReadLines(path)
            .Bind(lines => CheckHeader(path, lines, ZonesHeader))
            .Bind(lines =>
            {
                var parsed = ZoneParser.ParseCsvLines(lines);
                return parsed.Errors.IsEmpty
                    ? Result<ImmutableArray<Zone>>.Ok(parsed.Zones)
                    : Malformed<ImmutableArray<Zone>>(path, parsed.Errors);
            });

    // Writes the accepted orders as JSON. The text goes to a temporary file first and replaces the
    // real file only after the write worked, so a failure never leaves a half-written file behind.
    public static Result<string> SaveAcceptedOrders(string path, Ledger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var records = ledger.Orders.Values
            .OfType<QuoteState.Accepted>()
            .Select(a => new OrderRecord(a.Request.Id, a.Request.Customer, a.Request.Zone.Code,
                a.Request.DistanceKm, a.Request.WeightGrams, a.Price))
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToArray();

        return WriteText(path, JsonSerializer.Serialize(records, JsonOptions));
    }

    public static Result<string> WriteText(string path, string text)
    {
        var temp = path + ".tmp";

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(temp, text);
            File.Move(temp, path, overwrite: true);
            return Result<string>.Ok(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            DeleteTemp(temp);
            return Result<string>.Fail(ErrorCode.FileAccess, $"No permission to write '{path}': {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            DeleteTemp(temp);
            return Result<string>.Fail(ErrorCode.FileAccess, $"'{path}' is not a usable path: {ex.Message}");
        }
        catch (IOException ex)
        {
            DeleteTemp(temp);
            return Result<string>.Fail(ErrorCode.FileAccess, $"Could not write '{path}': {ex.Message}");
        }
    }

    private static Result<ImmutableArray<string>> ReadLines(string path)
    {
        try
        {
            return Result<ImmutableArray<string>>.Ok(File.ReadAllLines(path).ToImmutableArray());
        }
        catch (FileNotFoundException)
        {
            return Result<ImmutableArray<string>>.Fail(ErrorCode.FileMissing, $"File not found: '{path}'.");
        }
        catch (DirectoryNotFoundException)
        {
            return Result<ImmutableArray<string>>.Fail(ErrorCode.FileMissing, $"Folder not found for: '{path}'.");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result<ImmutableArray<string>>.Fail(ErrorCode.FileAccess, $"No permission to read '{path}': {ex.Message}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Result<ImmutableArray<string>>.Fail(ErrorCode.FileAccess, $"'{path}' is not a usable path: {ex.Message}");
        }
        catch (IOException ex)
        {
            return Result<ImmutableArray<string>>.Fail(ErrorCode.FileAccess, $"Could not read '{path}': {ex.Message}");
        }
    }

    private static Result<ImmutableArray<string>> CheckHeader(string path, ImmutableArray<string> lines, string expected)
    {
        if (lines.IsEmpty || !string.Equals(lines[0].Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            return Result<ImmutableArray<string>>.Fail(
                ErrorCode.FileMalformed,
                $"'{path}' is not in the expected format: the first line must be '{expected}'.");
        }

        return Result<ImmutableArray<string>>.Ok(lines);
    }

    private static Result<T> Malformed<T>(string path, ImmutableArray<string> errors) =>
        Result<T>.Fail(
            ErrorCode.FileMalformed,
            $"'{path}' has {errors.Length} invalid line(s): {string.Join(" ", errors)}");

    // Best effort cleanup of the temp file. The real error is returned to the caller either way.
    private static void DeleteTemp(string temp)
    {
        try
        {
            File.Delete(temp);
        }
        catch (IOException)
        {
            // The temp file stays on disk; the original write error is still reported.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}

public sealed record OrderRecord(string Id, string Customer, string Zone, decimal DistanceKm, int WeightGrams, decimal Price);
