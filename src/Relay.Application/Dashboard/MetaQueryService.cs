using Relay.Application.Abstractions;
using Relay.Domain;

namespace Relay.Application.Dashboard;

public sealed record MetaDefaults(
    string? Week,
    int Window,
    decimal TolerancePct,
    int MinBaselineEvents,
    int MinRateDenominator,
    int MinHistoryWeeks,
    decimal MinWeekCompleteness,
    decimal AmberFraction);

public sealed record MetaResult(
    IReadOnlyList<LocationInfo> Locations,
    string? FirstWeek,
    string? LatestWeekWithData,
    string? LatestCompleteWeek,
    int MaxWindowForWeek,
    MetaDefaults Defaults);

public sealed class MetaQueryService(IAccountMetadataReader metadataReader)
{
    public async Task<MetaResult?> GetAsync(int accountId, string? weekParam, CancellationToken ct)
    {
        WeekRange? week = null;
        if (!string.IsNullOrWhiteSpace(weekParam))
        {
            try
            {
                week = WeekRange.FromIsoWeek(weekParam);
            }
            catch (FormatException)
            {
                throw new RequestValidationException(
                    "week", $"'{weekParam}' is not a valid ISO week (expected yyyy-Www).");
            }
        }

        var meta = await metadataReader.ReadAsync(accountId, week, ct);
        if (meta is null)
        {
            return null;
        }

        return new MetaResult(
            meta.Locations,
            meta.FirstWeek?.ToIsoWeek(),
            meta.LatestWeekWithData?.ToIsoWeek(),
            meta.LatestCompleteWeek?.ToIsoWeek(),
            meta.MaxWindowForWeek,
            new MetaDefaults(
                meta.LatestCompleteWeek?.ToIsoWeek(),
                meta.DefaultWindow,
                meta.Thresholds.TolerancePct,
                meta.Thresholds.MinBaselineEvents,
                meta.Thresholds.MinRateDenominator,
                meta.Thresholds.MinHistoryWeeks,
                meta.Thresholds.MinWeekCompleteness,
                meta.Thresholds.AmberFraction));
    }
}
