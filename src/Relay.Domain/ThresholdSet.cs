namespace Relay.Domain;

public sealed record ThresholdSet(
    int MinBaselineEvents = 5,       // audit: greys 51% of tiles, removes 62% of breaches
    int MinRateDenominator = 20,     // ±9.7pp SE at n=20
    int MinHistoryWeeks = 4,
    decimal MinWeekCompleteness = 6m / 7m,
    decimal AmberFraction = 0.8m,
    decimal TolerancePct = 40m)      // audit: 25% -> 42.7% red; 40% -> 22.6%
{
    public static ThresholdSet Defaults { get; } = new();
}
