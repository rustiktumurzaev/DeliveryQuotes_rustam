using System.Collections.Immutable;
using DeliveryQuotes;
using Xunit;

namespace DeliveryQuotes.Tests;

public class QuotePipelineTests
{
    private static readonly string[] SampleLines =
    {
        "id,customer,distanceKm",
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

    private static readonly DeliveryLimits Limits = new(1m, 50m);

    private static ImmutableArray<DeliveryRequest> SampleRequests()
    {
        var dataset = RequestParser.ParseCsvLines(SampleLines);
        Assert.Empty(dataset.Errors);
        return dataset.Requests;
    }

    [Fact]
    public void NormalWorkflow_StandardPolicy_ProducesDocumentedResult()
    {
        var summary = QuotePipeline.Run(SampleRequests(), Limits, PricePolicies.Standard);

        Assert.Equal(new[] { "R5", "R1", "R9", "R7", "R2", "R10", "R8", "R3" }, summary.Quotes.Select(q => q.Id));
        Assert.Equal(new[] { 600m, 700m, 1000m, 1300m, 1500m, 1700m, 2500m, 5500m }, summary.Quotes.Select(q => q.Price));
        Assert.Equal(8, summary.Count);
        Assert.Equal(14800m, summary.TotalPrice);
    }

    [Fact]
    public void EmptyDataset_ProducesEmptySummary()
    {
        var parsed = RequestParser.ParseCsvLines(new[] { "id,customer,distanceKm" });
        var summary = QuotePipeline.Run(parsed.Requests, Limits, PricePolicies.Standard);

        Assert.Empty(parsed.Errors);
        Assert.Empty(parsed.Requests);
        Assert.Empty(summary.Quotes);
        Assert.Equal(0, summary.Count);
        Assert.Equal(0m, summary.TotalPrice);
    }

    [Fact]
    public void BoundaryValues_AreIncludedAtLimitsAndExcludedOutside()
    {
        var requests = new[]
        {
            new DeliveryRequest("MIN", "A", 1m, 100, "A"),
            new DeliveryRequest("MAX", "B", 50m, 100, "A"),
            new DeliveryRequest("BELOW", "C", 0.99m, 100, "A"),
            new DeliveryRequest("ABOVE", "D", 50.01m, 100, "A"),
            new DeliveryRequest("ZERO", "E", 0m, 100, "A")
        };

        var summary = QuotePipeline.Run(requests, Limits, PricePolicies.Standard);

        Assert.Equal(new[] { "MIN", "MAX" }, summary.Quotes.Select(q => q.Id));
        Assert.Equal(new[] { 600m, 5500m }, summary.Quotes.Select(q => q.Price));
    }

    [Theory]
    [InlineData("R1,Alice,-5,100,A")]
    [InlineData("R1,Alice,abc,100,A")]
    [InlineData("R1,Alice,,100,A")]
    [InlineData("R1,,5,100,A")]
    [InlineData(",Alice,5,100,A")]
    [InlineData("R1,Alice")]
    [InlineData("R1,Alice,5,100,A,extra")]
    [InlineData("R1,Alice,5,heavy,A")]
    [InlineData("R1,Alice,5,-100,A")]
    [InlineData("R1,Alice,5,100,")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void InvalidOrMissingLine_IsRejectedWithMessage(string? line)
    {
        var accepted = RequestParser.TryParseRequest(line, out var request, out var error);

        Assert.False(accepted);
        Assert.Null(request);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ZeroDistance_IsValidInputAndNotReplacedByDefault()
    {
        var accepted = RequestParser.TryParseRequest("R6,Frank,0,300,A", out var request, out var error);

        Assert.True(accepted);
        Assert.Equal(string.Empty, error);
        Assert.Equal(new DeliveryRequest("R6", "Frank", 0m, 300, "A"), request);
    }

    [Fact]
    public void ParseCsvLines_ReportsEveryInvalidLineWithNumber()
    {
        var lines = new[]
        {
            "id,customer,distanceKm",
            "R1,Alice,2,100,A",
            "R2,Bob,-5,100,A",
            "R3,Carol,abc,100,A",
            "R4,,10,100,A",
            "R5,Erin"
        };

        var dataset = RequestParser.ParseCsvLines(lines);

        Assert.Single(dataset.Requests);
        Assert.Equal(4, dataset.Errors.Length);
        Assert.StartsWith("Line 3:", dataset.Errors[0]);
        Assert.StartsWith("Line 6:", dataset.Errors[3]);
    }

    [Fact]
    public void MissingInput_ThrowsInsteadOfReturningSuccess()
    {
        Assert.Throws<ArgumentNullException>(() => RequestParser.ParseCsvLines(null));
        Assert.Throws<ArgumentNullException>(() => QuotePipeline.Run(null!, Limits, PricePolicies.Standard));
        Assert.Throws<ArgumentNullException>(() => QuotePipeline.Run(SampleRequests(), Limits, null!));
    }

    [Fact]
    public void TwoPolicies_GiveDifferentResultsOnSameInput()
    {
        var requests = SampleRequests();

        var standard = QuotePipeline.Run(requests, Limits, PricePolicies.Standard);
        var express = QuotePipeline.Run(requests, Limits, PricePolicies.Express);

        Assert.Equal(700m, standard.Quotes.Single(q => q.Id == "R1").Price);
        Assert.Equal(1100m, express.Quotes.Single(q => q.Id == "R1").Price);
        Assert.Equal(14800m, standard.TotalPrice);
        Assert.Equal(22600m, express.TotalPrice);
        Assert.Equal(standard.Quotes.Select(q => q.Id), express.Quotes.Select(q => q.Id));
    }

    [Fact]
    public void PriceRequests_InvokesDelegateOncePerRequest()
    {
        var requests = SampleRequests();
        var seenDistances = new List<decimal>();

        var priced = QuotePipeline.PriceRequests(requests, km =>
        {
            seenDistances.Add(km);
            return km * 2m;
        });

        Assert.Equal(requests.Length, seenDistances.Count);
        Assert.Equal(requests.Select(r => r.DistanceKm * 2m), priced.Select(p => p.Price));
    }

    [Fact]
    public void Stages_DoNotModifyCallerOwnedInput()
    {
        var requests = new List<DeliveryRequest>(SampleRequests());
        var requestsBefore = requests.ToArray();

        var deliverable = QuotePipeline.SelectDeliverable(requests, Limits);
        var deliverableBefore = deliverable.ToArray();
        var priced = QuotePipeline.PriceRequests(deliverable, PricePolicies.Express.PriceForDistance);
        var pricedBefore = priced.ToArray();
        QuotePipeline.BuildSummary(priced);

        Assert.Equal(requestsBefore, requests);
        Assert.Equal(deliverableBefore, deliverable);
        Assert.Equal(pricedBefore, priced);
    }

    [Fact]
    public void SelectDeliverable_ReturnsOnlyRequestsWithinLimits()
    {
        var deliverable = QuotePipeline.SelectDeliverable(SampleRequests(), Limits);

        Assert.Equal(new[] { "R1", "R2", "R3", "R5", "R7", "R8", "R9", "R10" }, deliverable.Select(r => r.Id));
    }

    [Fact]
    public void BuildSummary_OrdersByPriceThenIdAndSumsTotal()
    {
        var priced = new[]
        {
            new PricedRequest(new DeliveryRequest("B", "Bob", 5m, 100, "A"), 100m),
            new PricedRequest(new DeliveryRequest("A", "Ann", 5m, 100, "A"), 100m),
            new PricedRequest(new DeliveryRequest("C", "Cid", 1m, 100, "A"), 50m)
        };

        var summary = QuotePipeline.BuildSummary(priced);

        Assert.Equal(new[] { "C", "A", "B" }, summary.Quotes.Select(q => q.Id));
        Assert.Equal(250m, summary.TotalPrice);
    }

    [Fact]
    public void SameInputAndPolicy_ProduceSameOutput()
    {
        var requests = SampleRequests();

        var first = QuotePipeline.Run(requests, Limits, PricePolicies.Express);
        var second = QuotePipeline.Run(requests, Limits, PricePolicies.Express);

        Assert.Equal(first.Quotes.OrderBy(q => q.Id), second.Quotes.OrderBy(q => q.Id));
        Assert.Equal(first.TotalPrice, second.TotalPrice);
    }
}
