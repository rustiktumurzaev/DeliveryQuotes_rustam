using System.Text.Json;
using DeliveryQuotes;
using Xunit;
using static DeliveryQuotes.Tests.TestSupport;

namespace DeliveryQuotes.Tests;

public class DataFilesTests
{
    [Fact]
    public void LoadRequests_ValidFile_ReturnsAllRows()
    {
        using var dir = new TempDir();
        var path = dir.Write("requests.csv", RequestLines);

        var requests = Value(DataFiles.LoadRequests(path));

        Assert.Equal(10, requests.Length);
        Assert.Equal("R1", requests[0].Id);
    }

    [Fact]
    public void LoadRequests_MissingFile_ReportsFileMissingWithPath()
    {
        using var dir = new TempDir();
        var path = dir.File("does_not_exist.csv");

        var error = Failure(DataFiles.LoadRequests(path));

        Assert.Equal(ErrorCode.FileMissing, error.Code);
        Assert.Contains("does_not_exist.csv", error.Message);
    }

    [Fact]
    public void LoadRequests_MissingFolder_ReportsFileMissing()
    {
        using var dir = new TempDir();

        var error = Failure(DataFiles.LoadRequests(Path.Combine(dir.Path, "nope", "requests.csv")));

        Assert.Equal(ErrorCode.FileMissing, error.Code);
    }

    [Fact]
    public void LoadRequests_MalformedLines_ReportFileMalformedAndNoPartialData()
    {
        using var dir = new TempDir();
        var path = dir.Write("bad.csv", DataFiles.RequestsHeader, "R1,Alice,2,1500,A", "R2,Bob,abc,100,A", "R3,Cy");

        var error = Failure(DataFiles.LoadRequests(path));

        Assert.Equal(ErrorCode.FileMalformed, error.Code);
        Assert.Contains("2 invalid line(s)", error.Message);
        Assert.Contains("Line 3", error.Message);
    }

    [Fact]
    public void LoadRequests_WrongContent_JsonInsteadOfCsv_IsMalformed()
    {
        using var dir = new TempDir();
        var path = dir.Write("requests.csv", "{ \"requests\": [ 1, 2, 3 ] }");

        Assert.Equal(ErrorCode.FileMalformed, Failure(DataFiles.LoadRequests(path)).Code);
    }

    [Fact]
    public void LoadRequests_EmptyFile_IsMalformedButHeaderOnlyFileIsEmptyDataset()
    {
        using var dir = new TempDir();
        var emptyFile = dir.Write("empty.csv");
        var headerOnly = dir.Write("header.csv", DataFiles.RequestsHeader);

        Assert.Equal(ErrorCode.FileMalformed, Failure(DataFiles.LoadRequests(emptyFile)).Code);
        Assert.Empty(Value(DataFiles.LoadRequests(headerOnly)));
    }

    [Fact]
    public void LoadZones_ValidMissingAndMalformed()
    {
        using var dir = new TempDir();
        var good = dir.Write("zones.csv", ZoneLines);
        var bad = dir.Write("zones_bad.csv", DataFiles.ZonesHeader, "A,Centre,-5", "B,Suburbs");

        Assert.Equal(3, Value(DataFiles.LoadZones(good)).Length);
        Assert.Equal(ErrorCode.FileMissing, Failure(DataFiles.LoadZones(dir.File("none.csv"))).Code);
        Assert.Equal(ErrorCode.FileMalformed, Failure(DataFiles.LoadZones(bad)).Code);
    }

    [Fact]
    public void SaveAcceptedOrders_WritesJsonWithOnlyAcceptedOrders()
    {
        using var dir = new TempDir();
        var ledger = OrderWorkflow.ProcessAll(NewLedger(), SampleRequests(), PricePolicies.Standard, Limits, NoSurcharge)[^1].LedgerAfter;
        var path = dir.File(Path.Combine("out", "accepted.json"));

        var saved = Value(DataFiles.SaveAcceptedOrders(path, ledger));

        Assert.Equal(path, saved);
        var records = JsonSerializer.Deserialize<OrderRecord[]>(File.ReadAllText(path));
        Assert.NotNull(records);
        Assert.Equal(7, records!.Length);
        Assert.DoesNotContain(records, r => r.Id == "R5");
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void SaveAcceptedOrders_WhenTargetCannotBeWritten_FailsAndLeavesNoTempFile()
    {
        using var dir = new TempDir();
        var target = dir.File("orders.json");
        Directory.CreateDirectory(target); // a folder where the file should go

        var error = Failure(DataFiles.SaveAcceptedOrders(target, NewLedger()));

        Assert.Equal(ErrorCode.FileAccess, error.Code);
        Assert.True(Directory.Exists(target));
        Assert.False(File.Exists(target + ".tmp"));
    }

    [Fact]
    public void CompleteWorkflow_FromFilesToSavedOrders()
    {
        using var dir = new TempDir();
        var requestsPath = dir.Write("requests.csv", RequestLines);
        var zonesPath = dir.Write("zones.csv", ZoneLines);
        var outPath = dir.File("accepted.json");

        var saved = DataFiles.LoadRequests(requestsPath)
            .Bind(requests => DataFiles.LoadZones(zonesPath).Map(zones => (requests, zones)))
            .Map(data => OrderWorkflow.ProcessAll(Ledger.Create(data.zones), data.requests, PricePolicies.Standard, Limits, NoSurcharge))
            .Map(processed => processed[^1].LedgerAfter)
            .Bind(ledger => DataFiles.SaveAcceptedOrders(outPath, ledger));

        Assert.Equal(outPath, Value(saved));
        Assert.True(File.Exists(outPath));
    }

    [Fact]
    public void CompleteWorkflow_MissingFile_StopsBeforeAnyLaterStepAndWritesNothing()
    {
        using var dir = new TempDir();
        var zonesPath = dir.Write("zones.csv", ZoneLines);
        var outPath = dir.File("accepted.json");
        var laterSteps = 0;

        var result = DataFiles.LoadRequests(dir.File("missing.csv"))
            .Bind(requests => DataFiles.LoadZones(zonesPath).Map(zones => (requests, zones)))
            .Map(data =>
            {
                laterSteps++;
                return Ledger.Create(data.zones);
            })
            .Bind(ledger =>
            {
                laterSteps++;
                return DataFiles.SaveAcceptedOrders(outPath, ledger);
            });

        Assert.Equal(ErrorCode.FileMissing, Failure(result).Code);
        Assert.Equal(0, laterSteps);
        Assert.False(File.Exists(outPath));
    }
}
