using System.Collections.Immutable;
using DeliveryQuotes;
using Xunit;

namespace DeliveryQuotes.Tests;

public class QuotePipelineTests
{
    private static readonly string[] SampleLines =
    {
        "id,customer,distanceKm",
        "R1,Alice,2",
        "R2,Bob,10",
        "R3,Carol,50",
        "R4,Dave,75",
        "R5,Erin,1",
        "R6,Frank,0"
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

        Assert.Equal(new[] { "R5", "R1", "R2", "R3" }, summary.Quotes.Select(q => q.Id));
        Assert.Equal(new[] { 600m, 700m, 1500m, 5500m }, summary.Quotes.Select(q => q.Price));
        Assert.Equal(4, summary.Count);
        Assert.Equal(8300m, summary.TotalPrice);
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
            new DeliveryRequest("MIN", "A", 1m),
            new DeliveryRequest("MAX", "B", 50m),
            new DeliveryRequest("BELOW", "C", 0.99m),
            new DeliveryRequest("ABOVE", "D", 50.01m),
            new DeliveryRequest("ZERO", "E", 0m)
        };

        var summary = QuotePipeline.Run(requests, Limits, PricePolicies.Standard);

        Assert.Equal(new[] { "MIN", "MAX" }, summary.Quotes.Select(q => q.Id));
        Assert.Equal(new[] { 600m, 5500m }, summary.Quotes.Select(q => q.Price));
    }

    [Theory]
    [InlineData("R1,Alice,-5")]
    [InlineData("R1,Alice,abc")]
    [InlineData("R1,Alice,")]
    [InlineData("R1,,5")]
    [InlineData(",Alice,5")]
    [InlineData("R1,Alice")]
    [InlineData("R1,Alice,5,extra")]
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
        var accepted = RequestParser.TryParseRequest("R6,Frank,0", out var request, out var error);

        Assert.True(accepted);
        Assert.Equal(string.Empty, error);
        Assert.Equal(new DeliveryRequest("R6", "Frank", 0m), request);
    }

    [Fact]
    public void ParseCsvLines_ReportsEveryInvalidLineWithNumber()
    {
        var lines = new[]
        {
            "id,customer,distanceKm",
            "R1,Alice,2",
            "R2,Bob,-5",
            "R3,Carol,abc",
            "R4,,10",
            "R5,Erin"
        };

        var dataset = RequestParser.ParseCsvLines(lines);

        Assert.Single(dataset.Requests);
        Assert.Equal(4, dataset.Errors.Length);
        Assert.StartsWith("Line 3:", dataset.Errors[0]);
        Assert.StartsWith("Line 6:", dataset.Errors[3]);
    }

    [Fact]
    public void EmptyFileOrWrongHeader_IsRejectedAndIsNotAnEmptyDataset()
    {
        var noLines = RequestParser.ParseCsvLines(Array.Empty<string>());
        var wrongHeader = RequestParser.ParseCsvLines(new[] { "R1,Alice,2" });
        var headerOnly = RequestParser.ParseCsvLines(new[] { "id,customer,distanceKm" });

        Assert.Single(noLines.Errors);
        Assert.Empty(noLines.Requests);
        Assert.StartsWith("Line 1:", wrongHeader.Errors[0]);
        Assert.Empty(wrongHeader.Requests);
        Assert.Empty(headerOnly.Errors);   // valid empty dataset
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
        Assert.Equal(8300m, standard.TotalPrice);
        Assert.Equal(12650m, express.TotalPrice);
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

        Assert.Equal(new[] { "R1", "R2", "R3", "R5" }, deliverable.Select(r => r.Id));
    }

    [Fact]
    public void BuildSummary_OrdersByPriceThenIdAndSumsTotal()
    {
        var priced = new[]
        {
            new PricedRequest(new DeliveryRequest("B", "Bob", 5m), 100m),
            new PricedRequest(new DeliveryRequest("A", "Ann", 5m), 100m),
            new PricedRequest(new DeliveryRequest("C", "Cid", 1m), 50m)
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

        Assert.Equal(
            first.Quotes.Select(q => new { q.Id, q.Customer, q.DistanceKm, q.Price }),
            second.Quotes.Select(q => new { q.Id, q.Customer, q.DistanceKm, q.Price })
        );
        Assert.Equal(first.TotalPrice, second.TotalPrice);
    }
}
