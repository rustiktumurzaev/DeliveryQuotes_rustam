using System.Collections.Immutable;
using DeliveryQuotes;
using Xunit;

namespace DeliveryQuotes.Tests;

public class OrderDeskTests
{
    private static readonly DeliveryLimits Limits = new(1m, 50m);

    private static readonly Zone ZoneA = new("A", "Centre", 10_000);
    private static readonly Zone ZoneB = new("B", "Suburbs", 30_000);

    private static readonly IReadOnlyDictionary<string, Zone> Zones =
        new Dictionary<string, Zone> { ["A"] = ZoneA, ["B"] = ZoneB };

    private static Ledger NewLedger() => Ledger.Create(new[] { ZoneA, ZoneB });

    private static DeliveryRequest Request(string id = "R1", decimal km = 2m, int grams = 1500, string zone = "A") =>
        new(id, "Alice", km, grams, zone);

    private static ValidatedRequest Valid(DeliveryRequest request)
    {
        var validation = Assert.IsType<RequestValidation.Valid>(ValidatedRequest.Create(request, Zones, Limits));
        return validation.Request;
    }

    // ---- factory ----

    [Fact]
    public void Factory_ValidRequest_CreatesUsableValue()
    {
        var validated = Valid(Request(" R1 ", 2m, 1500, "A"));

        Assert.Equal("R1", validated.Id);
        Assert.Equal("Alice", validated.Customer);
        Assert.Equal(2m, validated.DistanceKm);
        Assert.Equal(1500, validated.WeightGrams);
        Assert.Equal(ZoneA, validated.Zone);
    }

    [Theory]
    [InlineData("R1", 0.99, 1500, "A", "Distance")]
    [InlineData("R1", 50.01, 1500, "A", "Distance")]
    [InlineData("R1", 2.0, 0, "A", "Weight")]
    [InlineData("R1", 2.0, 30001, "B", "Weight")]
    [InlineData("R1", 2.0, 1500, "Z", "does not exist")]
    [InlineData("R1", 2.0, 10001, "A", "heavier than the whole daily capacity")]
    [InlineData(" ", 2.0, 1500, "A", "id is required")]
    public void Factory_RejectsEachRule_AndSaysWhy(string id, double km, int grams, string zone, string expectedPart)
    {
        var validation = ValidatedRequest.Create(new DeliveryRequest(id, "Alice", (decimal)km, grams, zone), Zones, Limits);

        var invalid = Assert.IsType<RequestValidation.Invalid>(validation);
        Assert.Contains(invalid.Reasons, r => r.Contains(expectedPart));
    }

    [Fact]
    public void Factory_ReportsAllReasonsAtOnce()
    {
        var validation = ValidatedRequest.Create(new DeliveryRequest("R1", "", 99m, 0, "Z"), Zones, Limits);

        var invalid = Assert.IsType<RequestValidation.Invalid>(validation);
        Assert.Equal(4, invalid.Reasons.Length);
    }

    [Fact]
    public void Factory_ExactLimits_AreAccepted()
    {
        Assert.IsType<RequestValidation.Valid>(ValidatedRequest.Create(Request(km: 1m), Zones, Limits));
        Assert.IsType<RequestValidation.Valid>(ValidatedRequest.Create(Request(km: 50m), Zones, Limits));
        Assert.IsType<RequestValidation.Valid>(ValidatedRequest.Create(Request(grams: 10_000), Zones, Limits));
        Assert.IsType<RequestValidation.Valid>(ValidatedRequest.Create(Request(grams: 1), Zones, Limits));
    }

    [Fact]
    public void Factory_NullRequest_IsInvalidNotAnException()
    {
        Assert.IsType<RequestValidation.Invalid>(ValidatedRequest.Create(null, Zones, Limits));
    }

    // ---- distinct cases and pattern matching ----

    [Fact]
    public void Placement_CoversRejectedQuoteOnlyAndAccepted()
    {
        var ledger = NewLedger();

        var rejected = OrderDesk.Place(ledger, Request(zone: "Z"), PricePolicies.Standard, Limits);
        var accepted = OrderDesk.Place(ledger, Request("R1", 2m, 9_000, "A"), PricePolicies.Standard, Limits);
        var acceptedLedger = Assert.IsType<Placement.Accepted>(accepted).Next;
        var quoteOnly = OrderDesk.Place(acceptedLedger, Request("R2", 3m, 2_000, "A"), PricePolicies.Standard, Limits);

        Assert.IsType<Placement.Rejected>(rejected);
        Assert.IsType<Placement.Accepted>(accepted);
        Assert.IsType<Placement.QuoteOnly>(quoteOnly);
    }

    // ---- normal operation ----

    [Fact]
    public void NormalWorkflow_ValidRequest_BecomesAcceptedOrderWithPrice()
    {
        var placement = OrderDesk.Place(NewLedger(), Request("R2", 10m, 8200, "A"), PricePolicies.Standard, Limits);

        var accepted = Assert.IsType<Placement.Accepted>(placement);
        Assert.Equal(1500m, accepted.Order.Price);
        Assert.Equal(8200, accepted.Next.AcceptedGrams("A"));
        Assert.IsType<QuoteState.Accepted>(accepted.Next.Orders["R2"]);
    }

