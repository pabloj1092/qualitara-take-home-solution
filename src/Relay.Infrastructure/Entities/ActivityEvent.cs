namespace Relay.Infrastructure.Entities;

/// <summary>Maps the pre-existing <c>activity_events</c> table (schema.sql). Excluded from
/// migrations — EF never owns this table's DDL. Not queried directly by the dashboard read path
/// (that goes through <c>weekly_activity_facts</c>); present for model completeness and ad hoc
/// / test use.</summary>
public sealed class ActivityEvent
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public required string Location { get; set; }
    public required string EventType { get; set; }
    public DateTime OccurredAt { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Outcome { get; set; }
}
