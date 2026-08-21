using Relay.Application.Abstractions;
using Relay.Application.Baseline;
using Relay.Application.Status;
using Relay.Domain;

namespace Relay.Application.Dashboard;

public sealed record TileResult(
    TileKey Key,
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
    IReadOnlyList<SeriesPoint> Series);

public sealed record SectionResult(
    string EventType, string DisplayName, int SortOrder, TileResult CountTile, IReadOnlyList<TileResult> RateTiles);

public sealed record LocationSelection(int Id, string Name, bool Selected);

public sealed record WindowInfo(int Requested, int Effective);

public sealed record DashboardResult(
    AccountInfo Account,
    WeekRange ViewedWeek,
    WeekRange? FirstWeek,
    WeekRange? LatestWeekWithData,
    WindowInfo Window,
    decimal TolerancePct,
    IReadOnlyList<LocationSelection> Locations,
    IReadOnlyList<SectionResult> Sections,
    DisclosureData Disclosures);

/// <summary>
/// The seam between the database and the pure core: resolves thresholds (settings row ← query
/// overrides), asks <see cref="IDashboardReader"/> for the dense observations, calls
/// <see cref="BaselineService"/> and <see cref="StatusEvaluator"/> per tile, and assembles the
/// result. No <c>DbContext</c>, no EF Core, no way to acquire one.
/// </summary>
public sealed class DashboardQueryService(
    IDashboardReader dashboardReader,
    IAccountMetadataReader metadataReader,
    TimeProvider timeProvider)
{
    private readonly BaselineService _baselineService = new();
    private readonly StatusEvaluator _statusEvaluator = new();

    public async Task<DashboardResult?> GetAsync(
        int accountId,
        string? locationsParam,
        string? weekParam,
        int? windowParam,
        decimal? toleranceParam,
        CancellationToken ct)
    {
        var requestedWeek = ParseWeek(weekParam);

        var meta = await metadataReader.ReadAsync(accountId, requestedWeek, ct);
        if (meta is null)
        {
            return null;
        }

        // No data at all for this account: an honest empty dashboard, never a 404 or a 500 over
        // window/week bounds that a data-less account cannot satisfy (Requirements §5).
        if (meta.FirstWeek is null)
        {
            var placeholderWeek = requestedWeek
                ?? WeekRange.Containing(DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));
            return new DashboardResult(
                new AccountInfo(meta.AccountId, meta.Name, meta.Timezone),
                placeholderWeek,
                null,
                null,
                new WindowInfo(windowParam ?? meta.DefaultWindow, 0),
                toleranceParam ?? meta.Thresholds.TolerancePct,
                [],
                [],
                new DisclosureData(0, []));
        }

        var firstWeek = meta.FirstWeek.Value;
        var latestWeekWithData = meta.LatestWeekWithData!.Value;
        // LatestCompleteWeek is genuinely nullable (an account whose locations never all report a
        // full week at once has none) — fall back one step further rather than dereferencing it.
        var viewedWeek = requestedWeek ?? meta.LatestCompleteWeek ?? latestWeekWithData;
        if (viewedWeek.Start < firstWeek.Start || viewedWeek.Start > latestWeekWithData.Start)
        {
            throw new RequestValidationException(
                "week",
                $"'{viewedWeek.ToIsoWeek()}' is outside the available range " +
                $"[{firstWeek.ToIsoWeek()}, {latestWeekWithData.ToIsoWeek()}].");
        }

        // maxWindowForWeek shrinks to 0 at the very first week — that week is still legitimately
        // viewable (Requirements §5 only says the max "shrinks as week moves backwards"), so a
        // too-large window clamps and WindowInfo.Effective says so, rather than 400ing against a
        // range no value (not even the default) could ever satisfy. Only a malformed window
        // (< 1) is rejected — DashboardRequestValidator already catches this earlier, this is a
        // second, defensive check.
        var requestedWindow = windowParam ?? meta.DefaultWindow;
        if (requestedWindow < 1)
        {
            throw new RequestValidationException("window", $"'{requestedWindow}' must be at least 1.");
        }

        var window = Math.Min(requestedWindow, Math.Max(1, meta.MaxWindowForWeek));

        var tolerancePct = toleranceParam ?? meta.Thresholds.TolerancePct;
        if (tolerancePct is < 1 or > 100)
        {
            throw new RequestValidationException("tolerance", $"'{tolerancePct}' must be between 1 and 100.");
        }

        var locationIds = ResolveLocations(locationsParam, meta.Locations);
        var thresholds = meta.Thresholds with { TolerancePct = tolerancePct };

        var readModel = await dashboardReader.ReadAsync(
            new DashboardQuery(accountId, locationIds, viewedWeek, window), ct);
        if (readModel is null)
        {
            return null;
        }

        var sections = BuildSections(readModel.Tiles, viewedWeek, window, thresholds, readModel.Disclosures);

        var firstTile = readModel.Tiles.FirstOrDefault();
        var weeksEffective = firstTile is null ? 0 : Math.Min(window, firstTile.Observations.Count - 1);

        var selectedIds = locationIds.Count == 0 ? null : new HashSet<int>(locationIds);
        var locationSelections = readModel.Locations
            .Select(l => new LocationSelection(l.Id, l.Name, selectedIds is null || selectedIds.Contains(l.Id)))
            .ToList();

        return new DashboardResult(
            readModel.Account,
            viewedWeek,
            firstWeek,
            latestWeekWithData,
            new WindowInfo(requestedWindow, weeksEffective),
            tolerancePct,
            locationSelections,
            sections,
            readModel.Disclosures);
    }

    private static WeekRange? ParseWeek(string? weekParam)
    {
        if (string.IsNullOrWhiteSpace(weekParam))
        {
            return null;
        }

        try
        {
            return WeekRange.FromIsoWeek(weekParam);
        }
        catch (FormatException)
        {
            throw new RequestValidationException(
                "week", $"'{weekParam}' is not a valid ISO week (expected yyyy-Www).");
        }
    }

    private static IReadOnlyList<int> ResolveLocations(
        string? locationsParam, IReadOnlyList<LocationInfo> accountLocations)
    {
        if (string.IsNullOrWhiteSpace(locationsParam))
        {
            return [];
        }

        var names = locationsParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var byName = accountLocations.ToDictionary(l => l.Name, StringComparer.Ordinal);
        var ids = new List<int>(names.Length);
        foreach (var name in names)
        {
            if (!byName.TryGetValue(name, out var location))
            {
                throw new RequestValidationException("locations", $"'{name}' is not a location on this account.");
            }

            ids.Add(location.Id);
        }

        return ids;
    }

    private List<SectionResult> BuildSections(
        IReadOnlyList<TileSeries> tiles,
        WeekRange viewedWeek,
        int window,
        ThresholdSet thresholds,
        DisclosureData disclosures)
    {
        var sections = new List<SectionResult>();
        foreach (var eventGroup in tiles.GroupBy(t => t.Key.EventType).OrderBy(g => g.First().EventTypeSortOrder))
        {
            var countSeries = eventGroup.Single(t => t.Key.Kind == TileKind.Count);
            var countTile = BuildTile(countSeries, viewedWeek, window, thresholds, disclosures);

            var rateTiles = eventGroup
                .Where(t => t.Key.Kind == TileKind.Rate)
                .OrderBy(t => t.OutcomeSortOrder)
                .Select(t => BuildTile(t, viewedWeek, window, thresholds, disclosures))
                .ToList();

            sections.Add(new SectionResult(
                eventGroup.Key, countSeries.EventTypeDisplayName, countSeries.EventTypeSortOrder,
                countTile, rateTiles));
        }

        return sections;
    }

    private TileResult BuildTile(
        TileSeries series, WeekRange viewedWeek, int window, ThresholdSet thresholds, DisclosureData disclosures)
    {
        var baseline = _baselineService.Build(series.Observations, viewedWeek, window, series.Key.Kind, thresholds);
        var viewed = series.Observations[^1];

        var status = _statusEvaluator.Evaluate(
            viewed.Value, viewed.Denominator, viewed.DaysIncluded, viewed.ExpectedDays,
            baseline, series.Polarity, series.Key.Kind, thresholds);

        var disclosedSeries = ApplyDisclosures(baseline.Series, disclosures);
        var label = series.Key.Kind == TileKind.Count ? series.EventTypeDisplayName : series.OutcomeDisplayName!;

        return new TileResult(
            series.Key, label, series.Key.Kind,
            viewed.Value, status.DeltaPct, status.DeltaPp,
            baseline.Mean, baseline.BandLow, baseline.BandHigh,
            series.Key.Kind == TileKind.Rate ? viewed.Denominator : null,
            status.Status, status.Reason, baseline.WeeksContributing,
            disclosedSeries);
    }

    /// <summary>Upgrades a generically "PartialWeek" point to the more specific
    /// "DataQualityExclusion" when it overlaps a disclosed exclusion window — same underlying
    /// incompleteness, a more honest label.</summary>
    private static IReadOnlyList<SeriesPoint> ApplyDisclosures(
        IReadOnlyList<SeriesPoint> series, DisclosureData disclosures)
    {
        if (disclosures.Exclusions.Count == 0)
        {
            return series;
        }

        return series
            .Select(point =>
            {
                if (point.ExclusionReason != SeriesExclusionReason.PartialWeek)
                {
                    return point;
                }

                var weekEnd = point.WeekStart.AddDays(6);
                var overlaps = disclosures.Exclusions.Any(x => x.FromDate <= weekEnd && x.ToDate >= point.WeekStart);
                return overlaps ? point with { ExclusionReason = SeriesExclusionReason.DataQualityExclusion } : point;
            })
            .ToList();
    }
}
