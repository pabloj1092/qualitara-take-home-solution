using Relay.Domain;

namespace Relay.Application.Abstractions;

public sealed record AccountInfo(int Id, string Name, string Timezone);

public sealed record LocationInfo(int Id, string Name, DateOnly? OpenedOn, DateOnly? ClosedOn);

public sealed record ExclusionInfo(
    DateOnly FromDate, DateOnly ToDate, string Reason, IReadOnlyList<DateOnly> WeeksAffected);

public sealed record DisclosureData(int NullOutcomeCount, IReadOnlyList<ExclusionInfo> Exclusions);

/// <summary>
/// One tile's dense, spine-aligned observations, carrying the catalog metadata
/// (<c>event_type_catalog</c> / <c>outcome_catalog</c>) needed to label and order it — the read
/// port's job ends at handing over weekly observations, but section/tile labels and ordering are
/// reference data, not business logic, so they travel alongside rather than requiring a second
/// round trip through the metadata port.
/// </summary>
public sealed record TileSeries(
    TileKey Key,
    string EventTypeDisplayName,
    int EventTypeSortOrder,
    string? OutcomeDisplayName,
    int OutcomeSortOrder,
    OutcomePolarity Polarity,
    IReadOnlyList<WeekObservation> Observations);

/// <summary>
/// <paramref name="LocationIds"/> empty means "all locations for the account" — the same
/// convention the API uses when the <c>locations</c> query parameter is omitted.
/// </summary>
public sealed record DashboardQuery(
    int AccountId, IReadOnlyList<int> LocationIds, WeekRange ViewedWeek, int Window);

public sealed record DashboardReadModel(
    AccountInfo Account,
    IReadOnlyList<LocationInfo> Locations,
    IReadOnlyList<TileSeries> Tiles,
    DisclosureData Disclosures);

public sealed record AccountMeta(
    int AccountId,
    string Name,
    string Timezone,
    IReadOnlyList<LocationInfo> Locations,
    WeekRange? FirstWeek,
    WeekRange? LatestWeekWithData,
    WeekRange? LatestCompleteWeek,
    int MaxWindowForWeek,
    int DefaultWindow,
    ThresholdSet Thresholds);
