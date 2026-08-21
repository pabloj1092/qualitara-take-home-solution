using Relay.Application.Baseline;
using Relay.Application.Status;
using Relay.Domain;

namespace Relay.Tests.Unit;

/// <summary>
/// A handful of smoke assertions proving the arithmetic runs, written before the database exists
/// (Stage 1). Not the §1/§2 table-driven suite — that lands in Stage 3's
/// <c>StatusEvaluatorTests</c> / <c>BaselineServiceTests</c>.
/// </summary>
public class PureCoreSmokeTests
{
    private static readonly ThresholdSet Thresholds = ThresholdSet.Defaults;

    [Fact]
    public void BaselineService_AveragesCompleteWeeks_AndBandsAtTolerance()
    {
        var viewedWeek = WeekRange.FromIsoWeek("2026-W30");
        var spine = viewedWeek.Preceding(8)
            .Select(w => new WeekObservation(w.Start, 10m, null, 7, 7))
            .Append(new WeekObservation(viewedWeek.Start, 12m, null, 7, 7))
            .ToList();

        var result = new BaselineService().Build(spine, viewedWeek, 8, TileKind.Count, Thresholds);

        Assert.Equal(10m, result.Mean);
        Assert.Equal(8, result.WeeksEffective);
        Assert.Equal(8, result.WeeksContributing);
        Assert.Equal(6m, result.BandLow);
        Assert.Equal(14m, result.BandHigh);
        Assert.Equal(9, result.Series.Count);
        Assert.True(result.Series[^1].IsViewedWeek);
    }

    [Fact]
    public void StatusEvaluator_BelowMinBaselineEvents_IsInsufficientData()
    {
        var baseline = new BaselineResult(4.9m, null, null, 8, 8, 8, []);

        var result = new StatusEvaluator().Evaluate(
            5m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.InsufficientData, result.Status);
        Assert.Equal(ReasonCode.BaselineBelowMinEvents, result.Reason);
        Assert.Null(result.DeltaPct);
    }

    [Fact]
    public void StatusEvaluator_GoodOutcome60PercentBelowBaseline_Breaches()
    {
        var baseline = new BaselineResult(50m, 30m, 70m, 8, 8, 8, []);

        var result = new StatusEvaluator().Evaluate(
            20m, 100, 7, 7, baseline, OutcomePolarity.Good, TileKind.Rate, Thresholds);

        Assert.Equal(TileStatus.Breach, result.Status);
        Assert.Equal(ReasonCode.OutsideTolerance, result.Reason);
        Assert.Equal(-60m, result.DeltaPct);
    }

    [Fact]
    public void StatusEvaluator_NeutralPolarity_NeverBreaches()
    {
        var baseline = new BaselineResult(10m, 6m, 14m, 8, 8, 8, []);

        var result = new StatusEvaluator().Evaluate(
            30m, 100, 7, 7, baseline, OutcomePolarity.Neutral, TileKind.Rate, Thresholds);

        Assert.Equal(TileStatus.Normal, result.Status);
        Assert.Equal(ReasonCode.NeutralPolarity, result.Reason);
    }

    [Fact]
    public void StatusEvaluator_RateTile_LowBaselineMean_IsNotGatedByMinBaselineEvents()
    {
        // Rate tiles gate on the denominator, not the mean (Requirements §"Defining
        // min_baseline_events") — a 3% baseline mean must not read as BaselineBelowMinEvents.
        var baseline = new BaselineResult(3m, 1.8m, 4.2m, 8, 8, 8, []);

        var result = new StatusEvaluator().Evaluate(
            3.1m, 100, 7, 7, baseline, OutcomePolarity.Good, TileKind.Rate, Thresholds);

        Assert.NotEqual(TileStatus.InsufficientData, result.Status);
        Assert.NotEqual(ReasonCode.BaselineBelowMinEvents, result.Reason);
    }

    [Fact]
    public void StatusEvaluator_ZeroBaselineMean_IsBaselineZeroNotBelowMinEvents()
    {
        var baseline = new BaselineResult(0m, 0m, 0m, 8, 8, 8, []);

        var result = new StatusEvaluator().Evaluate(
            5m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.InsufficientData, result.Status);
        Assert.Equal(ReasonCode.BaselineZero, result.Reason);
    }

    [Fact]
    public void StatusEvaluator_NullMean_IsInsufficientHistoryNotBelowMinEvents()
    {
        var baseline = new BaselineResult(null, null, null, 8, 8, 0, []);

        var result = new StatusEvaluator().Evaluate(
            5m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.InsufficientData, result.Status);
        Assert.Equal(ReasonCode.InsufficientHistory, result.Reason);
    }

    [Fact]
    public void StatusEvaluator_ViewedWeekWithNoRowsAtAll_IsPartialWeek()
    {
        var baseline = new BaselineResult(10m, 6m, 14m, 8, 8, 8, []);

        // ExpectedDays == 0 (no rows at all for the viewed week) must not read as "0 of 0 days, complete".
        var result = new StatusEvaluator().Evaluate(
            0m, null, 0, 0, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.PartialWeek, result.Status);
        Assert.Equal(ReasonCode.ViewedWeekPartial, result.Reason);
    }

    [Fact]
    public void BaselineService_NullValueCandidateWeek_ExcludedRatherThanThrown()
    {
        var viewedWeek = WeekRange.FromIsoWeek("2026-W30");
        var spine = viewedWeek.Preceding(2)
            .Select(w => new WeekObservation(w.Start, null, null, 7, 7))
            .Append(new WeekObservation(viewedWeek.Start, 5m, null, 7, 7))
            .ToList();

        var result = new BaselineService().Build(spine, viewedWeek, 2, TileKind.Count, Thresholds);

        Assert.Equal(0, result.WeeksContributing);
        Assert.Null(result.Mean);
        Assert.All(result.Series.Where(p => !p.IsViewedWeek), p => Assert.False(p.IncludedInBaseline));
    }

    [Fact]
    public void BaselineService_RateBand_IsNotClampedAt100()
    {
        // Requirements §"Percentage points, not percentages, on rate tiles": completed at an
        // 82.3% baseline yields a bandHigh of 115% — the API reports the raw arithmetic, and
        // clamping for display is the frontend's job.
        var viewedWeek = WeekRange.FromIsoWeek("2026-W30");
        var spine = viewedWeek.Preceding(1)
            .Select(w => new WeekObservation(w.Start, 82.3m, 100, 7, 7))
            .Append(new WeekObservation(viewedWeek.Start, 82.3m, 100, 7, 7))
            .ToList();

        var result = new BaselineService().Build(spine, viewedWeek, 1, TileKind.Rate, Thresholds);

        Assert.Null(result.BandLow);
        Assert.Null(result.BandHigh);

        var result8 = new BaselineService().Build(
            viewedWeek.Preceding(8)
                .Select(w => new WeekObservation(w.Start, 82.3m, 100, 7, 7))
                .Append(new WeekObservation(viewedWeek.Start, 82.3m, 100, 7, 7))
                .ToList(),
            viewedWeek, 8, TileKind.Rate, Thresholds);

        Assert.Equal(115.22m, result8.BandHigh);
    }
}
