namespace Relay.Domain;

/// <summary>The shared vocabulary both sides of the read port speak.</summary>
public sealed record TileKey(string EventType, string? Outcome, TileKind Kind);
