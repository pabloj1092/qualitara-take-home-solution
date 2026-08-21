using Microsoft.AspNetCore.Mvc;
using Relay.Api.Dtos;
using Relay.Api.Validation;
using Relay.Application.Baseline;
using Relay.Application.Dashboard;
using Relay.Domain;

namespace Relay.Api.Controllers;

[ApiController]
[Route("api/accounts/{accountId:int}")]
public sealed class AccountsController(DashboardQueryService dashboardQueryService, MetaQueryService metaQueryService)
    : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardResponseDto>> GetDashboard(
        int accountId,
        [FromQuery] string? locations,
        [FromQuery] string? week,
        [FromQuery] int? window,
        [FromQuery] decimal? tolerance,
        CancellationToken ct)
    {
        DashboardRequestValidator.ValidateDashboardRequest(week, window, tolerance);

        var result = await dashboardQueryService.GetAsync(accountId, locations, week, window, tolerance, ct);
        if (result is null)
        {
            return NotFound(new { message = $"Account {accountId} was not found." });
        }

        return Ok(ToDto(result));
    }

    [HttpGet("meta")]
    public async Task<ActionResult<MetaResponseDto>> GetMeta(int accountId, [FromQuery] string? week, CancellationToken ct)
    {
        DashboardRequestValidator.ValidateMetaRequest(week);

        var result = await metaQueryService.GetAsync(accountId, week, ct);
        if (result is null)
        {
            return NotFound(new { message = $"Account {accountId} was not found." });
        }

        return Ok(ToDto(result));
    }

    private static DashboardResponseDto ToDto(DashboardResult result)
    {
        var previous = result.ViewedWeek.Previous();
        var next = result.ViewedWeek.Next();
        var hasPrevious = result.FirstWeek is not null && previous.Start >= result.FirstWeek.Value.Start;
        var hasNext = result.LatestWeekWithData is not null && next.Start <= result.LatestWeekWithData.Value.Start;

        return new DashboardResponseDto(
            result.Account.Id,
            result.Account.Name,
            result.Account.Timezone,
            $"All figures in {result.Account.Timezone} (account timezone).",
            new WeekDto(
                result.ViewedWeek.ToIsoWeek(), result.ViewedWeek.Start, result.ViewedWeek.End,
                result.ViewedWeek.Label(), hasPrevious, hasNext),
            new WindowDto(result.Window.Requested, result.Window.Effective),
            result.TolerancePct,
            result.Locations.Select(l => new LocationDto(l.Id, l.Name, l.Selected)).ToList(),
            result.Sections.Select(ToDto).ToList(),
            new DisclosuresDto(
                result.Disclosures.NullOutcomeCount,
                result.Disclosures.Exclusions
                    .Select(x => new ExclusionDto(x.FromDate, x.ToDate, x.Reason, x.WeeksAffected))
                    .ToList()));
    }

    private static SectionDto ToDto(SectionResult section) =>
        new(section.EventType, section.DisplayName, section.SortOrder,
            ToDto(section.CountTile), section.RateTiles.Select(ToDto).ToList());

    private static TileDto ToDto(TileResult tile) =>
        new(
            TileKeyString(tile.Key), tile.Label, tile.Kind,
            tile.Value, tile.DeltaPct, tile.DeltaPp,
            tile.BaselineMean, tile.BandLow, tile.BandHigh,
            tile.Denominator, tile.Status, tile.ReasonCode, tile.BaselineWeeksUsed,
            tile.Series.Select(ToDto).ToList());

    private static SeriesPointDto ToDto(SeriesPoint point) =>
        new(point.WeekStart, point.Value, point.Denominator, point.DaysIncluded, point.ExpectedDays,
            point.IncludedInBaseline, point.ExclusionReason, point.IsViewedWeek, point.OverlapsExclusion);

    private static string TileKeyString(TileKey key) =>
        key.Outcome is null ? key.EventType : $"{key.EventType}.{key.Outcome}";

    private static MetaResponseDto ToDto(MetaResult result) =>
        new(
            result.Locations.Select(l => new MetaLocationDto(l.Id, l.Name, l.OpenedOn, l.ClosedOn)).ToList(),
            result.FirstWeek, result.LatestWeekWithData, result.LatestCompleteWeek, result.MaxWindowForWeek,
            new MetaDefaultsDto(
                result.Defaults.Week, result.Defaults.Window, result.Defaults.TolerancePct,
                result.Defaults.MinBaselineEvents, result.Defaults.MinRateDenominator, result.Defaults.MinHistoryWeeks,
                result.Defaults.MinWeekCompleteness, result.Defaults.AmberFraction));
}
