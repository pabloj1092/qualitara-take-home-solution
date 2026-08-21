namespace Relay.Application.Baseline;

public sealed record BaselineResult(
    decimal? Mean,
    decimal? BandLow,
    decimal? BandHigh,
    int WeeksRequested,
    int WeeksEffective,
    int WeeksContributing,
    IReadOnlyList<SeriesPoint> Series);
