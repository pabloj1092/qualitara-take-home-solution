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
        //
        // Order matters, and it is checked in decreasing specificity. A definitively-too-new
        // account/location — fewer than MinHistoryWeeks calendar weeks even exist in the spine yet
        // — is checked first and unconditionally: no amount of volume in the viewed week can
        // satisfy a calendar-time requirement, so this must win over the denominator check below
        // even when the viewed week also happens to be thin. WeeksEffective (not WeeksContributing)
        // is the right signal here — it counts weeks that exist at all, before any are dropped for
        // being too thin or incomplete, so it is unaffected by *why* older weeks failed to
        // contribute.
        //
        // Past that gate, a thin rate denominator on the *viewed* week is checked next: for an
        // account with enough calendar history, it is the more direct, more actionable explanation,
        // and low volume tends to drag down the baseline candidate weeks right alongside it — so
        // whichever check runs first is the reason the user actually sees whenever both are true
        // at once, which is the common case for a quiet outcome, not the rare one. Reporting
        // "not enough history" for an account with six months of data because this week's n=11 is
        // the wrong story. A null mean means zero contributing weeks — a history problem, not a
        // "too small" problem, so it is folded into the history check rather than reported as
        // BaselineBelowMinEvents. Mean == 0 is checked before the minimum-events comparison so it
        // is reachable at all (0 < 5 would otherwise always win first) and it guards the division
        // below regardless of tile kind. MinBaselineEvents itself is an event-count threshold and
        // only means something for count tiles — rate tiles gate on the denominator, not the mean
        // (Requirements §"Defining min_baseline_events").
        if (baseline.WeeksEffective < thresholds.MinHistoryWeeks)
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.InsufficientHistory, null, null);
        }

        if (kind == TileKind.Rate
            && (viewedDenominator is null || viewedDenominator < thresholds.MinRateDenominator))
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.DenominatorBelowMin, null, null);
        }

        if (baseline.Mean is null || baseline.WeeksContributing < thresholds.MinHistoryWeeks)
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.InsufficientHistory, null, null);
        }

        if (baseline.Mean == 0m)
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.BaselineZero, null, null);
        }

        if (kind == TileKind.Count && baseline.Mean < thresholds.MinBaselineEvents)
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.BaselineBelowMinEvents, null, null);
        }

        // Rung 2 · PartialWeek — count tiles only; rate tiles survive an incomplete week because
        // numerator and denominator lose the same days. ExpectedDays == 0 (no rows at all for the
        // viewed week) counts as partial too — 0 < 0 would otherwise read as a complete week.
        if (kind == TileKind.Count
            && (viewedValue is null || viewedExpectedDays == 0 || viewedDaysIncluded < viewedExpectedDays))
        {
            return new StatusResult(TileStatus.PartialWeek, ReasonCode.ViewedWeekPartial, null, null);
        }

        // Defensive: past both gates, viewedValue should always be non-null — a count is never
        // null (just excluded above if it somehow were), and a null rate value implies a zero
        // denominator, already caught by the DenominatorBelowMin check above. Guard rather than
        // let a future data-layer change surface as a bare NRE.
        if (viewedValue is not { } value)
        {
            return new StatusResult(TileStatus.InsufficientData, ReasonCode.DenominatorBelowMin, null, null);
        }

        var deltaPct = (value - baseline.Mean.Value) / baseline.Mean.Value * 100m;
        decimal? deltaPp = kind == TileKind.Rate ? value - baseline.Mean.Value : null;

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
