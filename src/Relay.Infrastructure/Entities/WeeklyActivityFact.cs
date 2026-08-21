namespace Relay.Infrastructure.Entities;

/// <summary>
/// Keyless entity backed by the <c>weekly_activity_facts</c> view (plain, not materialized — see
/// PLAN.md § Why a view, not a materialized view). Dense over
/// <c>locations × iso_weeks × outcome slots</c>: <see cref="EventCount"/> is 0 where nothing
/// happened that week, and <see cref="Outcome"/> is null for the "missing outcome" slot each
/// event type carries. No <c>OutcomeKey</c> — that column existed only to key a unique index a
/// plain view does not need.
/// </summary>
public sealed class WeeklyActivityFact
{
    public int AccountId { get; set; }
    public int LocationId { get; set; }
    public DateOnly WeekStartLocal { get; set; }
    public required string EventType { get; set; }
    public string? Outcome { get; set; }
    public int EventCount { get; set; }
    public int DaysIncluded { get; set; }
    public int ExpectedDays { get; set; }
}
