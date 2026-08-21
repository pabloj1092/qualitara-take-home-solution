using Relay.Application.Baseline;
using Relay.Application.Status;
using Relay.Domain;

namespace Relay.Tests.Unit;

/// <summary>Requirements §2 "Baseline construction" — one test per bullet.</summary>
public class BaselineServiceTests
{
    private static readonly ThresholdSet Thresholds = ThresholdSet.Defaults;

    [Fact]
    public void Window8EndingAtViewedWeek_ExcludesViewedWeek_PreservingABreachItWouldOtherwiseHide()
    {
        var viewedWeek = WeekRange.FromIsoWeek("2026-W30");
        var spine = viewedWeek.Preceding(8)
            .Select(w => new WeekObservation(w.Start, 100m, null, 7, 7))
            .Append(new WeekObservation(viewedWeek.Start, 20m, null, 7, 7)) // a steep drop
            .ToList();

        var baseline = new BaselineService().Build(spine, viewedWeek, 8, TileKind.Count, Thresholds);

        // The viewed week's 20 never enters its own baseline.
        Assert.Equal(100m, baseline.Mean);
        Assert.Equal(8, baseline.WeeksContributing);

        var status = new StatusEvaluator().Evaluate(
            20m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        // Had the viewed week leaked into its own baseline (the degenerate case of a
        // spine[^1]-inclusion bug, where the mean collapses to the viewed value itself), deltaPct
        // would be exactly 0% and this would misreport Normal instead of Breach.
        Assert.Equal(TileStatus.Breach, status.Status);
    }

    [Fact]
    public void ZeroEventWeek_IsIncludedAsZero_RaisingTheMeanEnoughToFlipNormalToBreachIfSkipped()
    {
        // Window=5 (not fewer) so WeeksContributing clears MinHistoryWeeks=4 in *both* the
        // included and the skipped variant below — otherwise the skipped variant's dropped
        // WeeksContributing (4, one week short of 5) would trip StatusEvaluator's InsufficientData
        // rung before the Breach/Normal difference this test is actually about ever came into play.
        var viewedWeek = WeekRange.FromIsoWeek("2026-W30");
        var precedingWeeks = viewedWeek.Preceding(5).ToList();

        var spineWithZero = precedingWeeks.Take(4)
            .Select(w => new WeekObservation(w.Start, 100m, null, 7, 7))
            .Append(new WeekObservation(precedingWeeks[4].Start, 0m, null, 7, 7)) // a real zero-event week
            .Append(new WeekObservation(viewedWeek.Start, 60m, null, 7, 7))
            .ToList();

        var baselineWithZero = new BaselineService().Build(spineWithZero, viewedWeek, 5, TileKind.Count, Thresholds);

        Assert.Equal(5, baselineWithZero.WeeksContributing);
        Assert.Equal(80m, baselineWithZero.Mean); // (100×4 + 0) / 5 — the zero week counts as a real point

        var statusWithZero = new StatusEvaluator().Evaluate(
            60m, null, 7, 7, baselineWithZero, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.Normal, statusWithZero.Status); // -25%, comfortably inside tolerance

        // If the zero-event week were wrongly treated as missing (value: null) rather than a real
        // 0, BaselineService would drop it as PartialWeek instead of averaging it in — only the
        // four 100-weeks survive, the mean is pulled up to 100, and the identical viewed value of
        // 60 now reads as a 40% drop: Breach instead of Normal.
        var spineSkippingZero = precedingWeeks.Take(4)
            .Select(w => new WeekObservation(w.Start, 100m, null, 7, 7))
            .Append(new WeekObservation(precedingWeeks[4].Start, null, null, 7, 7))
            .Append(new WeekObservation(viewedWeek.Start, 60m, null, 7, 7))
            .ToList();

        var baselineSkippingZero = new BaselineService().Build(spineSkippingZero, viewedWeek, 5, TileKind.Count, Thresholds);

        Assert.Equal(4, baselineSkippingZero.WeeksContributing);
        Assert.Equal(100m, baselineSkippingZero.Mean);

        var statusSkippingZero = new StatusEvaluator().Evaluate(
            60m, null, 7, 7, baselineSkippingZero, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.Breach, statusSkippingZero.Status);
    }

    [Fact]
    public void WeekBelowMinWeekCompleteness_IsDropped_AndWeeksContributingReportsTheShrink()
    {
        var viewedWeek = WeekRange.FromIsoWeek("2026-W30");
        var weeks = viewedWeek.Preceding(4).ToList();

        var spine = new List<WeekObservation>
        {
            new(weeks[0].Start, 10m, null, 7, 7),
            new(weeks[1].Start, 12m, null, 3, 7), // 3/7 < 6/7 — below MinWeekCompleteness, dropped
            new(weeks[2].Start, 11m, null, 7, 7),
            new(weeks[3].Start, 9m, null, 7, 7),
            new(viewedWeek.Start, 10m, null, 7, 7),
        };

        var baseline = new BaselineService().Build(spine, viewedWeek, 4, TileKind.Count, Thresholds);

        // WeeksEffective is the window-size clamp (all 4 candidates existed and were considered);
        // WeeksContributing — what the API reports as baselineWeeksUsed — is what actually fed the
        // mean once the incomplete week was dropped, and it does not prorate the missing one in.
        Assert.Equal(4, baseline.WeeksEffective);
        Assert.Equal(3, baseline.WeeksContributing);
        Assert.Equal(10m, baseline.Mean); // (10 + 11 + 9) / 3 — the 3/7 week excluded, not prorated

        var droppedPoint = baseline.Series.Single(p => p.WeekStart == weeks[1].Start);
        Assert.False(droppedPoint.IncludedInBaseline);
        Assert.Equal(SeriesExclusionReason.PartialWeek, droppedPoint.ExclusionReason);
    }

    [Fact]
    public void ViewedWeek3OfAccountHistory_Window8ClampsTo2_AndReportsIt()
    {
        var viewedWeek = WeekRange.FromIsoWeek("2026-W03"); // the account's 3rd calendar week
        var precedingWeeks = viewedWeek.Preceding(2).ToList(); // only 2 weeks of history exist before it

        var spine = precedingWeeks
            .Select(w => new WeekObservation(w.Start, 10m, null, 7, 7))
            .Append(new WeekObservation(viewedWeek.Start, 10m, null, 7, 7))
            .ToList();

        var baseline = new BaselineService().Build(spine, viewedWeek, requestedWindow: 8, TileKind.Count, Thresholds);

        Assert.Equal(8, baseline.WeeksRequested);
        Assert.Equal(2, baseline.WeeksEffective); // clamped down from 8 — only 2 candidate weeks exist
        Assert.Equal(2, baseline.WeeksContributing);
    }

    [Fact]
    public void Window1_BaselineIsThePreviousWeeksValue_AndReturnsNoBand()
    {
        var viewedWeek = WeekRange.FromIsoWeek("2026-W30");
        const decimal previousWeekValue = 42m;
        var spine = new List<WeekObservation>
        {
            new(viewedWeek.Previous().Start, previousWeekValue, null, 7, 7),
            new(viewedWeek.Start, 42m, null, 7, 7), // no deviation at all from the "baseline"
        };

        var baseline = new BaselineService().Build(spine, viewedWeek, requestedWindow: 1, TileKind.Count, Thresholds);

        Assert.Equal(previousWeekValue, baseline.Mean); // window=1's baseline IS the previous week's value
        Assert.Null(baseline.BandLow);
        Assert.Null(baseline.BandHigh);

        // Per the resolved design decision (window ∈ {1,2,3} is intentionally always grey):
        // min_history_weeks is an absolute reliability floor on the baseline, independent of why
        // the window is short, so StatusEvaluator still reports InsufficientData here even though
        // BaselineService computed a perfectly well-defined mean and a zero deviation.
        var status = new StatusEvaluator().Evaluate(
            42m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.InsufficientData, status.Status);
        Assert.Equal(ReasonCode.InsufficientHistory, status.Reason);
    }
}
