using System.Collections.Immutable;
using DeliveryQuotes;
using Xunit;
using static DeliveryQuotes.Tests.TestSupport;

namespace DeliveryQuotes.Tests;

public class OrderWorkflowTests
{
    // ---- factory ----

    [Fact]
    public void Factory_ValidRequest_CreatesUsableValue()
    {
        var validated = Valid(Request(" R1 ", 2m, 1500, "A"));

        Assert.Equal("R1", validated.Id);
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
    public void Factory_RejectsEachRule_WithInvalidRequestAndReason(string id, double km, int grams, string zone, string expectedPart)
    {
        var result = ValidatedRequest.Create(new DeliveryRequest(id, "Alice", (decimal)km, grams, zone), Zones, Limits);

        var error = Failure(result);
        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
        Assert.Contains(expectedPart, error.Message);
    }

    [Fact]
    public void Factory_ExactLimits_AreAccepted()
    {
        Assert.IsType<Result<ValidatedRequest>.Success>(ValidatedRequest.Create(Request(km: 1m), Zones, Limits));
        Assert.IsType<Result<ValidatedRequest>.Success>(ValidatedRequest.Create(Request(km: 50m), Zones, Limits));
        Assert.IsType<Result<ValidatedRequest>.Success>(ValidatedRequest.Create(Request(grams: 10_000), Zones, Limits));
        Assert.IsType<Result<ValidatedRequest>.Success>(ValidatedRequest.Create(Request(grams: 1), Zones, Limits));
    }

    [Fact]
    public void Factory_NullRequest_IsAFailureNotAnException()
    {
        Assert.Equal(ErrorCode.InvalidRequest, Failure(ValidatedRequest.Create(null, Zones, Limits)).Code);
    }

    // ---- complete chain ----

    [Fact]
    public void CompleteChain_ValidRequest_ProducesAcceptedReceiptAndNewLedger()
    {
        var outcome = Value(OrderWorkflow.Place(NewLedger(), Request("R2", 10m, 8200, "A"), PricePolicies.Standard, Limits));

        Assert.Equal(new Receipt("R2", "Alice", "A", "Accepted", 1500m), outcome.Receipt);
        Assert.IsType<QuoteState.Accepted>(outcome.Ledger.Orders["R2"]);
        Assert.Equal(8200, Assert.IsType<Maybe<int>.Some>(outcome.Ledger.AcceptedGrams("A")).Value);
    }

    [Fact]
    public void TwoPolicies_GiveDifferentPricesThroughTheSameChain()
    {
        var standard = Value(OrderWorkflow.Place(NewLedger(), Request(), PricePolicies.Standard, Limits));
        var express = Value(OrderWorkflow.Place(NewLedger(), Request(), PricePolicies.Express, Limits));

        Assert.Equal(700m, standard.Receipt.Price);
        Assert.Equal(1100m, express.Receipt.Price);
    }

    // ---- domain failures: invalid request, not found, capacity, invalid state ----

    [Fact]
    public void DomainFailure_InvalidRequest_StopsTheChain()
    {
        var ledger = NewLedger();

        var error = Failure(OrderWorkflow.Place(ledger, Request(km: 99m), PricePolicies.Standard, Limits));

        Assert.Equal(ErrorCode.InvalidRequest, error.Code);
        Assert.Contains("R1", error.Message);
        Assert.Empty(ledger.Orders);
    }

    [Fact]
    public void DomainFailure_CapacityExceeded_LeavesNoQuoteBehind()
    {
        var ledger = Value(OrderWorkflow.Place(NewLedger(), Request("R1", 2m, 9_700, "A"), PricePolicies.Standard, Limits)).Ledger;

        var error = Failure(OrderWorkflow.Place(ledger, Request("R2", 2m, 500, "A"), PricePolicies.Standard, Limits));

        Assert.Equal(ErrorCode.CapacityExceeded, error.Code);
        Assert.Contains("300 g free", error.Message);
        Assert.Single(ledger.Orders);
    }

    [Fact]
    public void DomainFailure_NotFound_WhenAcceptingOrCancellingUnknownOrder()
    {
        var ledger = NewLedger();

        Assert.Equal(ErrorCode.NotFound, Failure(ledger.Accept("R404")).Code);
        Assert.Equal(ErrorCode.NotFound, Failure(ledger.Cancel("R404")).Code);
        Assert.Contains("Issue a quote first", Failure(ledger.Accept("R404")).Message);
    }

    [Fact]
    public void DomainFailure_InvalidState_ForRepeatedOperations()
    {
        var accepted = Value(OrderWorkflow.Place(NewLedger(), Request(), PricePolicies.Standard, Limits)).Ledger;

        Assert.Equal(ErrorCode.InvalidState, Failure(accepted.Accept("R1")).Code);
        Assert.Equal(ErrorCode.InvalidState, Failure(accepted.Issue(Valid(Request()), 700m)).Code);

        var cancelled = Value(accepted.Cancel("R1")).Ledger;
        Assert.Equal(ErrorCode.InvalidState, Failure(cancelled.Cancel("R1")).Code);
        Assert.Equal(ErrorCode.InvalidState, Failure(cancelled.Accept("R1")).Code);
    }

    [Fact]
    public void Cancel_OnlyQuotedOrder_IsRejected_AndCancelRefundsPrice()
    {
        var quoted = Value(NewLedger().Issue(Valid(Request()), 700m)).Ledger;
        Assert.Equal(ErrorCode.InvalidState, Failure(quoted.Cancel("R1")).Code);

        var accepted = Value(quoted.Accept("R1")).Ledger;
        var cancelled = Value(accepted.Cancel("R1"));

        Assert.Equal(700m, Assert.IsType<QuoteState.Cancelled>(cancelled.State).Refund);
        Assert.Equal(0, Assert.IsType<Maybe<int>.Some>(cancelled.Ledger.AcceptedGrams("A")).Value);
    }

    // ---- lookups ----

    [Fact]
    public void Lookup_FindPresentAndAbsent()
    {
        var ledger = Value(OrderWorkflow.Place(NewLedger(), Request(), PricePolicies.Standard, Limits)).Ledger;

        Assert.IsType<Maybe<QuoteState>.Some>(ledger.Find("R1"));
        Assert.IsType<Maybe<QuoteState>.None>(ledger.Find("R404"));
    }

    [Fact]
    public void Lookup_EmptyZoneIsSomeZero_UnknownZoneIsNone()
    {
        var ledger = NewLedger();

        Assert.Equal(0, Assert.IsType<Maybe<int>.Some>(ledger.AcceptedGrams("A")).Value);
        Assert.IsType<Maybe<int>.None>(ledger.AcceptedGrams("Z"));
    }

    // ---- boundary and state preservation ----

    [Fact]
    public void Capacity_ExactlyFull_IsAccepted_OneGramMoreIsNot()
    {
        var ledger = Value(OrderWorkflow.Place(NewLedger(), Request("R1", 2m, 6_000, "A"), PricePolicies.Standard, Limits)).Ledger;

        var exact = OrderWorkflow.Place(ledger, Request("R2", 2m, 4_000, "A"), PricePolicies.Standard, Limits);
        var tooMuch = OrderWorkflow.Place(ledger, Request("R3", 2m, 4_001, "A"), PricePolicies.Standard, Limits);

        Assert.IsType<Result<OrderOutcome>.Success>(exact);
        Assert.Equal(ErrorCode.CapacityExceeded, Failure(tooMuch).Code);
    }

    [Fact]
    public void ProcessAll_SampleFile_AcceptsSevenAndKeepsLedgerOnFailures()
    {
        var processed = OrderWorkflow.ProcessAll(NewLedger(), SampleRequests(), PricePolicies.Standard, Limits);

        var accepted = processed.Where(p => p.Result is Result<Receipt>.Success).Select(p => p.RequestId);
        var failed = processed.Where(p => p.Result is Result<Receipt>.Failure).Select(p => p.RequestId);

        Assert.Equal(new[] { "R1", "R2", "R3", "R7", "R8", "R9", "R10" }, accepted);
        Assert.Equal(new[] { "R4", "R5", "R6" }, failed);

        // R4 failed, so the ledger after R4 is the very same object as the ledger after R3.
        Assert.Same(processed[2].LedgerAfter, processed[3].LedgerAfter);
        Assert.Equal(ErrorCode.CapacityExceeded, Failure(processed[4].Result).Code);
    }

    [Fact]
    public void ProcessAll_DoesNotChangeTheInputLedgerOrRequests()
    {
        var ledger = NewLedger();
        var requests = SampleRequests();
        var before = requests.ToArray();

        _ = OrderWorkflow.ProcessAll(ledger, requests, PricePolicies.Standard, Limits);

        Assert.Empty(ledger.Orders);
        Assert.Equal(before, requests);
    }

    [Fact]
    public void ProcessAll_EmptyInput_GivesEmptyResult()
    {
        var processed = OrderWorkflow.ProcessAll(NewLedger(), ImmutableArray<DeliveryRequest>.Empty, PricePolicies.Standard, Limits);

        Assert.Empty(processed);
    }
}
