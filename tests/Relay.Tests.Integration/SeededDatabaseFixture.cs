using Microsoft.EntityFrameworkCore;
using Npgsql;
using Relay.Domain;
using Relay.Infrastructure;
using Testcontainers.PostgreSql;

namespace Relay.Tests.Integration;

/// <summary>
/// Boots a throwaway <c>postgres:16</c> container per test run, loads <c>schema.sql</c> +
/// <c>seed.sql</c> the same way <c>docker-compose.yml</c> does — as
/// <c>/docker-entrypoint-initdb.d</c> scripts, so this is provably the same seed every developer
/// runs locally — then applies the EF migration. Shared across the whole collection: seed.sql is
/// 2.4 MB, loaded once per run, never per test.
///
/// <b>Never points at the developer's live <c>relay_takehome_postgres</c> container.</b> §3 asserts
/// 805 / 12 / 398, figures that only hold against a pristine seed with migrations applied exactly
/// once.
/// </summary>
public sealed class SeededDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private NpgsqlDataSource? _dataSource;

    public SeededDatabaseFixture()
    {
        var repoRoot = FindRepoRoot();
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("relay_takehome")
            .WithUsername("relay")
            .WithPassword("relay")
            // Single-file bind mounts (not WithResourceMapping, which copies the file into a
            // directory at the target path rather than as the target path itself) — the exact
            // mechanism docker-compose.yml uses, so this is provably the same seed path.
            .WithBindMount(Path.Combine(repoRoot, "schema.sql"), "/docker-entrypoint-initdb.d/1_schema.sql")
            .WithBindMount(Path.Combine(repoRoot, "seed.sql"), "/docker-entrypoint-initdb.d/2_seed.sql")
            .Build();
    }

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // The `outcome_polarity` enum type does not exist until the migration below creates it
        // (01_types.sql), so migration runs through a plain connection string first — a raw SQL
        // migration never touches an enum-typed column, so it does not need the mapping. Only
        // after the type exists can a NpgsqlDataSource resolve it, matching Program.cs.
        var migrationOptions = new DbContextOptionsBuilder<RelayDbContext>()
            .UseNpgsql(ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        await using (var migrationDb = new RelayDbContext(migrationOptions))
        {
            await migrationDb.Database.MigrateAsync();
        }

        // The developer's long-lived relay_takehome_postgres container has accurate planner
        // statistics from autovacuum having run over time; a Testcontainers instance is seeded and
        // migrated within seconds and has none, which is exactly the situation where a planner can
        // choose a materially different plan (a broad Seq Scan instead of an Index Scan on a table
        // it under-estimates the selectivity of) for the same query. FactViewPushdownTests asserts
        // real production-like plan shape, so it needs the statistics a production database would
        // actually have.
        await using (var analyzeConnection = await OpenConnectionAsync())
        await using (var analyzeCommand = new NpgsqlCommand("ANALYZE;", analyzeConnection))
        {
            await analyzeCommand.ExecuteNonQueryAsync();
        }

        // Built once, after the migration creates outcome_polarity — mirrors Program.cs's
        // NpgsqlDataSourceBuilder.MapEnum<OutcomePolarity> exactly, which EfDashboardReader's
        // aggregate LINQ queries need to resolve OutcomeCatalog.Polarity correctly.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(ConnectionString);
        dataSourceBuilder.MapEnum<OutcomePolarity>("outcome_polarity");
        _dataSource = dataSourceBuilder.Build();
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    /// <summary>A fresh <see cref="RelayDbContext"/> against the container, configured exactly the
    /// way <c>Program.cs</c> configures the production one (snake_case naming, mapped
    /// <c>outcome_polarity</c> enum) so a query that works here works in production and vice versa.</summary>
    public RelayDbContext CreateDbContext()
    {
        if (_dataSource is null)
        {
            throw new InvalidOperationException($"{nameof(CreateDbContext)} called before {nameof(InitializeAsync)} completed.");
        }

        var options = new DbContextOptionsBuilder<RelayDbContext>()
            .UseNpgsql(_dataSource)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new RelayDbContext(options);
    }

    /// <summary>A raw ADO.NET connection for assertions that fall outside what EF can express
    /// (row counts against views EF doesn't map, <c>EXPLAIN</c>, etc.). Caller owns disposal.</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
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

[CollectionDefinition(Name)]
public sealed class SeededDatabaseCollection : ICollectionFixture<SeededDatabaseFixture>
{
    public const string Name = "Seeded database";
}
