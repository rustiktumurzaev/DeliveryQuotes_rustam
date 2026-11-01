using System.Collections.Immutable;
using DeliveryQuotes;
using Xunit;
using static DeliveryQuotes.Tests.TestSupport;

namespace DeliveryQuotes.Tests;

public class LazyAndResourceTests
{
    // ---- deferred execution ----

    [Fact]
    public void ProcessLazily_DoesNothingUntilEnumerated_ThenWorksOneItemAtATime()
    {
        var pulled = 0;
        var surchargeCalls = 0;
        IEnumerable<DeliveryRequest> Source()
        {
            foreach (var request in SampleRequests())
            {
                pulled++;
                yield return request;
            }
        }
        var surcharge = new FuncSurcharge(_ => { surchargeCalls++; return Result<int>.Ok(0); });

        var lazy = OrderWorkflow.ProcessLazily(NewLedger(), Source(), PricePolicies.Standard, Limits, surcharge);

        Assert.Equal(0, pulled);
        Assert.Equal(0, surchargeCalls);

        using var enumerator = lazy.GetEnumerator();
        Assert.True(enumerator.MoveNext());

        Assert.Equal(1, pulled);
        Assert.Equal(1, surchargeCalls);
        Assert.Equal("R1", enumerator.Current.RequestId);
    }

    [Fact]
    public void Materialising_RunsOnce_SnapshotCanBeReusedWithoutReprocessing_LazyReenumerationRepeatsWork()
    {
        var pulled = 0;
        IEnumerable<DeliveryRequest> Source()
        {
            foreach (var request in SampleRequests())
            {
                pulled++;
                yield return request;
            }
        }

        var lazy = OrderWorkflow.ProcessLazily(NewLedger(), Source(), PricePolicies.Standard, Limits, NoSurcharge);
        var snapshot = lazy.ToImmutableArray();
        Assert.Equal(10, pulled);

        var firstPass = snapshot.Select(p => p.RequestId).ToArray();
        var secondPass = snapshot.Select(p => p.RequestId).ToArray();
        Assert.Equal(10, pulled);
        Assert.Equal(firstPass, secondPass);

        _ = lazy.ToArray();
        Assert.Equal(20, pulled);   // enumerating the lazy sequence again does the work again
    }

    [Fact]
    public void ProcessLazily_NullArgumentsAreRejectedImmediately()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OrderWorkflow.ProcessLazily(NewLedger(), null!, PricePolicies.Standard, Limits, NoSurcharge));
    }

    // ---- resource lifetime ----

    private sealed class TrackingReader : StringReader
    {
        public int Disposals { get; private set; }

        public TrackingReader(string text) : base(text)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposals++;
            }

            base.Dispose(disposing);
        }
    }

    private sealed class FailingReader : TextReader
    {
        private int _reads;

        public int Disposals { get; private set; }

        public override string? ReadLine() =>
            ++_reads == 1 ? "first" : throw new IOException("disk went away");

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposals++;
            }

            base.Dispose(disposing);
        }
    }

    [Fact]
    public void LineSource_OpensOnlyWhenEnumerated_AndDisposesAfterFullRead()
    {
        var opened = 0;
        var reader = new TrackingReader("a\nb\nc");

        var lines = LineSource.ReadLines(() => { opened++; return reader; });
        Assert.Equal(0, opened);

        Assert.Equal(new[] { "a", "b", "c" }, lines.ToArray());

        Assert.Equal(1, opened);
        Assert.Equal(1, reader.Disposals);
    }

    [Fact]
    public void LineSource_EarlyStop_StillDisposesTheReader()
    {
        var reader = new TrackingReader("a\nb\nc");

        foreach (var line in LineSource.ReadLines(() => reader))
        {
            Assert.Equal("a", line);
            break;
        }

        Assert.Equal(1, reader.Disposals);
    }

    [Fact]
    public void LineSource_FailureWhileReading_DisposesTheReaderAndPropagates()
    {
        var reader = new FailingReader();

        Assert.Throws<IOException>(() => LineSource.ReadLines(() => reader).ToArray());

        Assert.Equal(1, reader.Disposals);
    }

    [Fact]
    public void LineSource_NullOpener_IsRejectedImmediately()
    {
        Assert.Throws<ArgumentNullException>(() => LineSource.ReadLines(null!));
    }

    [Fact]
    public void LoadRequests_ReleasesTheFileHandle_OnSuccessAndOnMalformedContent()
    {
        using var dir = new TempDir();
        var good = dir.Write("good.csv", RequestLines);
        var bad = dir.Write("bad.csv", DataFiles.RequestsHeader, "R1,Alice,abc,1,A");

        _ = DataFiles.LoadRequests(good);
        _ = DataFiles.LoadRequests(bad);

        // Deleting or rewriting would fail (on Windows) if a handle were still open.
        File.Delete(good);
        File.WriteAllText(bad, "changed");
        Assert.False(File.Exists(good));
    }
}
