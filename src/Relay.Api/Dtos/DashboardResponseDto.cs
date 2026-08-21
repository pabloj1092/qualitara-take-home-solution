using Relay.Application.Baseline;
using Relay.Domain;

namespace Relay.Api.Dtos;

public sealed record WeekDto(
    string IsoWeek, DateOnly Start, DateOnly End, string Label, bool HasPrevious, bool HasNext);

public sealed record WindowDto(int Requested, int Effective);

public sealed record LocationDto(int Id, string Name, bool Selected);

public sealed record SeriesPointDto(
    DateOnly WeekStart,
    decimal? Value,
    int? Denominator,
    int DaysIncluded,
    int ExpectedDays,
    bool IncludedInBaseline,
    SeriesExclusionReason? ExclusionReason,
    bool IsViewedWeek,
    bool OverlapsExclusion);

public sealed record TileDto(
    string Key,
    string Label,
    TileKind Kind,
    decimal? Value,
    decimal? DeltaPct,
    decimal? DeltaPp,
    decimal? BaselineMean,
    decimal? BandLow,
    decimal? BandHigh,
    int? Denominator,
    TileStatus Status,
    ReasonCode ReasonCode,
    int BaselineWeeksUsed,
    IReadOnlyList<SeriesPointDto> Series);

public sealed record SectionDto(
    string EventType, string DisplayName, int SortOrder, TileDto CountTile, IReadOnlyList<TileDto> RateTiles);

public sealed record ExclusionDto(DateOnly FromDate, DateOnly ToDate, string Reason, IReadOnlyList<DateOnly> WeeksAffected);

public sealed record DisclosuresDto(int NullOutcomeCount, IReadOnlyList<ExclusionDto> Exclusions);

public sealed record DashboardResponseDto(
    int AccountId,
    string AccountName,
    string Timezone,
    string TimezoneNote,
    WeekDto Week,
    WindowDto Window,
    decimal TolerancePct,
    IReadOnlyList<LocationDto> Locations,
    IReadOnlyList<SectionDto> Sections,
    DisclosuresDto Disclosures);
