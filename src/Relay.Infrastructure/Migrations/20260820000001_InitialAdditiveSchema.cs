using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Up() is a sequence of migrationBuilder.Sql() calls against embedded Migrations/Sql/*.sql
    /// resources (PLAN.md § Migration 001 — detail). The order is load-bearing: each step reads
    /// relations the previous ones create, so a relation-does-not-exist error means the order
    /// broke, never a silently empty view. Every file is independently idempotent — EF's
    /// migrations history makes a second `dotnet run` a no-op, but each .sql is also safe to
    /// re-execute by hand.
    /// </remarks>
    public partial class InitialAdditiveSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationSql.Load("01_types.sql"));
            migrationBuilder.Sql(MigrationSql.Load("02_tables.sql"));
            migrationBuilder.Sql(MigrationSql.Load("03_indexes.sql"));
            migrationBuilder.Sql(MigrationSql.Load("04_seed_catalogs.sql"));
            migrationBuilder.Sql(MigrationSql.Load("05_locations_backfill.sql"));
            migrationBuilder.Sql(MigrationSql.Load("06_activity_events_local.sql"));
            migrationBuilder.Sql(MigrationSql.Load("07_iso_weeks.sql"));
            migrationBuilder.Sql(MigrationSql.Load("08_activity_events_clean.sql"));
            migrationBuilder.Sql(MigrationSql.Load("09_weekly_activity_facts.sql"));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Drops only additive objects, in reverse dependency order. accounts and activity_events
        /// keep every row — this migration never owns their DDL.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS weekly_activity_facts;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS activity_events_clean;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS check_date_shift;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS check_timezone_applied;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS check_local_hour_plausibility;");
            migrationBuilder.Sql("DROP VIEW IF EXISTS activity_events_local;");

            migrationBuilder.Sql("DROP TABLE IF EXISTS iso_weeks;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS data_quality_exclusions;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS account_dashboard_settings;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS outcome_catalog;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS event_type_catalog;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS locations;");

            migrationBuilder.Sql("DROP TYPE IF EXISTS outcome_polarity;");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_activity_events_account_id_location_event_type_occurred_at;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_activity_events_account_id_occurred_at;");
        }
    }
}
