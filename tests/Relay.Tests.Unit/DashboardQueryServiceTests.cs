using Relay.Application;
using Relay.Application.Abstractions;
using Relay.Application.Dashboard;
using Relay.Application.Testing;
using Relay.Domain;

namespace Relay.Tests.Unit;

/// <summary>
/// The orchestrator against the stub ports — no database. Regression coverage for two bugs found
/// in review: a null <c>LatestCompleteWeek</c> crashing the default-week resolution, and viewing
/// the earliest available week (where <c>maxWindowForWeek</c> is legitimately 0) being rejected
/// instead of clamped.
/// </summary>
public class DashboardQueryServiceTests
{
    private static readonly WeekRange FirstWeek = WeekRange.FromIsoWeek("2026-W01");
    private static readonly WeekRange LatestWeekWithData = WeekRange.FromIsoWeek("2026-W10");

    private static (DashboardQueryService Service, StubDashboardReader Reader) BuildService(int accountId, AccountMeta meta)
    {
        var metadataReader = new StubAccountMetadataReader().Seed(accountId, meta);
        var dashboardReader = new StubDashboardReader().Seed(
            accountId,
            new DashboardReadModel(
                new AccountInfo(accountId, "Test Account", "America/New_York"),
                meta.Locations,
                [],
                new DisclosureData(0, [])));

        return (new DashboardQueryService(dashboardReader, metadataReader, TimeProvider.System), dashboardReader);
    }

    [Fact]
    public async Task GetAsync_NullLatestCompleteWeek_FallsBackToLatestWeekWithDataInsteadOfThrowing()
    {
        // An account whose locations never all report a full week at once has a null
        // LatestCompleteWeek (ComputeLatestCompleteWeekAsync can legitimately return nothing).
        var meta = new AccountMeta(
            1, "No Complete Week Account", "America/New_York",
            [new LocationInfo(1, "Site A", null, null)],
            FirstWeek, LatestWeekWithData, LatestCompleteWeek: null,
            MaxWindowForWeek: 9, DefaultWindow: 8, ThresholdSet.Defaults);
        var (service, _) = BuildService(1, meta);

        var result = await service.GetAsync(1, null, null, null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(LatestWeekWithData.Start, result.ViewedWeek.Start);
    }

    [Fact]
    public async Task GetAsync_EarliestWeek_ClampsWindowInsteadOfRejecting()
    {
        // maxWindowForWeek is legitimately 0 at the very first week — it must still be viewable,
        // with the default (or any requested) window clamped rather than 400ing against a range
        // (1..0) that no value could ever satisfy.
        var meta = new AccountMeta(
            1, "Earliest Week Account", "America/New_York",
            [new LocationInfo(1, "Site A", null, null)],
            FirstWeek, LatestWeekWithData, LatestCompleteWeek: FirstWeek,
            MaxWindowForWeek: 0, DefaultWindow: 8, ThresholdSet.Defaults);
        var (service, _) = BuildService(1, meta);

        var result = await service.GetAsync(1, null, null, null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(FirstWeek.Start, result.ViewedWeek.Start);
        Assert.Equal(8, result.Window.Requested);
    }

    [Fact]
    public async Task GetAsync_WindowBelowOne_StillRejected()
    {
        var meta = new AccountMeta(
            1, "Test Account", "America/New_York",
            [new LocationInfo(1, "Site A", null, null)],
            FirstWeek, LatestWeekWithData, LatestCompleteWeek: LatestWeekWithData,
            MaxWindowForWeek: 9, DefaultWindow: 8, ThresholdSet.Defaults);
        var (service, _) = BuildService(1, meta);

        await Assert.ThrowsAsync<RequestValidationException>(
            () => service.GetAsync(1, null, null, 0, null, CancellationToken.None));
    }
}
