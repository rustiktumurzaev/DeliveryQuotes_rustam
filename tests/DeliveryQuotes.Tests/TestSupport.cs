using System.Collections.Immutable;
using DeliveryQuotes;
using Xunit;

namespace DeliveryQuotes.Tests;

internal static class TestSupport
{
    public static readonly DeliveryLimits Limits = new(1m, 50m);

    public static readonly Zone ZoneA = new("A", "Centre", 10_000);
    public static readonly Zone ZoneB = new("B", "Suburbs", 30_000);
    public static readonly Zone ZoneC = new("C", "Outskirts", 8_000);

    public static readonly string[] RequestLines =
    {
        "id,customer,distanceKm,weightGrams,zone",
        "R1,Alice,2,1500,A",
        "R2,Bob,10,8200,A",
        "R3,Carol,50,12300,B",
        "R4,Dave,75,2000,B",
        "R5,Erin,1,500,A",
        "R6,Frank,0,300,A",
        "R7,Grace,8,6000,B",
        "R8,Heidi,20,6000,B",
        "R9,Ivan,5,3000,C",
        "R10,Judy,12,4000,C"
    };

    public static readonly string[] ZoneLines =
    {
        "code,name,capacityGrams",
        "A,Centre,10000",
        "B,Suburbs,30000",
        "C,Outskirts,8000"
    };

    public static IReadOnlyDictionary<string, Zone> Zones { get; } =
        new Dictionary<string, Zone> { ["A"] = ZoneA, ["B"] = ZoneB, ["C"] = ZoneC };

    public static Ledger NewLedger() => Ledger.Create(new[] { ZoneA, ZoneB, ZoneC });

    public static DeliveryRequest Request(string id = "R1", decimal km = 2m, int grams = 1500, string zone = "A") =>
        new(id, "Alice", km, grams, zone);

    public static ImmutableArray<DeliveryRequest> SampleRequests() => RequestParser.ParseCsvLines(RequestLines).Requests;

    public static T Value<T>(Result<T> result) => Assert.IsType<Result<T>.Success>(result).Value;

    public static Error Failure<T>(Result<T> result) => Assert.IsType<Result<T>.Failure>(result).Error;

    public static ValidatedRequest Valid(DeliveryRequest request) =>
        Value(ValidatedRequest.Create(request, Zones, Limits));
}

// A folder under the system temp path that is deleted when the test ends.
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dq-tests-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string Write(string name, params string[] lines)
    {
        var path = File(name);
        System.IO.File.WriteAllLines(path, lines);
        return path;
    }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
