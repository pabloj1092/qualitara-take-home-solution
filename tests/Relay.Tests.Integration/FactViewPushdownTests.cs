using System.Text.RegularExpressions;
using Npgsql;

namespace Relay.Tests.Integration;

/// <summary>
/// The standing guard on the view-not-materialized decision (PLAN.md § Why a view, not a
/// materialized view / § Pushdown check): <c>weekly_activity_facts</c> only stays cheap if
/// <c>WHERE account_id = ...</c> reaches the base tables instead of the planner building the full
/// ~18.6k-row cross join and filtering at the end. That is planner behaviour, not a guarantee, so
/// it is asserted here rather than assumed. Asserts plan <em>shape</em> only — never a wall-clock
/// number, which would be flaky by construction on a laptop.
/// </summary>
[Collection(SeededDatabaseCollection.Name)]
public partial class FactViewPushdownTests(SeededDatabaseFixture fixture)
{
    [GeneratedRegex(@"Seq Scan on activity_events\b")]
    private static partial Regex SeqScanOnActivityEvents();

    [GeneratedRegex(@"Index Scan using ix_activity_events_\S+ on activity_events")]
    private static partial Regex IndexScanOnActivityEvents();

    [Fact]
    public async Task AccountPredicate_PushesDownToAnIndexScanOnActivityEvents_NeverASeqScan()
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            EXPLAIN (ANALYZE, BUFFERS)
            SELECT event_type, outcome, sum(event_count)
            FROM   weekly_activity_facts
            WHERE  account_id = 6
              AND  week_start_local BETWEEN DATE '2026-05-25' AND DATE '2026-07-20'
            GROUP  BY 1, 2
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
        {
            lines.Add(reader.GetString(0));
        }

        var plan = string.Join('\n', lines);

        Assert.False(SeqScanOnActivityEvents().IsMatch(plan), $"Plan sequentially scans activity_events:\n{plan}");
        Assert.True(IndexScanOnActivityEvents().IsMatch(plan), $"No Index Scan on activity_events found:\n{plan}");
    }
}
