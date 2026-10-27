namespace DeliveryQuotes;

// Explicit outcome of an operation that can fail for an expected reason.
//   Success carries the value (never null).
//   Failure carries an Error.
// The constructors are internal and the only public way in is Ok/Fail, so there is no
// "success without a value" and no "failure that still has a value".
public abstract record Result<T>
{
    private Result()
    {
    }

    public sealed record Success : Result<T>
    {
        public T Value { get; }

        internal Success(T value) => Value = value;
    }

    public sealed record Failure : Result<T>
    {
        public Error Error { get; }

        internal Failure(Error error) => Error = error;
    }

    public static Result<T> Ok(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Success(value);
    }

    public static Result<T> Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Failure(error);
    }

    public static Result<T> Fail(ErrorCode code, string message) => Fail(new Error(code, message));
}

public static class ResultExtensions
{
    // Map: apply a plain function to a success. A failure passes through and f is NOT called.
    public static Result<TOut> Map<T, TOut>(this Result<T> result, Func<T, TOut> f)
    {
        ArgumentNullException.ThrowIfNull(f);

        return result switch
        {
            Result<T>.Success success => Result<TOut>.Ok(f(success.Value)),
            Result<T>.Failure failure => Result<TOut>.Fail(failure.Error),
            _ => Result<TOut>.Fail(ErrorCode.InvalidState, "Unknown result case.")
        };
    }

    // Bind: continue with a step that can fail itself. A failure passes through and f is NOT called.
    public static Result<TOut> Bind<T, TOut>(this Result<T> result, Func<T, Result<TOut>> f)
    {
        ArgumentNullException.ThrowIfNull(f);

        return result switch
        {
            Result<T>.Success success => f(success.Value),
            Result<T>.Failure failure => Result<TOut>.Fail(failure.Error),
            _ => Result<TOut>.Fail(ErrorCode.InvalidState, "Unknown result case.")
        };
    }

    // Match: the one place where the final outcome is turned into something else (text, exit code).
    public static TOut Match<T, TOut>(this Result<T> result, Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return result switch
        {
            Result<T>.Success success => onSuccess(success.Value),
            Result<T>.Failure failure => onFailure(failure.Error),
            _ => onFailure(new Error(ErrorCode.InvalidState, "Unknown result case."))
        };
    }
}
