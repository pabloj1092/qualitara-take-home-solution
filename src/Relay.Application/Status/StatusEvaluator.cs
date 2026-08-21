using Relay.Application.Baseline;
using Relay.Domain;

namespace Relay.Application.Status;

/// <summary>
/// The five-rung status ladder (Requirements §"Status ladder"), evaluated in order, first match
/// wins. Pure: value objects in, value objects out — no I/O, no database. This is deliberate:
/// the rule most likely to put a wrong colour on a customer's screen gets the fastest and most
/// exhaustive tests in the whole solution (Requirements §1, `StatusEvaluatorTests`).
/// </summary>
public sealed class StatusEvaluator
{
    public StatusResult Evaluate(
        decimal? viewedValue,
        int? viewedDenominator,
        int viewedDaysIncluded,
        int viewedExpectedDays,
        BaselineResult baseline,
        OutcomePolarity polarity,
        TileKind kind,
        ThresholdSet thresholds)
    {
        // Rung 1 · InsufficientData — never a colour, never red, outranks every other rule.
        if (baseline.Mean is null || baseline.Mean < thresholds.MinBaselineEvents)
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.BaselineBelowMinEvents, null, null);
        }

        if (baseline.Mean == 0m)
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.BaselineZero, null, null);
        }

        if (baseline.WeeksContributing < thresholds.MinHistoryWeeks)
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.InsufficientHistory, null, null);
        }

        if (kind == TileKind.Rate
            && (viewedDenominator is null || viewedDenominator < thresholds.MinRateDenominator))
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.DenominatorBelowMin, null, null);
        }

        // Rung 2 · PartialWeek — count tiles only; rate tiles survive an incomplete week because
        // numerator and denominator lose the same days.
        if (kind == TileKind.Count && viewedDaysIncluded < viewedExpectedDays)
        {
            return new StatusResult(TileStatus.PartialWeek, ReasonCode.ViewedWeekPartial, null, null);
        }

        // Past both gates: the baseline is trustworthy and the viewed week is complete enough to
        // compare. viewedValue is guaranteed non-null here — a count is never null, and a rate
        // tile with a null value implies a zero denominator, already caught above.
        var deltaPct = (viewedValue!.Value - baseline.Mean.Value) / baseline.Mean.Value * 100m;
        decimal? deltaPp = kind == TileKind.Rate ? viewedValue.Value - baseline.Mean.Value : null;

        // Rung 5 (early exit) · neutral outcomes have no bad side and can only ever land here.
        if (polarity == OutcomePolarity.Neutral)
        {
            return new StatusResult(TileStatus.Normal, ReasonCode.NeutralPolarity, deltaPct, deltaPp);
        }

        var onBadSide = polarity == OutcomePolarity.Good ? deltaPct < 0m : deltaPct > 0m;
        if (!onBadSide)
        {
            return new StatusResult(TileStatus.Normal, ReasonCode.GoodDirection, deltaPct, deltaPp);
        }

        var absDeviation = Math.Abs(deltaPct);

        // Rung 3 · Breach — boundary inclusive.
        if (absDeviation >= thresholds.TolerancePct)
        {
            return new StatusResult(TileStatus.Breach, ReasonCode.OutsideTolerance, deltaPct, deltaPp);
        }

        // Rung 4 · Warning — boundary inclusive.
        if (absDeviation >= thresholds.AmberFraction * thresholds.TolerancePct)
        {
            return new StatusResult(TileStatus.Warning, ReasonCode.NearTolerance, deltaPct, deltaPp);
        }

        // Rung 5 · Normal — on the bad side, but comfortably inside tolerance.
        return new StatusResult(TileStatus.Normal, ReasonCode.WithinTolerance, deltaPct, deltaPp);
    }
}
