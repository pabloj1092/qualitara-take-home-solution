using Relay.Application.Baseline;
using Relay.Application.Status;
using Relay.Domain;

namespace Relay.Tests.Unit;

/// <summary>
/// Requirements §1 "Status evaluation" — one named case per row of the status-ladder table.
/// Written directly off the spec table, not off <see cref="StatusEvaluator"/>'s own code: five
/// ladder bugs were found in review by reading the table against the implementation, and a test
/// derived from the implementation would have rubber-stamped every one of them.
///
/// Every case asserts <c>Reason</c>, not just <c>Status</c> — and three of them (2, 9, 12)
/// deliberately construct a second, coincident InsufficientData condition alongside the one the
/// table names, to prove the ladder's rung order picks the one the table specifies rather than
/// whichever happens to be checked first in some other order.
/// </summary>
public class StatusEvaluatorTests
{
    private static readonly ThresholdSet Thresholds = ThresholdSet.Defaults; // MinBaselineEvents=5, MinRateDenominator=20, MinHistoryWeeks=4, TolerancePct=40, AmberFraction=0.8

    private static BaselineResult Baseline(
        decimal? mean, decimal? bandLow = null, decimal? bandHigh = null,
        int weeksEffective = 8, int weeksContributing = 8) =>
        new(mean, bandLow, bandHigh, WeeksRequested: 8, weeksEffective, weeksContributing, Series: []);

    [Fact]
    public void Row01_BaselineMean4_9_MinBaselineEvents5_IsInsufficientData()
    {
        var baseline = Baseline(4.9m);

        var result = new StatusEvaluator().Evaluate(
            5m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.InsufficientData, result.Status);
        Assert.Equal(ReasonCode.BaselineBelowMinEvents, result.Reason);
        Assert.Null(result.DeltaPct); // not a colour
    }

    [Fact]
    public void Row02_BaselineMean0_ViewedWeek5_IsInsufficientData_NeverBaselineBelowMinEvents()
    {
        // Coincidence: mean=0 also satisfies "< min_baseline_events" (0 < 5), so this also proves
        // the zero check is reached and wins before the below-minimum check ever runs.
        var baseline = Baseline(0m, 0m, 0m);

        var result = new StatusEvaluator().Evaluate(
            5m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.InsufficientData, result.Status);
        Assert.Equal(ReasonCode.BaselineZero, result.Reason);
        Assert.NotEqual(ReasonCode.BaselineBelowMinEvents, result.Reason);
        Assert.Null(result.DeltaPct); // never +∞%
    }

    [Fact]
    public void Row03_DeviationExactlyAtTolerance_IsBreach_BoundaryInclusive()
    {
        var baseline = Baseline(100m, 60m, 140m);

        var result = new StatusEvaluator().Evaluate(
            60m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.Breach, result.Status);
        Assert.Equal(ReasonCode.OutsideTolerance, result.Reason);
        Assert.Equal(-40m, result.DeltaPct);
    }

    [Fact]
    public void Row04_DeviationExactlyAtAmberFraction_IsWarning_BoundaryInclusive()
    {
        var baseline = Baseline(100m, 60m, 140m);

        var result = new StatusEvaluator().Evaluate(
            68m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds); // -32% = 0.8 × 40

        Assert.Equal(TileStatus.Warning, result.Status);
        Assert.Equal(ReasonCode.NearTolerance, result.Reason);
        Assert.Equal(-32m, result.DeltaPct);
    }

    [Fact]
    public void Row05_DeviationAHairUnderAmberFraction_IsNormal()
    {
        var baseline = Baseline(100m, 60m, 140m);

        var result = new StatusEvaluator().Evaluate(
            68.5m, null, 7, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds); // -31.5%, under 32

        Assert.Equal(TileStatus.Normal, result.Status);
        Assert.Equal(ReasonCode.WithinTolerance, result.Reason);
    }

    [Fact]
    public void Row06_BadOutcome60PercentBelowBaseline_IsNormal_GoodDirectionNeverWarns()
    {
        var baseline = Baseline(20m, 12m, 28m);

        var result = new StatusEvaluator().Evaluate(
            8m, 100, 7, 7, baseline, OutcomePolarity.Bad, TileKind.Rate, Thresholds); // -60%, but dropping is good for a bad outcome

        Assert.Equal(TileStatus.Normal, result.Status);
        Assert.Equal(ReasonCode.GoodDirection, result.Reason);
        Assert.Equal(-60m, result.DeltaPct);
    }

