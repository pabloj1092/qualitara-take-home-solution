namespace Relay.Domain;

/// <summary>
/// Ordered worst-to-best. InsufficientData outranks every other status: the dashboard would
/// rather admit it cannot judge a tile than render a confident-looking wrong verdict.
/// </summary>
public enum TileStatus
{
    InsufficientData,
    PartialWeek,
    Breach,
    Warning,
    Normal,
}
