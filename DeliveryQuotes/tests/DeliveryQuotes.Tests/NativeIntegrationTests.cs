using System.Collections.Immutable;
using DeliveryQuotes;
using Xunit;
using static DeliveryQuotes.Tests.TestSupport;

namespace DeliveryQuotes.Tests;

// These tests call the real C library. Build it first (native/build.sh or native/build.cmd);
// `dotnet build` does it automatically when gcc is on the PATH.
public class NativeIntegrationTests
{
    [Theory]
    [InlineData(1, 0)]
    [InlineData(5000, 0)]      // last free gram
    [InlineData(5001, 50)]     // one started kilogram above 5000 g
    [InlineData(6000, 50)]
    [InlineData(8200, 200)]    // ceil(3.2) = 4 kg * 50
    [InlineData(12300, 400)]   // ceil(7.3) = 8 kg * 50, checked by hand
    [InlineData(30000, 1250)]  // 25 kg * 50
    public void RealNativeCall_ReturnsManuallyCalculatedSurcharge(int grams, int expected)
    {
        var result = new NativeWeightSurcharge().For(grams);

        Assert.Equal(expected, Value(result));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(30001)]
    public void InvalidWeight_IsRejectedByTheAdapterBeforeTheNativeCall(int grams)
    {
        var nativeCalls = 0;
        var adapter = new NativeWeightSurcharge(w => { nativeCalls++; return 0; });

        var error = Failure(adapter.For(grams));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
        Assert.Equal(0, nativeCalls);
    }

    [Fact]
    public void NativeRejection_BecomesNativeFailure()
    {
        var error = Failure(new NativeWeightSurcharge(_ => -1).For(100));

        Assert.Equal(ErrorCode.NativeFailure, error.Code);
    }

    [Fact]
    public void LoadingProblems_BecomeClearNativeUnavailableFailures()
    {
        Func<int, int>[] failing =
        {
            _ => throw new DllNotFoundException("deliveryquotes_native"),
            _ => throw new EntryPointNotFoundException("dq_weight_surcharge"),
            _ => throw new BadImageFormatException("wrong architecture")
        };

        foreach (var native in failing)
        {
            var error = Failure(new NativeWeightSurcharge(native).For(6000));

            Assert.Equal(ErrorCode.NativeUnavailable, error.Code);
            Assert.False(string.IsNullOrWhiteSpace(error.Message));
        }
    }

    // ---- complete workflows ----

    [Fact]
    public void CompleteWorkflow_Normal_NativeSurchargeFeedsPricesReportsAndExport()
    {
        using var dir = new TempDir();
        var requestsPath = dir.Write("requests.csv", RequestLines);
        var zonesPath = dir.Write("zones.csv", ZoneLines);

        var loaded = DataFiles.LoadRequests(requestsPath)
            .Bind(requests => DataFiles.LoadZones(zonesPath).Map(zones => (requests, zones)));
        var data = Value(loaded);

        var processed = OrderWorkflow.ProcessAll(
            Ledger.Create(data.zones), data.requests, PricePolicies.Standard, Limits, new NativeWeightSurcharge());
        var ledger = processed[^1].LedgerAfter;

        var prices = processed
            .Where(p => p.Result is Result<Receipt>.Success)
            .ToDictionary(p => p.RequestId, p => Value(p.Result).Price);
        Assert.Equal(1700m, prices["R2"]);    // 1500 + 200 native surcharge
        Assert.Equal(5900m, prices["R3"]);    // 5500 + 400 native surcharge
        Assert.Equal(700m, prices["R1"]);     // light parcel, no surcharge

        var zones = Reports.RevenueByZone(ledger.AcceptedLines());
        Assert.Equal(new[] { "B", "C", "A" }, zones.Select(z => z.ZoneCode));
        Assert.Equal(9800m, zones[0].Revenue);

        var export = DataFiles.WriteText(dir.File("zone_revenue.csv"), Reports.ZoneRevenueCsv(zones));
        Assert.Equal(dir.File("zone_revenue.csv"), Value(export));
        Assert.Contains("A,2,9700,2400,1700", File.ReadAllText(dir.File("zone_revenue.csv")));
    }

    [Fact]
    public void CompleteWorkflow_NativeFailure_KeepsLedgerEmptyReportsNothingAndExplains()
    {
        var broken = new NativeWeightSurcharge(_ => throw new DllNotFoundException("deliveryquotes_native"));
        var ledger = NewLedger();

        var processed = OrderWorkflow.ProcessAll(ledger, SampleRequests(), PricePolicies.Standard, Limits, broken);

        // R4 and R6 fail validation first; every other request fails at the native step.
        Assert.Equal(ErrorCode.InvalidRequest, Failure(processed[3].Result).Code);
        Assert.Equal(ErrorCode.NativeUnavailable, Failure(processed[0].Result).Code);
        Assert.All(processed, p => Assert.IsType<Result<Receipt>.Failure>(p.Result));
        Assert.All(processed, p => Assert.Same(ledger, p.LedgerAfter));
        Assert.Empty(processed[^1].LedgerAfter.AcceptedLines());
        Assert.Empty(Reports.RevenueByZone(processed[^1].LedgerAfter.AcceptedLines()));
    }
}
