using Relay.Domain;

namespace Relay.Application.Baseline;

/// <summary>
/// Pure windowing/completeness logic (Requirements §2, §"Week completeness"). Takes a dense,
/// ascending spine of <see cref="WeekObservation"/> ending at the viewed week and turns it into a
/// baseline mean, tolerance band and per-week series — no I/O, no database.
/// </summary>
public sealed class BaselineService
{
    public BaselineResult Build(
        IReadOnlyList<WeekObservation> spine,
        WeekRange viewedWeek,
        int requestedWindow,
        TileKind kind,
        ThresholdSet thresholds)
    {
        if (spine.Count == 0 || spine[^1].WeekStart != viewedWeek.Start)
        {
            throw new ArgumentException(
                "Spine must be dense, ascending, and end at the viewed week.", nameof(spine));
        }

        var viewedObservation = spine[^1];

        // Weeks -window .. -1, oldest first, viewed week always excluded.
        var candidates = spine.Take(spine.Count - 1).ToList();
        if (candidates.Count > requestedWindow)
        {
            candidates = candidates.Skip(candidates.Count - requestedWindow).ToList();
        }

        var weeksEffective = candidates.Count;
        var points = new List<SeriesPoint>(spine.Count);
        var contributingValues = new List<decimal>();

        foreach (var observation in candidates)
        {
            var (included, reason) = Classify(observation, kind, thresholds);
            if (included && observation.Value is { } value)
            {
                contributingValues.Add(value);
            }

            points.Add(new SeriesPoint(
                observation.WeekStart, observation.Value, observation.Denominator,
                observation.DaysIncluded, observation.ExpectedDays,
                IncludedInBaseline: included, ExclusionReason: reason, IsViewedWeek: false));
        }

        // The viewed week is never part of the baseline, but its own completeness/denominator is
        // still worth disclosing on the sparkline (a partial or thin viewed week gets flagged too).
        var (_, viewedReason) = Classify(viewedObservation, kind, thresholds);
        points.Add(new SeriesPoint(
            viewedObservation.WeekStart, viewedObservation.Value, viewedObservation.Denominator,
            viewedObservation.DaysIncluded, viewedObservation.ExpectedDays,
            IncludedInBaseline: false, ExclusionReason: viewedReason, IsViewedWeek: true));

        var weeksContributing = contributingValues.Count;
        decimal? mean = weeksContributing > 0 ? contributingValues.Average() : null;

        // Unclamped: Requirements §"Percentage points, not percentages, on rate tiles" gives
        // `completed` at an 82.3% baseline a bandHigh of 115% and puts the 100% clamp at
        // "display" — the API reports the arithmetic the tolerance control actually produces, and
        // the frontend clamps it for rendering. It never affects a verdict either way.
        decimal? bandLow = null;
        decimal? bandHigh = null;
        if (requestedWindow != 1 && mean is not null)
        {
            var spread = mean.Value * (thresholds.TolerancePct / 100m);
            bandLow = mean.Value - spread;
            bandHigh = mean.Value + spread;
        }

        return new BaselineResult(
            mean, bandLow, bandHigh, requestedWindow, weeksEffective, weeksContributing, points);
    }

    /// <summary>Whether one baseline-candidate week contributes to the mean, and why not if it
    /// doesn't. Count tiles gate on day-completeness and never prorate; rate tiles gate on the
    /// denominator, because numerator and denominator lose the same days on an incomplete week.</summary>
    private static (bool Included, SeriesExclusionReason? Reason) Classify(
        WeekObservation observation, TileKind kind, ThresholdSet thresholds)
    {
        if (kind == TileKind.Count)
        {
            if (observation.Value is null)
            {
                // Defensive: a count is never null by contract; treat an unexpected null as
                // partial rather than let it reach the mean as an unchecked `!`.
                return (false, SeriesExclusionReason.PartialWeek);
            }

            var completeness = observation.ExpectedDays > 0
                ? (decimal)observation.DaysIncluded / observation.ExpectedDays
                : 0m;
            return completeness < thresholds.MinWeekCompleteness
                ? (false, SeriesExclusionReason.PartialWeek)
                : (true, null);
        }

        if (observation.Denominator is null || observation.Value is null)
        {
            return (false, SeriesExclusionReason.NoDenominator);
        }

        return observation.Denominator < thresholds.MinRateDenominator
            ? (false, SeriesExclusionReason.BelowMinDenominator)
            : (true, null);
    }
}
