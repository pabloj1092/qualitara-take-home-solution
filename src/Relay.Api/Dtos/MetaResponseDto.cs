namespace Relay.Api.Dtos;

public sealed record MetaLocationDto(int Id, string Name, DateOnly? OpenedOn, DateOnly? ClosedOn);

public sealed record MetaDefaultsDto(
    string? Week,
    int Window,
    decimal TolerancePct,
    int MinBaselineEvents,
    int MinRateDenominator,
    int MinHistoryWeeks,
    decimal MinWeekCompleteness,
    decimal AmberFraction);

public sealed record MetaResponseDto(
    IReadOnlyList<MetaLocationDto> Locations,
    string? FirstWeek,
    string? LatestWeekWithData,
    string? LatestCompleteWeek,
    int MaxWindowForWeek,
    MetaDefaultsDto Defaults);
