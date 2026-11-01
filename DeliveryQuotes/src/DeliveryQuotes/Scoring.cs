namespace DeliveryQuotes;

public sealed record ScoreConfig(decimal WeightFactor, decimal DistanceFactor);

// Dispatch priority of an accepted order. Higher score = should be dispatched earlier.
public static class Scoring
{
    // Curried: weightFactor -> distanceFactor -> (order line -> score)
    //   score = (weight in kg) * weightFactor + (distance in km) * distanceFactor
    public static Func<decimal, Func<decimal, Func<OrderLine, decimal>>> Priority { get; } =
        weightFactor => distanceFactor => line =>
            line.WeightGrams / 1000m * weightFactor + line.DistanceKm * distanceFactor;

    // CreateScore : Configuration -> (OrderLine -> Score)
    public static Func<OrderLine, decimal> CreateScore(ScoreConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return Priority(config.WeightFactor)(config.DistanceFactor);
    }

    // Partial application: two configured functions made from the same curried function.
    public static Func<OrderLine, decimal> HeavyFirst { get; } = Priority(2m)(0.5m);

    public static Func<OrderLine, decimal> FarFirst { get; } = Priority(0.5m)(2m);
}