    // ---- missing prerequisite, repeated operation ----

    [Fact]
    public void Accept_WithoutQuote_IsRefused()
    {
        var transition = NewLedger().Accept("R404");

        var refused = Assert.IsType<Transition.Refused>(transition);
        Assert.Contains("Issue a quote first", refused.Reasons[0]);
    }

    [Fact]
    public void Accept_Twice_SecondCallIsRefusedAndNothingChanges()
    {
        var ledger = Assert.IsType<Placement.Accepted>(
            OrderDesk.Place(NewLedger(), Request(), PricePolicies.Standard, Limits)).Next;

        var again = ledger.Accept("R1");

        var refused = Assert.IsType<Transition.Refused>(again);
        Assert.Contains("already accepted", refused.Reasons[0]);
        Assert.Equal(1500, ledger.AcceptedGrams("A"));
    }

    [Fact]
    public void Issue_SameIdTwice_IsRefused()
    {
        var ledger = NewLedger();
        var first = Assert.IsType<Transition.Applied>(ledger.Issue(Valid(Request()), 700m));

        var second = first.Next.Issue(Valid(Request()), 700m);

        Assert.IsType<Transition.Refused>(second);
    }

    [Fact]
    public void Cancel_OnlyAcceptedOrdersCanBeCancelled_AndOnlyOnce()
    {
        var quoted = Assert.IsType<Transition.Applied>(NewLedger().Issue(Valid(Request()), 700m)).Next;
        Assert.IsType<Transition.Refused>(quoted.Cancel("R1"));

        var accepted = Assert.IsType<Transition.Applied>(quoted.Accept("R1")).Next;
        var cancelled = Assert.IsType<Transition.Applied>(accepted.Cancel("R1"));

        var state = Assert.IsType<QuoteState.Cancelled>(cancelled.State);
        Assert.Equal(700m, state.Refund);
        Assert.Equal(0, cancelled.Next.AcceptedGrams("A"));
        Assert.IsType<Transition.Refused>(cancelled.Next.Cancel("R1"));
        Assert.IsType<Transition.Refused>(cancelled.Next.Accept("R1"));
    }

    // ---- boundary and state preservation ----

    [Fact]
    public void Capacity_ExactlyFull_IsAccepted_OneGramMoreIsNot()
    {
        var ledger = Assert.IsType<Placement.Accepted>(
            OrderDesk.Place(NewLedger(), Request("R1", 2m, 6_000, "A"), PricePolicies.Standard, Limits)).Next;

        var exact = OrderDesk.Place(ledger, Request("R2", 2m, 4_000, "A"), PricePolicies.Standard, Limits);
        var tooMuch = OrderDesk.Place(ledger, Request("R3", 2m, 4_001, "A"), PricePolicies.Standard, Limits);

        Assert.IsType<Placement.Accepted>(exact);
        var quoteOnly = Assert.IsType<Placement.QuoteOnly>(tooMuch);
        Assert.Contains("4000 g free", quoteOnly.Reasons[0]);
    }

    [Fact]
    public void FailedTransition_LeavesOriginalLedgerUnchanged()
    {
        var ledger = Assert.IsType<Placement.Accepted>(
            OrderDesk.Place(NewLedger(), Request(), PricePolicies.Standard, Limits)).Next;
        var ordersBefore = ledger.Orders;

        _ = ledger.Accept("R1");
        _ = ledger.Accept("nope");
        _ = ledger.Cancel("nope");
        _ = OrderDesk.Place(ledger, Request("R9", 99m, 100, "A"), PricePolicies.Standard, Limits);

        Assert.Same(ordersBefore, ledger.Orders);
        Assert.Single(ledger.Orders);
    }

    [Fact]
    public void CancelFreesCapacityForLaterAcceptance()
    {
        var ledger = NewLedger();
        ledger = Assert.IsType<Placement.Accepted>(
            OrderDesk.Place(ledger, Request("R1", 2m, 9_000, "A"), PricePolicies.Standard, Limits)).Next;
        var blocked = OrderDesk.Place(ledger, Request("R2", 2m, 2_000, "A"), PricePolicies.Standard, Limits);
        var blockedLedger = Assert.IsType<Placement.QuoteOnly>(blocked).Next;

        var afterCancel = Assert.IsType<Transition.Applied>(blockedLedger.Cancel("R1")).Next;
        var retry = afterCancel.Accept("R2");

        Assert.IsType<Transition.Applied>(retry);
    }

    [Fact]
    public void ZoneParser_ReadsZonesAndRejectsBadLines()
    {
        var parsed = ZoneParser.ParseCsvLines(new[]
        {
            "code,name,capacityGrams",
            "A,Centre,10000",
            "B,Suburbs,0",
            "A,Again,500",
            "C,Outskirts"
        });

        Assert.Single(parsed.Zones);
        Assert.Equal(3, parsed.Errors.Length);
    }
}
