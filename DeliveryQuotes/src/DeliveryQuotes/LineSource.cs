namespace DeliveryQuotes;

// Lazy line reader with a clear owner for the resource.
//
//   Owner:        the iterator below. It opens the reader on the first MoveNext, not earlier.
//   Release point: the `using` ends the reader when enumeration finishes, throws, or is stopped
//                  early (foreach + break, Take(n), ...), because disposing the enumerator runs
//                  the finally block of the using.
// Callers must not keep the enumerable around and expect the reader to stay open.
public static class LineSource
{
    public static IEnumerable<string> ReadLines(Func<TextReader> open)
    {
        ArgumentNullException.ThrowIfNull(open); // checked now, not at the first MoveNext
        return Iterate(open);
    }

    private static IEnumerable<string> Iterate(Func<TextReader> open)
    {
        using var reader = open();

        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
