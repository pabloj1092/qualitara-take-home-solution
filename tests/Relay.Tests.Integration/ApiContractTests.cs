using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Relay.Api.Dtos;

namespace Relay.Tests.Integration;

/// <summary>Points <c>Program</c> at the fixture's container instead of the developer's
/// <c>appsettings.json</c> connection string, and forces the real EF reader path (never the
/// stub — <c>UseStubDashboardReader</c> is a Stage 1 checkpoint-only flag).</summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Relay"] = connectionString,
                ["UseStubDashboardReader"] = "false",
            });
        });
    }
}

/// <summary>Requirements §5 "API contract" — integration, <c>WebApplicationFactory</c> against the
/// seeded database.</summary>
[Collection(SeededDatabaseCollection.Name)]
public sealed class ApiContractTests : IDisposable
{
    // Mirrors Program.cs's JSON configuration exactly (camelCase string enums) — this test client
    // deserializes the server's real wire format, not a re-derived shape of its own.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly ApiFactory _factory;
    private readonly HttpClient _client;

    public ApiContractTests(SeededDatabaseFixture fixture)
    {
        _factory = new ApiFactory(fixture.ConnectionString);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Meta_MaxWindowForWeek_ShrinksAsWeekMovesBackwards()
    {
        var atLatest = await _client.GetFromJsonAsync<MetaResponseDto>(
            "/api/accounts/6/meta?week=2026-W30", JsonOptions);
        var earlier = await _client.GetFromJsonAsync<MetaResponseDto>(
            "/api/accounts/6/meta?week=2026-W10", JsonOptions);

        Assert.NotNull(atLatest);
        Assert.NotNull(earlier);
        Assert.True(earlier.MaxWindowForWeek < atLatest.MaxWindowForWeek);
    }

    [Fact]
    public async Task Meta_LastCompleteWeek_ReportsMaxWindowForWeek24_NotTheLeadingPartialWeek()
    {
        // The resolved design decision behind EfAccountMetadataReader's fix: the global spine's
        // first calendar week is always 1-of-7 days and can never contribute to a baseline, so it
        // is excluded from the usable window (PLAN.md Verified facts table: 24, not the raw
        // calendar-week count of 25).
        var meta = await _client.GetFromJsonAsync<MetaResponseDto>("/api/accounts/6/meta?week=2026-W30", JsonOptions);

        Assert.NotNull(meta);
        Assert.Equal(24, meta.MaxWindowForWeek);
    }

    [Fact]
    public async Task Account20_QuietHarborSpa_ReturnsEmptySections_Not404Not500()
    {
        var response = await _client.GetAsync("/api/accounts/20/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DashboardResponseDto>(JsonOptions);
        Assert.NotNull(body);
        Assert.Empty(body.Sections);
        Assert.Empty(body.Locations);
    }

    [Fact]
    public async Task Account16_OldTownBarbers_IsMostlyInsufficientData_NeverAWallOfRed()
    {
        var response = await _client.GetAsync("/api/accounts/16/dashboard");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DashboardResponseDto>(JsonOptions);
        Assert.NotNull(body);

        var tiles = body.Sections
            .SelectMany(s => new[] { s.CountTile }.Concat(s.RateTiles))
            .ToList();

        Assert.NotEmpty(tiles);
        var breaching = tiles.Count(t => t.Status == Relay.Domain.TileStatus.Breach);
        var insufficientData = tiles.Count(t => t.Status == Relay.Domain.TileStatus.InsufficientData);

        Assert.True(insufficientData > tiles.Count / 2, $"expected mostly InsufficientData, got {insufficientData}/{tiles.Count}");
        Assert.True(breaching < tiles.Count / 3, $"expected far from a wall of red, got {breaching}/{tiles.Count} breaching");
    }

    [Fact]
    public async Task Account6_MetroCollision_HasFifteenLocations_AndDisclosesTheExclusion()
    {
        var response = await _client.GetAsync("/api/accounts/6/dashboard?week=2026-W23&window=8");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<DashboardResponseDto>(JsonOptions);
        Assert.NotNull(body);

        Assert.Equal(15, body.Locations.Count); // the largest location set in the seed

        var exclusion = Assert.Single(body.Disclosures.Exclusions);
        Assert.Equal(new DateOnly(2026, 6, 3), exclusion.FromDate);
        Assert.Contains(new DateOnly(2026, 6, 1), exclusion.WeeksAffected);

        // The regression this exercises end-to-end: the D1 week sits exactly at the completeness
        // floor, so its ExclusionReason can be null, but OverlapsExclusion must still be true.
        var overlappingPoint = body.Sections
            .SelectMany(s => new[] { s.CountTile }.Concat(s.RateTiles))
            .SelectMany(t => t.Series)
            .Where(p => p.WeekStart == new DateOnly(2026, 6, 1))
            .ToList();

        Assert.NotEmpty(overlappingPoint);
        Assert.All(overlappingPoint, p => Assert.True(p.OverlapsExclusion));
    }

    [Fact]
    public async Task UnknownAccount_Returns404()
    {
        var response = await _client.GetAsync("/api/accounts/9999/dashboard");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/accounts/6/dashboard?window=0", "window")]
    [InlineData("/api/accounts/6/dashboard?tolerance=101", "tolerance")]
    [InlineData("/api/accounts/6/dashboard?week=not-a-week", "week")]
    public async Task InvalidRequestParameter_Returns400_NamingTheOffendingParameter(string url, string expectedParameter)
    {
        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<JsonDocument>(JsonOptions);
        Assert.NotNull(problem);
        Assert.Equal(expectedParameter, problem.RootElement.GetProperty("parameter").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.RootElement.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Dashboard_SameQueryStringTwice_ReturnsIdenticalPayload()
    {
        const string url = "/api/accounts/6/dashboard?week=2026-W23&window=8&tolerance=40";

        var first = await _client.GetStringAsync(url);
        var second = await _client.GetStringAsync(url);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Dashboard_MatchesTheCommittedSnapshot_ByteIdentical()
    {
        // §4's first assertion: run `dotnet test`, then `TZ=Asia/Tokyo dotnet test` — this
        // snapshot must be byte-identical both times, proving no server-process wall-clock time
        // ever leaks into a customer-facing figure. Every field in this response is driven by the
        // explicit week/window/tolerance query params or the database, never TimeProvider.System.
        var actual = await _client.GetStringAsync("/api/accounts/6/dashboard?week=2026-W23&window=8&tolerance=40");

        var snapshotPath = Path.Combine(
            FindRepoRoot(), "tests", "Relay.Tests.Integration", "PayloadSnapshots", "account6_2026-W23.json");
        var expected = await File.ReadAllTextAsync(snapshotPath);

        Assert.Equal(expected, actual);
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "schema.sql")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException(
                $"Could not locate repo root (a directory containing schema.sql) above {AppContext.BaseDirectory}.");
    }
}
