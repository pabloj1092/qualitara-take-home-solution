namespace Relay.Domain;

/// <summary>
/// One dense weekly data point — the boundary type the database hands to the pure core.
/// <paramref name="Value"/> is the count (count tiles) or the pooled rate (rate tiles);
/// <paramref name="Denominator"/> is populated on rate tiles only.
/// </summary>
public sealed record WeekObservation(
    DateOnly WeekStart,
    decimal? Value,
    int? Denominator,
    int DaysIncluded,
    int ExpectedDays);
