using Microsoft.EntityFrameworkCore;
using Relay.Application.Abstractions;
using Relay.Domain;

namespace Relay.Infrastructure.Reading;

/// <summary>
/// Implements <see cref="IDashboardReader"/>: one GroupBy→Sum against <c>weekly_activity_facts</c>,
/// pivoted into dense <see cref="WeekObservation"/> spines. Never returns an entity or an
/// <see cref="IQueryable{T}"/> — everything EF knows about dies at this boundary.
/// </summary>
public sealed class EfDashboardReader(RelayDbContext db) : IDashboardReader
{
    public async Task<DashboardReadModel?> ReadAsync(DashboardQuery query, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == query.AccountId, ct);
        if (account is null)
        {
            return null;
        }

        var allLocations = await db.Locations.AsNoTracking()
            .Where(l => l.AccountId == query.AccountId)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);

        var locationIds = (query.LocationIds.Count > 0
            ? query.LocationIds
            : allLocations.Select(l => l.Id)).ToList();

        if (locationIds.Count == 0)
        {
            return new DashboardReadModel(
                new AccountInfo(account.Id, account.Name, account.Timezone),
                allLocations.Select(l => new LocationInfo(l.Id, l.Name, l.OpenedOn, l.ClosedOn)).ToList(),
                [],
                new DisclosureData(0, []));
        }

        // Density is owned by SQL, not recomputed here: iso_weeks is the actual spine, so the
        // week list is read from it (ordered, capped at window+1) rather than reconstructed by
        // calendar arithmetic in C#. If fewer than window+1 rows exist before the viewed week,
        // this naturally returns fewer weeks — the clamping BaselineService reports as
        // WeeksEffective.
        var weekStarts = await db.IsoWeeks.AsNoTracking()
            .Where(w => w.WeekStart <= query.ViewedWeek.Start)
            .OrderByDescending(w => w.WeekStart)
            .Take(query.Window + 1)
            .Select(w => w.WeekStart)
            .ToListAsync(ct);
        weekStarts.Reverse();

        // DashboardQueryService already validated the viewed week against
        // [firstWeek, latestWeekWithData] — both themselves read from iso_weeks — so this holds
        // as long as the spine is gapless across that whole range. If it ever isn't, fail with a
        // diagnosis naming the account and week rather than letting BaselineService's generic
        // "spine must end at the viewed week" surface as an unexplained 500.
        if (weekStarts.Count == 0 || weekStarts[^1] != query.ViewedWeek.Start)
        {
            throw new InvalidOperationException(
                $"iso_weeks has no row for {query.ViewedWeek.Start:yyyy-MM-dd} " +
                $"(account {query.AccountId}) even though it fell within the account's validated " +
                "data range — the spine has a gap.");
        }

        // Day-completeness is a (location, week) fact, repeated redundantly across every
        // event_type/outcome row it touches — dedupe per location first (Max is a "pick one",
        // every row for a location-week carries the same value), then pool across locations by
        // summing. The result applies uniformly to every tile for that week.
        var perLocationWeek = await db.WeeklyActivityFacts.AsNoTracking()
            .Where(f => f.AccountId == query.AccountId
                     && locationIds.Contains(f.LocationId)
                     && weekStarts.Contains(f.WeekStartLocal))
            .GroupBy(f => new { f.WeekStartLocal, f.LocationId })
            .Select(g => new
            {
                g.Key.WeekStartLocal,
                Included = g.Max(x => x.DaysIncluded),
                Expected = g.Max(x => x.ExpectedDays),
            })
            .ToListAsync(ct);

        var daysByWeek = perLocationWeek
            .GroupBy(x => x.WeekStartLocal)
            .ToDictionary(g => g.Key, g => (Included: g.Sum(x => x.Included), Expected: g.Sum(x => x.Expected)));

        // Event counts pooled across the selected locations, per (week, event_type, outcome) —
        // the numerator/denominator source for every count and rate tile.
        var counts = await db.WeeklyActivityFacts.AsNoTracking()
            .Where(f => f.AccountId == query.AccountId
                     && locationIds.Contains(f.LocationId)
                     && weekStarts.Contains(f.WeekStartLocal))
            .GroupBy(f => new { f.WeekStartLocal, f.EventType, f.Outcome })
            .Select(g => new
            {
                g.Key.WeekStartLocal,
                g.Key.EventType,
                g.Key.Outcome,
                EventCount = g.Sum(x => x.EventCount),
            })
            .ToListAsync(ct);

        var eventTypes = await db.EventTypeCatalogs.AsNoTracking().OrderBy(e => e.SortOrder).ToListAsync(ct);
        var outcomes = await db.OutcomeCatalogs.AsNoTracking()
            .OrderBy(o => o.EventType).ThenBy(o => o.SortOrder).ToListAsync(ct);

        var tiles = new List<TileSeries>();
        foreach (var eventType in eventTypes)
        {
            var forType = counts.Where(c => c.EventType == eventType.Code).ToList();

            // Count tile: every outcome slot, including the null-outcome one — "counted in the
            // event-type total" even though it is excluded from every rate denominator below.
            var countObservations = weekStarts
                .Select(week =>
                {
                    var (included, expected) = daysByWeek.GetValueOrDefault(week, (0, 0));
                    var value = forType.Where(c => c.WeekStartLocal == week).Sum(c => (decimal?)c.EventCount) ?? 0m;
                    return new WeekObservation(week, value, null, included, expected);
                })
                .ToList();

            // Count tiles have no outcome and therefore no natural polarity. Treated as "Good" —
            // a volume drop (calls stopped coming in) is the actionable direction to flag, a
            // spike is not — consistent with tolerance_pct governing count and rate tiles alike.
            tiles.Add(new TileSeries(
                new TileKey(eventType.Code, null, TileKind.Count),
                eventType.DisplayName, eventType.SortOrder, null, 0, OutcomePolarity.Good,
                countObservations));

            foreach (var outcome in outcomes.Where(o => o.EventType == eventType.Code))
            {
                var rateObservations = weekStarts
                    .Select(week =>
                    {
                        var (included, expected) = daysByWeek.GetValueOrDefault(week, (0, 0));
                        var weekRows = forType.Where(c => c.WeekStartLocal == week).ToList();
                        var denominator = weekRows.Where(c => c.Outcome != null).Sum(c => c.EventCount);
                        var numerator = weekRows.FirstOrDefault(c => c.Outcome == outcome.Code)?.EventCount ?? 0;
                        decimal? value = denominator > 0 ? Math.Round(numerator * 100m / denominator, 2) : null;
                        return new WeekObservation(week, value, denominator, included, expected);
                    })
                    .ToList();

                tiles.Add(new TileSeries(
                    new TileKey(eventType.Code, outcome.Code, TileKind.Rate),
                    eventType.DisplayName, eventType.SortOrder, outcome.DisplayName, outcome.SortOrder,
                    outcome.Polarity, rateObservations));
            }
        }

        var nullOutcomeCount = counts.Where(c => c.Outcome is null).Sum(c => c.EventCount);

        var minWeek = weekStarts.Min();
        var exclusions = await db.DataQualityExclusions.AsNoTracking()
            .Where(x => x.AccountId == null || x.AccountId == query.AccountId)
            .Where(x => x.FromDate <= query.ViewedWeek.End && x.ToDate >= minWeek)
            .ToListAsync(ct);

        var disclosureExclusions = exclusions
            .Select(x => new ExclusionInfo(
                x.FromDate, x.ToDate, x.Reason,
                weekStarts.Where(w => x.FromDate <= w.AddDays(6) && x.ToDate >= w).ToList()))
            .ToList();

        return new DashboardReadModel(
            new AccountInfo(account.Id, account.Name, account.Timezone),
            allLocations.Select(l => new LocationInfo(l.Id, l.Name, l.OpenedOn, l.ClosedOn)).ToList(),
            tiles,
            new DisclosureData(nullOutcomeCount, disclosureExclusions));
    }
}
