using Relay.Domain;

namespace Relay.Infrastructure.Entities;

/// <summary>Holds the polarity table (Requirements §Data-quality). Makes an outcome's polarity a
/// customer setting rather than an argument in code. Primary key is (EventType, Code).</summary>
public sealed class OutcomeCatalog
{
    public required string EventType { get; set; }
    public required string Code { get; set; }
    public required string DisplayName { get; set; }
    public OutcomePolarity Polarity { get; set; }
    public int SortOrder { get; set; }
}
