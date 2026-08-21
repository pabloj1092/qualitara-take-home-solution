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
}
