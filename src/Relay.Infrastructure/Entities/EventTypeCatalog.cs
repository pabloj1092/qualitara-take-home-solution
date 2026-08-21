namespace Relay.Infrastructure.Entities;

/// <summary>Drives section order and labels — removes hardcoded event-type strings from Angular.</summary>
public sealed class EventTypeCatalog
{
    public required string Code { get; set; }
    public required string DisplayName { get; set; }
    public int SortOrder { get; set; }
}
