using Microsoft.EntityFrameworkCore;
using Relay.Domain;
using Relay.Infrastructure.Entities;

namespace Relay.Infrastructure;

public sealed class RelayDbContext(DbContextOptions<RelayDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<EventTypeCatalog> EventTypeCatalogs => Set<EventTypeCatalog>();
    public DbSet<OutcomeCatalog> OutcomeCatalogs => Set<OutcomeCatalog>();
    public DbSet<AccountDashboardSettings> AccountDashboardSettings => Set<AccountDashboardSettings>();
    public DbSet<DataQualityExclusion> DataQualityExclusions => Set<DataQualityExclusion>();
    public DbSet<IsoWeek> IsoWeeks => Set<IsoWeek>();
    public DbSet<WeeklyActivityFact> WeeklyActivityFacts => Set<WeeklyActivityFact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresEnum<OutcomePolarity>("outcome_polarity");

        // Pre-existing tables from schema.sql. EF reads them but never owns their DDL.
        modelBuilder.Entity<Account>(b =>
        {
            b.ToTable("accounts", t => t.ExcludeFromMigrations());
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
        });

        modelBuilder.Entity<ActivityEvent>(b =>
        {
            b.ToTable("activity_events", t => t.ExcludeFromMigrations());
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedNever();
        });

        // Additive objects — migration 001 (Stage 2) owns these.
        modelBuilder.Entity<Location>(b =>
        {
            b.ToTable("locations");
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.AccountId, x.Name }).IsUnique();
        });

        modelBuilder.Entity<EventTypeCatalog>(b =>
        {
            b.ToTable("event_type_catalog");
            b.HasKey(x => x.Code);
        });

        modelBuilder.Entity<OutcomeCatalog>(b =>
        {
            b.ToTable("outcome_catalog");
            b.HasKey(x => new { x.EventType, x.Code });
        });

        modelBuilder.Entity<AccountDashboardSettings>(b =>
        {
            b.ToTable("account_dashboard_settings");
            b.HasKey(x => x.AccountId);
            b.Property(x => x.AccountId).ValueGeneratedNever();
        });

        modelBuilder.Entity<DataQualityExclusion>(b =>
        {
            b.ToTable("data_quality_exclusions");
            b.HasKey(x => x.Id);
        });

        modelBuilder.Entity<IsoWeek>(b =>
        {
            b.ToTable("iso_weeks");
            b.HasKey(x => x.WeekStart);
        });

        // Plain view, not materialized (PLAN.md § Why a view, not a materialized view). Keyless
        // mapping is load-bearing: without ToView, EF treats this as a table and Add-Migration
        // emits a colliding CREATE TABLE.
        modelBuilder.Entity<WeeklyActivityFact>(b =>
        {
            b.HasNoKey();
            b.ToView("weekly_activity_facts");
        });
    }
}
