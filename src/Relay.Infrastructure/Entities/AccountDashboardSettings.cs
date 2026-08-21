namespace Relay.Infrastructure.Entities;

/// <summary>Lets the customer own the definition of "normal". Supplies the defaults that the URL
/// params override. Primary key is AccountId (one row per account).</summary>
public sealed class AccountDashboardSettings
{
    public int AccountId { get; set; }
    public int DefaultComparisonWeeks { get; set; }
    public decimal TolerancePct { get; set; }
    public int MinBaselineEvents { get; set; }
    public int MinRateDenominator { get; set; }
    public int MinHistoryWeeks { get; set; }
    public decimal MinWeekCompleteness { get; set; }
    public decimal AmberFraction { get; set; }
    public DateTime UpdatedAt { get; set; }
}
