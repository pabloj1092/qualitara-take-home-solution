using Npgsql;
using Relay.Application.Dashboard;
using Relay.Infrastructure.Reading;

namespace Relay.Tests.Integration;

/// <summary>
/// Requirements §3 "Data quality" — integration, against a throwaway seeded database. Every
/// number below was verified directly against the pristine seed before being hardcoded here
/// (schema.sql + seed.sql are fixed, read-only content — CLAUDE.md — so these are deterministic,
/// not incidental).
/// </summary>
[Collection(SeededDatabaseCollection.Name)]
public class DataQualityTests(SeededDatabaseFixture fixture)
{
    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var result = await command.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    [Fact]
    public async Task D1Exclusion_Removes805RawRowsForAccount6On20260603()
    {
        var raw = await ScalarAsync<long>(
            "SELECT count(*) FROM activity_events WHERE account_id = 6 AND occurred_at::date = DATE '2026-06-03'");
        var clean = await ScalarAsync<long>(
            "SELECT count(*) FROM activity_events_clean WHERE account_id = 6 AND local_date = DATE '2026-06-03'");

        Assert.Equal(805, raw);
        Assert.Equal(0, clean);
    }

    [Fact]
    public async Task D1Exclusion_MeasurablyLowersTheWeeksTile_NotJustTheRawRowCount()
    {
        // The row-count assertion above proves the exclusion filter fires; this proves the effect
        // actually reaches the fact view a tile reads from. "Dedupe-only" replicates
        // activity_events_clean's DISTINCT ON logic but skips the data_quality_exclusions filter,
        // isolating D1's effect on this one week from D4's (dedupe) effect on the same week.
        const string dedupeOnlySql = """
            WITH deduped AS (
                SELECT DISTINCT ON (account_id, location, event_type, occurred_at) id
                FROM activity_events
                WHERE account_id = 6
                  AND occurred_at >= DATE '2026-06-01' AND occurred_at < DATE '2026-06-08'
                ORDER BY account_id, location, event_type, occurred_at, id
            )
            SELECT count(*) FROM deduped
            """;
        var dedupeOnly = await ScalarAsync<long>(dedupeOnlySql);

        var actual = await ScalarAsync<long>(
            "SELECT coalesce(sum(event_count), 0) FROM weekly_activity_facts " +
            "WHERE account_id = 6 AND week_start_local = DATE '2026-06-01'");

        Assert.Equal(880, dedupeOnly);
        Assert.Equal(76, actual);
        // 804, not the full 805: one of the seed's 12 global duplicate pairs also falls inside
        // this same week on a day other than 2026-06-03, so dedupe (D4) removes one more row from
        // "dedupeOnly" than the exclusion alone would — the same D1/D4 interaction
        // sql/verify_migration.sql documents at the whole-dataset level.
        Assert.Equal(804, dedupeOnly - actual);
    }

    [Fact]
    public async Task Dedupe_CollapsesExactlyTwelveDuplicatePairs_AndNoneSurviveInClean()
    {
        const string duplicatePairsSql = """
            SELECT count(*) FROM (
                SELECT account_id, location, event_type, occurred_at
                FROM activity_events
                GROUP BY account_id, location, event_type, occurred_at
                HAVING count(*) > 1
            ) AS dups
            """;
        const string survivingDuplicatesSql = """
            SELECT count(*) FROM (
                SELECT account_id, location, event_type, occurred_at_utc
                FROM activity_events_clean
                GROUP BY account_id, location, event_type, occurred_at_utc
                HAVING count(*) > 1
            ) AS surviving
            """;

        Assert.Equal(12, await ScalarAsync<long>(duplicatePairsSql));
        Assert.Equal(0, await ScalarAsync<long>(survivingDuplicatesSql));
    }

    [Fact]
    public async Task NullOutcomes_398Rows_ExcludedFromRateDenominators_ButCountedInEventTypeTotal()
    {
        var rawNullOutcomes = await ScalarAsync<long>(
            "SELECT count(*) FROM activity_events WHERE outcome IS NULL");
        Assert.Equal(398, rawNullOutcomes);

        // Pick one representative slice (account 6, call_received, its whole history) and show the
        // event-type total and the rate denominator deliberately do not reconcile — and that the
        // gap between them is exactly the null-outcome footnote count, not a mystery.
        var eventTypeTotal = await ScalarAsync<long>(
            "SELECT sum(event_count) FROM weekly_activity_facts " +
            "WHERE account_id = 6 AND event_type = 'call_received'");
        var denominatorTotal = await ScalarAsync<long>(
            "SELECT sum(event_count) FROM weekly_activity_facts " +
            "WHERE account_id = 6 AND event_type = 'call_received' AND outcome IS NOT NULL");
        var nullOutcomeSlice = await ScalarAsync<long>(
            "SELECT sum(event_count) FROM weekly_activity_facts " +
            "WHERE account_id = 6 AND event_type = 'call_received' AND outcome IS NULL");

        Assert.NotEqual(eventTypeTotal, denominatorTotal); // deliberately do not reconcile
        Assert.Equal(eventTypeTotal, denominatorTotal + nullOutcomeSlice); // the gap is fully explained
        Assert.True(nullOutcomeSlice > 0);
    }

    [Fact]
    public async Task DurationSeconds_AppearsInNoResponsePayloadAnywhere()
    {
        // D3 (uniform noise) — dropped at the view (PLAN.md § activity_events_clean), the cheapest
        // way to make this true by construction. Asserted at the column level: the column simply
        // does not exist on the view a dashboard read can reach, so no future query against it can
        // reintroduce it by accident.
        var columnExists = await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns " +
            "WHERE table_name = 'activity_events_clean' AND column_name = 'duration_seconds'");

        Assert.Equal(0, columnExists);

        var factColumnExists = await ScalarAsync<long>(
            "SELECT count(*) FROM information_schema.columns " +
            "WHERE table_name = 'weekly_activity_facts' AND column_name = 'duration_seconds'");

        Assert.Equal(0, factColumnExists);
    }

    [Fact]
    public async Task D1Exclusion_FlagsOverlapsExclusion_EvenOnAWeekThatClearsTheCompletenessFloor()
    {
        // The week containing the D1 exclusion (2026-06-01) removes exactly 1 of 7 days for every
        // location, landing completeness at exactly min_week_completeness (6/7) — not below it —
        // so ExclusionReason legitimately stays null there. OverlapsExclusion is the distinct
        // signal that exists precisely so the sparkline still has something to hatch that point on.
        await using var db = fixture.CreateDbContext();
        var service = new DashboardQueryService(
            new EfDashboardReader(db), new EfAccountMetadataReader(db), TimeProvider.System);

        var result = await service.GetAsync(6, null, "2026-W23", 8, null, CancellationToken.None);

        Assert.NotNull(result);
        var d1WeekPoints = result.Sections
            .SelectMany(s => new[] { s.CountTile }.Concat(s.RateTiles))
            .SelectMany(t => t.Series)
            .Where(p => p.WeekStart == new DateOnly(2026, 6, 1))
            .ToList();

        Assert.NotEmpty(d1WeekPoints);
        Assert.All(d1WeekPoints, p => Assert.True(p.OverlapsExclusion));
        // At least one of those points is the count tile clearing the completeness floor exactly
        // (ExclusionReason null) — the specific case the flag exists to cover.
        Assert.Contains(d1WeekPoints, p => p.ExclusionReason is null);
    }
}
