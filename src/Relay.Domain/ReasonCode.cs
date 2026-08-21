namespace Relay.Domain;

public enum ReasonCode
{
    BaselineBelowMinEvents,
    BaselineZero,
    InsufficientHistory,
    DenominatorBelowMin,
    ViewedWeekPartial,
    OutsideTolerance,
    NearTolerance,
    WithinTolerance,
    GoodDirection,
    NeutralPolarity,
}
