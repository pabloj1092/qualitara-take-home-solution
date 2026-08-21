namespace Relay.Infrastructure.Entities;

/// <summary>Turns the hardcoded 2026-06-03 exclusion into auditable data. Null
/// <see cref="AccountId"/>/<see cref="Location"/>/<see cref="EventType"/> means "any" — the
/// wildcard columns are what let scope be data rather than code.</summary>
public sealed class DataQualityExclusion
{
    public int Id { get; set; }
    public int? AccountId { get; set; }
    public string? Location { get; set; }
    public string? EventType { get; set; }
    public DateOnly FromDate { get; set; }
    public DateOnly ToDate { get; set; }
    public required string Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
