namespace DeliveryQuotes;

// A lookup that may find nothing. None means "there is no such item"; it is not the same as
// zero, false or an empty string, which are legitimate values and come back as Some.
public abstract record Maybe<T>
{
    private Maybe()
    {
    }

    public sealed record Some(T Value) : Maybe<T>;

    public sealed record None : Maybe<T>;
}

public static class Maybe
{
    public static Maybe<T> Some<T>(T value) => new Maybe<T>.Some(value);

    public static Maybe<T> None<T>() => new Maybe<T>.None();

    public static Maybe<TValue> Lookup<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> source, TKey key)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.TryGetValue(key, out var value) ? Some(value) : None<TValue>();
    }

    // When the workflow needs the item, a missing one becomes a meaningful failure.
    public static Result<T> ToResult<T>(this Maybe<T> maybe, Func<Error> onMissing)
    {
        ArgumentNullException.ThrowIfNull(onMissing);

        return maybe switch
        {
            Maybe<T>.Some some => Result<T>.Ok(some.Value),
            _ => Result<T>.Fail(onMissing())
        };
    }
}
