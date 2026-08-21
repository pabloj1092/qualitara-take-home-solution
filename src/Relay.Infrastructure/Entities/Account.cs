namespace Relay.Infrastructure.Entities;

/// <summary>Maps the pre-existing <c>accounts</c> table (schema.sql). Excluded from migrations —
/// EF never owns this table's DDL.</summary>
public sealed class Account
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Industry { get; set; }
    public required string Timezone { get; set; }
    public DateTime CreatedAt { get; set; }
}
