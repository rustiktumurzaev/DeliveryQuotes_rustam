using DeliveryQuotes;
using Xunit;
using static DeliveryQuotes.Tests.TestSupport;

namespace DeliveryQuotes.Tests;

public class ResultTests
{
    private static readonly Error Boom = new(ErrorCode.InvalidState, "boom");

    [Fact]
    public void Map_Success_ReceivesValueAndReturnsNewValue()
    {
        var mapped = Result<int>.Ok(20).Map(x => x + 1);

        Assert.Equal(21, Value(mapped));
    }

    [Fact]
    public void Bind_Success_PassesValueAndKeepsTheNextResult()
    {
        var bound = Result<int>.Ok(20).Bind(x => Result<string>.Ok($"v{x}"));
        var boundFailure = Result<int>.Ok(20).Bind(_ => Result<string>.Fail(Boom));

        Assert.Equal("v20", Value(bound));
        Assert.Equal(Boom, Failure(boundFailure));
    }

    [Fact]
    public void Map_Failure_IsPreservedAndFunctionIsNotCalled()
    {
        var calls = 0;

        var mapped = Result<int>.Fail(Boom).Map(x =>
        {
            calls++;
            return x + 1;
        });

        Assert.Equal(0, calls);
        Assert.Equal(Boom, Failure(mapped));
    }

    [Fact]
    public void Bind_Failure_IsPreservedAndFunctionIsNotCalled()
    {
        var calls = 0;

        var bound = Result<int>.Fail(Boom).Bind(x =>
        {
            calls++;
            return Result<int>.Ok(x);
        });

        Assert.Equal(0, calls);
        Assert.Equal(Boom, Failure(bound));
    }

    [Fact]
    public void Chain_FirstFailureSkipsAllLaterSteps()
    {
        var laterCalls = 0;

        var result = Result<int>.Ok(1)
            .Bind(_ => Result<int>.Fail(Boom))
            .Map(x =>
            {
                laterCalls++;
                return x;
            })
            .Bind(x =>
            {
                laterCalls++;
                return Result<int>.Ok(x);
            });

        Assert.Equal(0, laterCalls);
        Assert.Equal(Boom, Failure(result));
    }

    [Fact]
    public void Ok_WithNull_IsRejectedSoSuccessAlwaysHasAValue()
    {
        Assert.Throws<ArgumentNullException>(() => Result<string>.Ok(null!));
        Assert.Throws<ArgumentNullException>(() => Result<string>.Fail((Error)null!));
    }

    [Fact]
    public void Match_CallsOnlyTheMatchingBranch()
    {
        var ok = Result<int>.Ok(5).Match(v => $"ok {v}", e => $"fail {e.Message}");
        var fail = Result<int>.Fail(Boom).Match(v => $"ok {v}", e => $"fail {e.Message}");

        Assert.Equal("ok 5", ok);
        Assert.Equal("fail boom", fail);
    }

    [Fact]
    public void Maybe_PresentAbsentAndLegitimateZero()
    {
        var dictionary = new Dictionary<string, int> { ["zero"] = 0, ["one"] = 1 };

        var zero = dictionary.Lookup("zero");
        var absent = dictionary.Lookup("nothing");

        Assert.Equal(0, Assert.IsType<Maybe<int>.Some>(zero).Value);
        Assert.IsType<Maybe<int>.None>(absent);
    }

    [Fact]
    public void Maybe_ToResult_MissingBecomesMeaningfulFailure()
    {
        var found = Maybe.Some(0).ToResult(() => Boom);
        var missing = Maybe.None<int>().ToResult(() => new Error(ErrorCode.NotFound, "no item"));

        Assert.Equal(0, Value(found));
        Assert.Equal(ErrorCode.NotFound, Failure(missing).Code);
    }
}
