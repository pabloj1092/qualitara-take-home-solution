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
/// <param name="OverlapsExclusion">
/// Whether this week's date range overlaps a disclosed <c>data_quality_exclusions</c> row,
/// independent of <paramref name="ExclusionReason"/>. A week can overlap an exclusion and still
/// clear the completeness floor — the D1 week for account 6 (2026-06-01) removes exactly 1 of 7
/// days, landing at completeness == min_week_completeness exactly, which is not below the
/// threshold — so <see cref="ExclusionReason"/> stays <c>null</c> there. Without this separate
/// flag the sparkline would have no signal to hatch that point on at all.
/// </param>
public sealed record SeriesPoint(
    DateOnly WeekStart,
    decimal? Value,
    int? Denominator,
    int DaysIncluded,
    int ExpectedDays,
    bool IncludedInBaseline,
    SeriesExclusionReason? ExclusionReason,
    bool IsViewedWeek,
    bool OverlapsExclusion = false);
