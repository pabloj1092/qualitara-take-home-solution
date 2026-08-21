namespace Relay.Infrastructure.Entities;

/// <summary>The week spine. Without it, zero-event weeks vanish from the result set and an
/// N-week window quietly becomes shorter, biased upward because the missing weeks were the quiet
/// ones. Primary key is WeekStart.</summary>
public sealed class IsoWeek
{
    public DateOnly WeekStart { get; set; }
    public DateOnly WeekEnd { get; set; }
    public int IsoYear { get; set; }
    public int IsoWeekNumber { get; set; }
}
