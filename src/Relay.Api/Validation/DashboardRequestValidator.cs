using System.Text.RegularExpressions;
using Relay.Application;

namespace Relay.Api.Validation;

/// <summary>
/// The stateless half of request validation — checks that don't need <c>AccountMeta</c> and so
/// don't need a database round trip. Account-dependent checks (window vs. <c>maxWindowForWeek</c>,
/// week vs. the account's data range, unknown location names) live in
/// <c>DashboardQueryService</c>, which already has to read the account to answer them.
/// </summary>
public static partial class DashboardRequestValidator
{
    [GeneratedRegex(@"^\d{4}-W\d{2}$")]
    private static partial Regex IsoWeekShape();

    public static void ValidateDashboardRequest(string? week, int? window, decimal? tolerance)
    {
        if (week is not null && !IsoWeekShape().IsMatch(week))
        {
            throw new RequestValidationException("week", $"'{week}' is not a valid ISO week (expected yyyy-Www).");
        }

        if (window is < 1)
        {
            throw new RequestValidationException("window", $"'{window}' must be at least 1.");
        }

        if (tolerance is < 1 or > 100)
        {
            throw new RequestValidationException("tolerance", $"'{tolerance}' must be between 1 and 100.");
        }
    }

    public static void ValidateMetaRequest(string? week)
    {
        if (week is not null && !IsoWeekShape().IsMatch(week))
        {
            throw new RequestValidationException("week", $"'{week}' is not a valid ISO week (expected yyyy-Www).");
        }
    }
}
