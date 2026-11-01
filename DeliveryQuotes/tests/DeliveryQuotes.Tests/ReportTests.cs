using System.Collections.Immutable;
using DeliveryQuotes;
using Xunit;
using static DeliveryQuotes.Tests.TestSupport;

namespace DeliveryQuotes.Tests;

public class ReportTests
{
    private static OrderLine Line(string id, string zone, decimal km, int grams, decimal price) =>
        new(id, "Cust", zone, km, grams, price);

    private static readonly OrderLine[] Sample =
    {
        Line("R1", "A", 2m, 1500, 700m),
        Line("R2", "A", 10m, 8200, 1700m),
        Line("R3", "B", 50m, 12300, 5900m),
        Line("R7", "B", 8m, 6000, 1350m),
        Line("R8", "B", 20m, 6000, 2550m),
        Line("R9", "C", 5m, 3000, 1000m),
        Line("R10", "C", 12m, 4000, 1700m)
    };

    // ---- report 1: dispatch list ----

    [Fact]
    public void DispatchList_FiltersScoresRanksAndNumbers()
    {
        var rows = Reports.DispatchList(Sample, 4000, Scoring.HeavyFirst);

        Assert.Equal(new[] { "R3", "R8", "R2", "R7", "R10" }, rows.Select(r => r.Id));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.Rank));
        Assert.Equal(49.6m, rows[0].Score);
        Assert.Equal(14m, rows[4].Score);
    }

    [Fact]
    public void DispatchList_EqualScores_AreOrderedByIdSoResultIsDeterministic()
    {
        var tied = new[]
        {
            Line("B2", "A", 10m, 5000, 1m),
            Line("A9", "A", 10m, 5000, 1m),
            Line("B1", "A", 10m, 5000, 1m)
        };

        var first = Reports.DispatchList(tied, 1, Scoring.HeavyFirst);
        var second = Reports.DispatchList(tied.AsEnumerable().Reverse(), 1, Scoring.HeavyFirst);

        Assert.Equal(new[] { "A9", "B1", "B2" }, first.Select(r => r.Id));
        Assert.Equal(first.Select(r => r.Id), second.Select(r => r.Id));
    }

    [Fact]
    public void DispatchList_EmptyInputAndNoMatches_GiveEmptyResult()
    {
        Assert.Empty(Reports.DispatchList(Array.Empty<OrderLine>(), 1, Scoring.HeavyFirst));
        Assert.Empty(Reports.DispatchList(Sample, 99_999, Scoring.HeavyFirst));
    }

    [Fact]
    public void DispatchList_NullArguments_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Reports.DispatchList(null!, 1, Scoring.HeavyFirst));
        Assert.Throws<ArgumentNullException>(() => Reports.DispatchList(Sample, 1, null!));
    }

    // ---- report 2: revenue by zone ----

    [Fact]
    public void RevenueByZone_GroupsSumsCountsAndOrdersByRevenue()
    {
        var rows = Reports.RevenueByZone(Sample);

        Assert.Equal(new[] { "B", "C", "A" }, rows.Select(r => r.ZoneCode));
        Assert.Equal(new ZoneSummary("B", 3, 24300, 9800m, 5900m), rows[0]);
        Assert.Equal(new ZoneSummary("C", 2, 7000, 2700m, 1700m), rows[1]);
        Assert.Equal(new ZoneSummary("A", 2, 9700, 2400m, 1700m), rows[2]);
    }

    [Fact]
    public void RevenueByZone_EqualRevenue_OrdersByZoneCode()
    {
        var rows = Reports.RevenueByZone(new[] { Line("1", "C", 1m, 1, 100m), Line("2", "A", 1m, 1, 100m), Line("3", "B", 1m, 1, 100m) });

        Assert.Equal(new[] { "A", "B", "C" }, rows.Select(r => r.ZoneCode));
    }

    [Fact]
    public void RevenueByZone_EmptyInput_GivesEmptyResult()
    {
        Assert.Empty(Reports.RevenueByZone(Array.Empty<OrderLine>()));
        Assert.Throws<ArgumentNullException>(() => Reports.RevenueByZone(null!));
    }

    [Fact]
    public void ZoneRevenueCsv_WritesHeaderAndRows()
    {
        var csv = Reports.ZoneRevenueCsv(Reports.RevenueByZone(Sample));

        Assert.StartsWith("zone,orders,totalWeightGrams,revenue,largestOrder\n", csv);
        Assert.Contains("B,3,24300,9800,5900\n", csv);
    }

    // ---- extensions ----

    [Fact]
    public void AcceptedLines_ReturnsOnlyAcceptedOrdersSortedById()
    {
        var ledger = NewLedger();
        ledger = Value(ledger.Issue(Valid(Request("R2", 2m, 100, "A")), 10m)).Ledger;          // quoted only
        ledger = Value(OrderWorkflow.Place(ledger, Request("R3", 2m, 100, "A"), PricePolicies.Standard, Limits, NoSurcharge)).Ledger;
        ledger = Value(OrderWorkflow.Place(ledger, Request("R1", 2m, 100, "A"), PricePolicies.Standard, Limits, NoSurcharge)).Ledger;
        ledger = Value(OrderWorkflow.Place(ledger, Request("R4", 2m, 100, "A"), PricePolicies.Standard, Limits, NoSurcharge)).Ledger;
        ledger = Value(ledger.Cancel("R4")).Ledger;                                             // cancelled

        var ids = ledger.AcceptedLines().Select(l => l.Id);

        Assert.Equal(new[] { "R1", "R3" }, ids);
        Assert.Empty(NewLedger().AcceptedLines());
    }

    [Fact]
    public void RankedBy_UsesTheGivenScoreAndDoesNotScoreBeforeEnumeration()
    {
        var calls = 0;

        var ranked = Sample.RankedBy(l => { calls++; return l.Price; });
        Assert.Equal(0, calls);

        Assert.Equal("R3", ranked.First().Line.Id);
        Assert.Equal(Sample.Length, calls);
    }

    // ---- configured functions (currying and partial application) ----

    [Fact]
    public void ConfiguredScores_SameInput_DifferentResultsAndOrder()
    {
        var r2 = Line("R2", "A", 10m, 8200, 1700m);

        Assert.Equal(21.4m, Scoring.HeavyFirst(r2));   // 8.2 kg * 2 + 10 km * 0.5
        Assert.Equal(24.1m, Scoring.FarFirst(r2));     // 8.2 kg * 0.5 + 10 km * 2

        var heavy = Reports.DispatchList(Sample, 4000, Scoring.HeavyFirst).Select(r => r.Id);
        var far = Reports.DispatchList(Sample, 4000, Scoring.FarFirst).Select(r => r.Id);
        Assert.Equal(new[] { "R3", "R8", "R2", "R7", "R10" }, heavy);
        Assert.Equal(new[] { "R3", "R8", "R10", "R2", "R7" }, far);
    }

    [Fact]
    public void CreateScore_MatchesTheCurriedFunction()
    {
        var line = Line("X", "A", 4m, 3000, 1m);

        var viaConfig = Scoring.CreateScore(new ScoreConfig(1m, 1m));
        var viaCurry = Scoring.Priority(1m)(1m);

        Assert.Equal(7m, viaConfig(line));
        Assert.Equal(viaCurry(line), viaConfig(line));
        Assert.Throws<ArgumentNullException>(() => Scoring.CreateScore(null!));
    }
}
