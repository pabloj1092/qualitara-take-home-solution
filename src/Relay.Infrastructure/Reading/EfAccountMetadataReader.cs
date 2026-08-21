using Microsoft.EntityFrameworkCore;
using Relay.Application.Abstractions;
using Relay.Domain;

namespace Relay.Infrastructure.Reading;

public sealed class EfAccountMetadataReader(RelayDbContext db) : IAccountMetadataReader
{
    public async Task<AccountMeta?> ReadAsync(int accountId, WeekRange? week, CancellationToken ct)
    {
        var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return null;
        }

        var locations = await db.Locations.AsNoTracking()
            .Where(l => l.AccountId == accountId)
            .OrderBy(l => l.Name)
            .Select(l => new LocationInfo(l.Id, l.Name, l.OpenedOn, l.ClosedOn))
            .ToListAsync(ct);

        var settings = await db.AccountDashboardSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AccountId == accountId, ct);

        var thresholds = settings is null
            ? ThresholdSet.Defaults
            : new ThresholdSet(
                settings.MinBaselineEvents, settings.MinRateDenominator, settings.MinHistoryWeeks,
                settings.MinWeekCompleteness, settings.AmberFraction, settings.TolerancePct);
        var defaultWindow = settings?.DefaultComparisonWeeks ?? 8;

        // No locations at all (account 20): the dense fact view has literally zero rows for this
        // account (density is locations × iso_weeks × outcome slots — zero locations, zero rows).
        // Empty, not an error.
        if (locations.Count == 0)
        {
            return new AccountMeta(
                accountId, account.Name, account.Timezone, locations,
                FirstWeek: null, LatestWeekWithData: null, LatestCompleteWeek: null,
                MaxWindowForWeek: 0, defaultWindow, thresholds);
        }

        // iso_weeks is one global spine (built from the min/max local_date across *all*
        // activity_events_local, not per account). Any account with ≥1 location has a dense row
        // for every global week, so first/last-with-data are the spine's own boundaries.
        var globalFirst = await db.IsoWeeks.AsNoTracking().MinAsync(w => (DateOnly?)w.WeekStart, ct);
        var globalLast = await db.IsoWeeks.AsNoTracking().MaxAsync(w => (DateOnly?)w.WeekStart, ct);

        WeekRange? firstWeek = globalFirst is { } first ? new WeekRange(first, first.AddDays(6)) : null;
        WeekRange? latestWeekWithData = globalLast is { } last ? new WeekRange(last, last.AddDays(6)) : null;

        var latestCompleteWeek = await ComputeLatestCompleteWeekAsync(accountId, ct);

        var targetWeek = week ?? latestCompleteWeek ?? latestWeekWithData;
        var maxWindow = 0;
        if (targetWeek is not null && firstWeek is not null)
        {
            maxWindow = Math.Max(0, (targetWeek.Value.Start.DayNumber - firstWeek.Value.Start.DayNumber) / 7);
        }

        return new AccountMeta(
            accountId, account.Name, account.Timezone, locations,
            firstWeek, latestWeekWithData, latestCompleteWeek,
            maxWindow, defaultWindow, thresholds);
    }

    /// <summary>
    /// The latest week where every one of the account's locations is fully complete
    /// (Requirements §"Week completeness": pool as SUM(days_included) / SUM(expected_days) —
    /// never sum the day columns across an individual location's own event-type/outcome rows,
    /// since they repeat identically there). Raw SQL rather than two chained LINQ GroupBys: EF
    /// cannot translate "group, take one per group, then group and sum again" as a single pushed-
    /// down query, and materializing every (week, location) pair client-side to reduce it in C#
    /// is the one query in this reader that doesn't push down — see PLAN.md's Pushdown check.
    /// The account_id predicate on the innermost subquery reaches weekly_activity_facts directly,
    /// so it inlines into the view the same way the rest of this reader's queries do.
    /// </summary>
    private async Task<WeekRange?> ComputeLatestCompleteWeekAsync(int accountId, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<DateOnly>($"""
            SELECT week_start_local
            FROM (
                SELECT week_start_local, SUM(days_included) AS included, SUM(expected_days) AS expected
                FROM (
                    SELECT DISTINCT ON (week_start_local, location_id)
                           week_start_local, location_id, days_included, expected_days
                    FROM weekly_activity_facts
                    WHERE account_id = {accountId}
                    ORDER BY week_start_local, location_id
                ) AS per_location_week
                GROUP BY week_start_local
            ) AS per_week
            WHERE expected > 0 AND included = expected
            ORDER BY week_start_local DESC
            LIMIT 1
            """)
            .ToListAsync(ct);

        return rows.Count > 0 ? new WeekRange(rows[0], rows[0].AddDays(6)) : null;
    }
}
