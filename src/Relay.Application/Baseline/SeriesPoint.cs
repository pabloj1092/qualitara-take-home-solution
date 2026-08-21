using Relay.Domain;

namespace Relay.Application.Baseline;

public enum SeriesExclusionReason
{
    PartialWeek,
    DataQualityExclusion,
    BelowMinDenominator,
    NoDenominator,
}

/// <summary>One calendar week of a tile's series. Nothing is ever omitted: a week with no data
/// is a point, not a gap.</summary>
public sealed record SeriesPoint(
    DateOnly WeekStart,
    decimal? Value,
    int? Denominator,
    int DaysIncluded,
    int ExpectedDays,
    bool IncludedInBaseline,
    SeriesExclusionReason? ExclusionReason,
    bool IsViewedWeek);
