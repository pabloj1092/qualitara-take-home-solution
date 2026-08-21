namespace Relay.Infrastructure.Entities;

/// <summary>Backfilled from the 69 distinct <c>(account_id, location)</c> pairs in
/// <c>activity_events</c> (migration step 5).</summary>
public sealed class Location
{
    public int Id { get; set; }
    public int AccountId { get; set; }
    public required string Name { get; set; }
    public DateOnly? OpenedOn { get; set; }
    public DateOnly? ClosedOn { get; set; }
    public DateTime CreatedAt { get; set; }
}