    [Fact]
    public void Row07_GoodOutcome60PercentBelowBaseline_IsBreach()
    {
        var baseline = Baseline(20m, 12m, 28m);

        var result = new StatusEvaluator().Evaluate(
            8m, 100, 7, 7, baseline, OutcomePolarity.Good, TileKind.Rate, Thresholds); // -60%, and dropping is bad for a good outcome

        Assert.Equal(TileStatus.Breach, result.Status);
        Assert.Equal(ReasonCode.OutsideTolerance, result.Reason);
        Assert.Equal(-60m, result.DeltaPct);
    }

    [Fact]
    public void Row08_VoicemailOrOpenAt200Percent_IsNormal_NeutralPolarityNeverBreaches()
    {
        var baseline = Baseline(10m, 6m, 14m);

        var result = new StatusEvaluator().Evaluate(
            30m, 100, 7, 7, baseline, OutcomePolarity.Neutral, TileKind.Rate, Thresholds); // +200%

        Assert.Equal(TileStatus.Normal, result.Status);
        Assert.Equal(ReasonCode.NeutralPolarity, result.Reason);
        Assert.Equal(200m, result.DeltaPct);
    }

    [Fact]
    public void Row09_ThreeWeeksOfHistory_MinHistoryWeeks4_IsInsufficientData_NeverDenominatorBelowMin()
    {
        // Coincidence: the viewed week's denominator (15) is also below min_rate_denominator (20).
        // An account too new to have four calendar weeks of history is the correct reason
        // regardless — no amount of volume this week fixes "too new".
        var baseline = Baseline(36.36m, weeksEffective: 3, weeksContributing: 3);

        var result = new StatusEvaluator().Evaluate(
            36.36m, 15, 7, 7, baseline, OutcomePolarity.Good, TileKind.Rate, Thresholds);

        Assert.Equal(TileStatus.InsufficientData, result.Status);
        Assert.Equal(ReasonCode.InsufficientHistory, result.Reason);
        Assert.NotEqual(ReasonCode.DenominatorBelowMin, result.Reason);
    }

    [Fact]
    public void Row10_ViewedWeek4Of7Days_CountTile_IsPartialWeek()
    {
        var baseline = Baseline(10m, 6m, 14m);

        var result = new StatusEvaluator().Evaluate(
            9m, null, 4, 7, baseline, OutcomePolarity.Good, TileKind.Count, Thresholds);

        Assert.Equal(TileStatus.PartialWeek, result.Status);
        Assert.Equal(ReasonCode.ViewedWeekPartial, result.Reason);
        Assert.Null(result.DeltaPct);
    }

    [Fact]
    public void Row11_ViewedWeek4Of7Days_RateTile_IsEvaluatedNormally()
    {
        // Rate tiles are unaffected by an incomplete viewed week — numerator and denominator lose
        // the same days, so the ratio survives (Requirements §"Week completeness").
        var baseline = Baseline(20m, 12m, 28m);

        var result = new StatusEvaluator().Evaluate(
            20m, 50, 4, 7, baseline, OutcomePolarity.Good, TileKind.Rate, Thresholds);

        Assert.NotEqual(TileStatus.PartialWeek, result.Status);
        Assert.Equal(TileStatus.Normal, result.Status);
        Assert.Equal(0m, result.DeltaPct);
    }

    [Fact]
    public void Row12_RateDenominator19_MinRateDenominator20_IsInsufficientData_NeverBaselineZero()
    {
        // Coincidence: the baseline mean is also 0 (which would independently trigger
        // BaselineZero). The thin viewed-week denominator is the more direct, more actionable
        // explanation and must win.
        var baseline = Baseline(0m, 0m, 0m);

        var result = new StatusEvaluator().Evaluate(
            15m, 19, 7, 7, baseline, OutcomePolarity.Good, TileKind.Rate, Thresholds);

        Assert.Equal(TileStatus.InsufficientData, result.Status);
        Assert.Equal(ReasonCode.DenominatorBelowMin, result.Reason);
        Assert.NotEqual(ReasonCode.BaselineZero, result.Reason);
    }
}
