# Staged implementation plan — Relay account dashboard

## Context

`RequirementsFinal.md` is a settled spec for an account-level weekly analytics dashboard: per event type, a
count tile plus outcome rate tiles, each judged against the account's own trailing baseline and rendered with a
server-computed status. The repo today contains **only** the database (`docker-compose.yml`, `schema.sql`,
`seed.sql`), the data audit (`docs/relay-data-audit.md`), the hand-run local-time layer (`sql/local_time.sql`)
and the spec. There is no application code and no migration; every object the spec's *Database schema to add*
describes is missing from a fresh clone.

The point of the design is restraint. The audit's principal finding is that per-location outcome rates in this
data are indistinguishable from noise (r = 0.06 half-over-half, χ² p ≈ 0.19), and that at ~1 event/location/day
a naive dashboard is 54.7% red in an average week. So the build is arranged around *not* making claims the data
cannot support: `InsufficientData` outranks every other status, incomplete weeks are dropped rather than
prorated, zero-event weeks are real points rather than gaps, and the verdict logic lives in pure C# where it can
be unit-tested exhaustively without a database.

**Outcome**: `docker compose up -d && dotnet run` (plus `npm start`) gives a working dashboard on the existing
seed, with the spec's §1–§6 test plan implemented.

### Build order (this revision)

Stages follow the requested sequence — **backend → migrations → backend tests → frontend → frontend tests** —
rather than the vertical-slice order of the previous draft. Two consequences, stated up front because they
change how Stage 1 is verified rather than what gets built:

- **Stage 1 compiles but cannot serve real data.** The views it queries do not exist until Stage 2, so its
  checkpoint is a build + OpenAPI + pure-core smoke, not a `curl`. The first end-to-end proof is Stage 2's.
- **`StatusEvaluator` and `BaselineService` get written before their tests exist.** Mitigation: write them
  directly against the §1 and §2 tables as a line-by-line checklist, so Stage 3 is *confirmation*, not
  discovery. If Stage 3 finds the ladder wrong, that is a real cost of this ordering and worth noting.

Everything else is order-independent: the migration content, the core's signatures, the API contract, the
component tree and the open questions are unchanged.

### Verified facts (queried against the live container, 2026-08-20)

| Fact | Value |
|---|---|
| Distinct `(account_id, location)` pairs | **69** |
| Duplicate pairs on `(account_id, location, event_type, occurred_at)` | **12** (identical on duration + outcome too) |
| NULL outcomes | **398** of 12,626 |
| Rows on 2026-06-03 for account 6 (Metro Collision) | **805** |
| Local date range | 2026-02-01 (Sun) → 2026-07-27 (Mon) |
| ISO weeks spanned | **27** (2026-01-26 → 2026-07-27); first and last are 1-of-7-day weeks |
| Last *complete* week | 2026-07-20 |
| Max window at the last complete week | **24** |
| Accounts with events | 19 of 20 (account 20 = Quiet Harbor Spa has none) |
| Distinct timezones | 6 IANA zones |

### Prerequisite (blocker for Stage 0)

**The .NET SDK is not installed on this machine** (`dotnet: command not found`). Node 24.13 / npm 11.6 are
present; Angular CLI is not.

```bash
brew install --cask dotnet-sdk && npm i -g @angular/cli
```

---

## Stage 0 · Solution skeleton

Setup, not a build phase. Empty but wired-up .NET solution and Angular workspace, no logic.

**Files created**
```
Relay.sln
Directory.Build.props                  (net8.0, nullable enable, TreatWarningsAsErrors, LangVersion latest)
.editorconfig
src/Relay.Api/                         Program.cs, appsettings.json, appsettings.Development.json
                                       (refs Application + Infrastructure — composition root, and the
                                        only project allowed to reference both)
src/Relay.Application/                 (class lib, refs Domain only — no EF, enforced by an arch test)
src/Relay.Domain/                      (class lib, no refs — this is the point)
src/Relay.Infrastructure/              (class lib, refs Application + Npgsql.EntityFrameworkCore.PostgreSQL —
                                        it implements Application's ports, so it points inward)
tests/Relay.Tests.Unit/                (xUnit, refs Application + Domain — no EF reference, enforced)
tests/Relay.Tests.Integration/         (xUnit, Testcontainers.PostgreSql, refs Api + Infrastructure)
web/                                   (ng new relay-web --standalone --routing --style=scss; ng-zorro-antd)
.gitignore                             (+ bin/ obj/ node_modules/ .DS_Store)
```

Project reference direction is a design constraint, not bookkeeping. Dependencies point **inward only**:
`Api → Infrastructure → Application → Domain`, and `Domain` references nothing. `Application` never references
EF Core; it declares **ports** (interfaces) that `Infrastructure` implements, and `Api` wires the two together
at startup. That is what lets `Relay.Tests.Unit` reference `Application` and still be EF-free, and what makes
the arch test in Stage 1 pass rather than fail on the first build.

**Prove it** — `dotnet build` clean at `TreatWarningsAsErrors`; `dotnet run` serves `GET /health` →
`200 {"status":"ok"}`; `cd web && npm start` serves the ng-zorro default shell.

---

## Stage 1 · Backend

The whole .NET application, written against views that do not exist yet. Three layers in dependency order.

**1a · `Relay.Domain` + `Relay.Application` — the pure core and the ports.** Detail in **§ Pure-C# core**.
```
src/Relay.Domain/OutcomePolarity.cs, TileKind.cs, TileStatus.cs, ReasonCode.cs,
                 ThresholdSet.cs, WeekRange.cs, WeekObservation.cs, TileKey.cs
src/Relay.Application/Baseline/BaselineService.cs, BaselineResult.cs, SeriesPoint.cs
src/Relay.Application/Status/StatusEvaluator.cs, StatusResult.cs
src/Relay.Application/Abstractions/IDashboardReader.cs        (port — returns WeekObservation)
src/Relay.Application/Abstractions/IAccountMetadataReader.cs  (port — locations, weeks, ThresholdSet)
src/Relay.Application/Abstractions/ReadModels.cs              (DashboardQuery, DashboardReadModel,
                                                               TileSeries, AccountMeta, DisclosureData)
src/Relay.Application/Dashboard/DashboardQueryService.cs      (orchestrator — takes the ports, not a DbContext)
src/Relay.Application/Dashboard/MetaQueryService.cs
```
Write the ladder straight off the §1 table and the windowing straight off §2 — those tables are the
specification of `StatusEvaluator` and `BaselineService`, and Stage 3 only confirms it.

**1b · `Relay.Infrastructure` — DbContext and the port implementations.**
```
src/Relay.Infrastructure/RelayDbContext.cs
src/Relay.Infrastructure/Entities/           Account, ActivityEvent, Location, EventTypeCatalog,
                                             OutcomeCatalog, AccountDashboardSettings,
                                             DataQualityExclusion, IsoWeek, WeeklyActivityFact
src/Relay.Infrastructure/Reading/EfDashboardReader.cs         (implements IDashboardReader — the single
                                                               GroupBy→Sum, then pivot to WeekObservation)
src/Relay.Infrastructure/Reading/EfAccountMetadataReader.cs   (implements IAccountMetadataReader)
```
This is the **only** project that names `DbContext`, `EntityFrameworkCore` or a table. Everything EF knows dies
at the boundary: the readers return `WeekObservation` and plain records, never entities and never `IQueryable`
— hand an `IQueryable` across and lazy evaluation drags EF back into `Application` through the type system.
`WeeklyActivityFact` and every view-backed entity get `.HasNoKey().ToView("…")`. Without `ToView`, EF treats the
view as a table and Stage 2's `Add-Migration` emits a colliding `CREATE TABLE` — which is exactly the failure
this ordering makes easy to hit, so configure it now. `weekly_activity_facts` is a **plain view**, not a
materialized one (see **Why a view, not a materialized view**); the EF mapping is identical either way, so
nothing here changes if the pushdown check later forces materialization. `WeeklyActivityFact` has no
`OutcomeKey` property — that column existed only to key a unique index that a plain view does not need.

**1c · `Relay.Api` — endpoints, DTOs, validation, composition root.** Detail in **§ API contract**.
```
src/Relay.Api/Controllers/AccountsController.cs            (dashboard + meta)
src/Relay.Api/Dtos/*.cs
src/Relay.Api/Validation/DashboardRequestValidator.cs      (400s with actionable messages)
src/Relay.Api/Program.cs                                   (DI: AddDbContext<RelayDbContext>,
                                                            AddScoped<IDashboardReader, EfDashboardReader>,
                                                            AddScoped<IAccountMetadataReader,
                                                                      EfAccountMetadataReader>,
                                                            AddSingleton(TimeProvider.System);
                                                            CORS for the ng dev server, ResponseCache,
                                                            JSON: camelCase, DateOnly as ISO, nulls kept,
                                                            ProblemDetails error handler, Swagger)
```
`Program.cs` is the one place the port meets its implementation. Nothing else in the solution mentions
`EfDashboardReader` by name.

**Prove it** — the honest checkpoint for a backend with no database behind it yet:
1. `dotnet build` clean at warnings-as-errors, and the architecture guard test passes
   (`tests/Relay.Tests.Unit/ArchitectureTests.cs` — asserts `typeof(BaselineService).Assembly`, which *is*
   `Relay.Application`, references neither `Microsoft.EntityFrameworkCore` nor `Relay.Infrastructure`). This
   test is the reason the port exists: without it `DashboardQueryService` drifts back to taking a `DbContext`
   and the unit suite quietly acquires a database dependency.
2. `dotnet run` → `/swagger` lists both endpoints with the exact DTO schemas from **§ API contract**; eyeball
   `deltaPp` nullable, `value` nullable, `series[]` present.
3. A handful of smoke assertions on `BaselineService` / `StatusEvaluator` — 3 or 4 cases, enough to prove the
   arithmetic runs. This is **not** the §1/§2 suite; that lands in Stage 3.
4. `curl 'localhost:5080/api/accounts/6/dashboard'` returns a clean, actionable error naming the missing
   relation — not an unhandled 500. That the failure is legible is itself worth proving here.
5. *Optional, and the port makes it nearly free* — register a hand-built `StubDashboardReader` behind a
   config flag and `curl` the endpoint for a real, fully shaped payload before the database exists. This is the
   one genuine repair to the weak Stage 1 checkpoint that the backend-before-migrations ordering creates: the
   whole assembly path (validation → orchestrator → baseline → status → DTO → JSON) gets exercised end to end,
   with only the SQL unproven. The same stub is the fake used by the Stage 3 orchestrator tests, so it is not
   throwaway code.

---

## Stage 2 · Migrations

Everything additive in one migration; this is where the backend from Stage 1 comes alive. Detail in
**§ Migration 001**.

**Files created**
```
src/Relay.Infrastructure/Migrations/20260820000001_InitialAdditiveSchema.cs
src/Relay.Infrastructure/Migrations/Sql/*.sql   (9 embedded resources)
src/Relay.Infrastructure/MigrationSql.cs        (loads an embedded resource by name)
src/Relay.Api/Program.cs                        (+ Development-only db.Database.Migrate())
sql/verify_migration.sql                        (the checkpoint script, hand-runnable)
```

**Prove it**
```bash
docker compose down -v && docker compose up -d   # pristine seed, no additive objects
dotnet run --project src/Relay.Api               # migration applies at startup
docker exec -i relay_takehome_postgres psql -U relay -d relay_takehome -f - < sql/verify_migration.sql
```
`verify_migration.sql` asserts, each as a row that must read `PASS`:
- `locations` = 69 rows; every `(account_id, name)` in `activity_events` has a match
- `event_type_catalog` = 3, `outcome_catalog` = 7, `account_dashboard_settings` = 20,
  `data_quality_exclusions` = 1
- `iso_weeks` = 27 rows, first `2026-01-26`, last `2026-07-27`
- `activity_events_clean` has **12** fewer rows than dedupe-only would (D4), and **805** fewer rows for
  account 6 than `activity_events_local` (D1)
- `weekly_activity_facts` is dense: `count(*) = ` locations × in-range weeks × outcome slots
- **the predicate pushes down** — see *Pushdown check* below; this is the assertion that keeps the view a view
- `SELECT * FROM check_timezone_applied` still returns the documented FAIL verdict (the audit's D2 record)

Then **run `dotnet run` a second time** and confirm zero DDL executes (EF history short-circuits) — and
separately, re-execute the embedded `.sql` files by hand and confirm every one is silent. Both halves matter.

Finally, the Stage 1 endpoints now serve real data:
```bash
curl -s 'localhost:5080/api/accounts/6/dashboard?week=2026-W30&window=8&tolerance=40' | jq
curl -s 'localhost:5080/api/accounts/20/dashboard' -o /dev/null -w '%{http_code}\n'   # 200
curl -s 'localhost:5080/api/accounts/99/meta'      -o /dev/null -w '%{http_code}\n'   # 404
```
Spot-check one tile by hand: pick `call_received` count for account 6 at 2026-W30 and reproduce
`baselineMean` with a direct psql query. If the API and psql disagree, the plan has a bug and this is where it
surfaces — before any test is written to enshrine the wrong number.

---

## Stage 3 · Backend tests

The §1–§5 suites, now that both the code and the database exist. Detail in **§ Test plan → stage mapping**.

**Files created**
```
tests/Relay.Tests.Unit/StatusEvaluatorTests.cs          (§1 — 12 theory rows, one per spec table row)
tests/Relay.Tests.Unit/BaselineServiceTests.cs          (§2 — 5 facts)
tests/Relay.Tests.Unit/WeekRangeTests.cs                (§4 pure half: DST, ISO year boundaries)
tests/Relay.Tests.Unit/ArchitectureTests.cs             (written in Stage 1; listed here for completeness)
tests/Relay.Tests.Unit/Fakes/StubDashboardReader.cs     (in-memory IDashboardReader / IAccountMetadataReader)
tests/Relay.Tests.Unit/DashboardQueryServiceTests.cs    (orchestrator against the stub — no database)
tests/Relay.Tests.Integration/SeededDatabaseFixture.cs  (ICollectionFixture, Testcontainers)
tests/Relay.Tests.Integration/DataQualityTests.cs       (§3)
tests/Relay.Tests.Integration/TimezoneBoundaryTests.cs  (§4 DB half)
tests/Relay.Tests.Integration/ApiContractTests.cs       (§5, WebApplicationFactory)
tests/Relay.Tests.Integration/FactViewPushdownTests.cs  (EXPLAIN — see Pushdown check)
tests/Relay.Tests.Integration/PayloadSnapshots/*.json
```

`DashboardQueryServiceTests` is what the port buys beyond compilability: the orchestration — threshold
resolution, per-tile densification against the spine, section assembly, the disclosure counts — becomes
unit-testable with no container at all. Account 20's empty response and the §2 window-clamp reporting are far
cheaper to assert here than through Testcontainers, and they run in milliseconds. The integration suite is then
left to test what only a real database can: the SQL.

`FactViewPushdownTests` runs the `EXPLAIN` from **Pushdown check** and fails if `activity_events` is sequentially
scanned with the account predicate applied above the join. It is the standing guard on the view-not-materialized
decision: a later change to the view definition that breaks pushdown fails a test instead of quietly getting
slower. It asserts the plan shape, never a wall-clock number — timing assertions on a laptop are flaky by
construction.

Two obligations surfaced during Stage 1 review, added here so Stage 3 doesn't have to rediscover them:
- `StatusEvaluatorTests` must assert on `reasonCode`, not just `status`, for any row where two `InsufficientData`
  conditions can coincide (a thin rate denominator dragging `WeeksContributing` to 0 alongside it) — the ladder's
  rung order determines which reason the user sees, and a status-only assertion cannot catch one reason silently
  shadowing another.
- An integration test must assert `iso_weeks` is gapless across `[firstWeek, latestWeekWithData]`, including both
  boundary weeks. `EfDashboardReader` reads its week list from `iso_weeks` and trusts it to end at the viewed
  week (`DashboardQueryService` validates the week against that same range first) — the guard in
  `EfDashboardReader` turns a gap into a diagnosed `InvalidOperationException` rather than an unexplained 500,
  but the gaplessness itself is Stage 2's `07_iso_weeks.sql` to get right and Stage 3's to prove.

The fixture boots `postgres:16`, runs `schema.sql` + `seed.sql`, applies EF migrations, and is shared across
the collection — `seed.sql` is 2.4 MB, load it once per run, never per test. **Never point at the developer's
live container**: §3 asserts 805 / 12 / 398, figures that only hold against a pristine seed.

**Prove it** — `dotnet test` green, then `TZ=Asia/Tokyo dotnet test` green with **byte-identical snapshot
files**. The snapshots being unchanged between the two runs *is* §4's first assertion. Expect this stage to
send you back into Stage 1 code at least once; that is the ordering's cost being paid.

---

## Stage 4 · Frontend

Shell, store, filters, tiles, sparkline, disclosures — the whole Angular app against the live API. Detail in
**§ Frontend**.

**Files created**
```
web/src/app/app.routes.ts, app.config.ts
web/src/app/core/api/dashboard-api.service.ts, models/*.ts   (mirrors the DTOs)
web/src/app/dashboard/dashboard-store.ts                     (signals + switchMap/debounce)
web/src/app/dashboard/dashboard-page.component.ts
web/src/app/dashboard/filters/filter-bar.component.ts
web/src/app/dashboard/filters/{location-select,window-select,tolerance-slider,week-picker}.component.ts
web/src/app/dashboard/section/event-section.component.ts
web/src/app/dashboard/tile/metric-tile.component.ts
web/src/app/dashboard/tile/status-badge.component.ts
web/src/app/dashboard/sparkline/sparkline.component.ts       (hand-rolled inline SVG)
web/src/app/dashboard/disclosures/disclosure-bar.component.ts
web/src/app/dashboard/empty-state.component.ts
```

Build the store and filter bar first with tiles rendering as raw JSON, then the visual layer — inside one stage
that split keeps the URL round-trip working before any tile exists, which is much easier to debug.

**Prove it**, manually, in this order because each catches a different class of mistake:
1. Account **6** (Metro Collision, 15 locations), week 2026-W23 — the 2026-06-03 exclusion is disclosed in the
   bar and hatched on the sparkline, and the week's baseline is visibly lower than with the exclusion removed.
2. Account **16** (Old Town Barbers, 1 location, 167 events) — mostly grey, never a wall of red.
3. Account **20** (Quiet Harbor Spa) — 200, empty state, no console errors.
4. Any account at the last data week — every count tile reads `PartialWeek` with its raw number and no colour;
   rate tiles are judged normally.
5. Change all four filters, copy the URL into a new tab — identical dashboard. Prev/next arrows disabled at
   `firstWeek` and `latestWeekWithData`.
6. Drag the tolerance slider hard — devtools shows in-flight requests cancelled, last response wins.

---

## Stage 5 · Frontend tests

The §6 suite. Karma + Jasmine (ng default) with `HttpTestingController`.

**Files created**
```
web/src/app/dashboard/dashboard-store.spec.ts                (§6 #1 URL round-trip, #4 cancellation)
web/src/app/dashboard/tile/status-badge.spec.ts              (§6 #2 accessible name)
web/src/app/dashboard/sparkline/sparkline.spec.ts            (§6 #3 partial-week badge, tooltip)
web/src/app/dashboard/section/event-section.spec.ts
```

§6 #2 and #3 assert on the **accessible name** (`toHaveAccessibleName` / `getByRole`), never on a colour class
— that is what makes the test worth having. §6 #4 asserts the last response wins after rapid emissions.

**Prove it** — `cd web && npm test` green, headless in CI mode.

---

## Stage 6 · Polish and hand-off

Response caching headers, `README.md` run instructions (**not** the logbook section — that stays untouched),
`docs/decisions.md` recording which Open Question resolutions were actually taken, and re-sync `PLAN.md` at the
repo root with this file.

**Prove it** — clone-fresh rehearsal: `git clean -xdf` in a scratch copy, `docker compose down -v`,
`docker compose up -d`, `dotnet run`, `npm start` → working dashboard, no manual SQL step anywhere.

> **If time is short.** Stages 0 → 1 → 2 are the spine and are not divisible — a backend without its migration
> is not demonstrable. From there, Stage 3's §1/§2 unit tests are the highest value per minute (they prove the
> numbers), then Stage 4 minus the sparkline polish. Stage 3's Testcontainers fixture can degrade to running
> §3/§4 against a locally recreated database, said out loud in the README. Stage 5 and 6 are the first to cut.

---

## § Migration 001 — detail

One migration, `20260820000001_InitialAdditiveSchema`. `Up()` is a sequence of `migrationBuilder.Sql()` calls
against embedded `.sql` resources, executed in this order. **The order is load-bearing**: `activity_events_clean`
reads `data_quality_exclusions` and cannot be created before the row it filters on exists, and
`weekly_activity_facts` reads `activity_events_clean`, `locations` and `iso_weeks` — a view cannot be created
before the relations it names.

Note the order is load-bearing for *creation*, not for *data*. Because `weekly_activity_facts` is a plain view
(see below), the seed rows no longer have to exist before it is created for the numbers to come out right —
they are read at query time. Getting the order wrong now fails loudly with `relation does not exist` rather
than quietly producing a correct-looking empty fact.

| # | Embedded file | What it does | Idempotency |
|---:|---|---|---|
| 1 | `01_types.sql` | `outcome_polarity` enum (`good`/`bad`/`neutral`) | `DO $$ … IF NOT EXISTS (SELECT 1 FROM pg_type …) $$` |
| 2 | `02_tables.sql` | `locations`, `event_type_catalog`, `outcome_catalog`, `account_dashboard_settings`, `data_quality_exclusions`, `iso_weeks` + PKs, FKs, `UNIQUE(account_id,name)`, `UNIQUE(event_type,code)` | `CREATE TABLE IF NOT EXISTS`, `CREATE UNIQUE INDEX IF NOT EXISTS` |
| 3 | `03_indexes.sql` | on `activity_events`: `(account_id, occurred_at)` and `(account_id, location, event_type, occurred_at)`. Never `location` alone | `CREATE INDEX IF NOT EXISTS` |
| 4 | `04_seed_catalogs.sql` | 3 event types, 7 outcomes with polarity, 20 settings rows, **the 2026-06-03 exclusion row** | `INSERT … ON CONFLICT (pk) DO UPDATE` |
| 5 | `05_locations_backfill.sql` | `INSERT INTO locations(account_id,name) SELECT DISTINCT account_id, location FROM activity_events` → 69 rows, `opened_on`/`closed_on` NULL | `ON CONFLICT (account_id,name) DO NOTHING` |
| 6 | `06_activity_events_local.sql` | verbatim from `sql/local_time.sql`, including the three plausibility-check views | `CREATE OR REPLACE VIEW` |
| 7 | `07_iso_weeks.sql` | populate the spine by `generate_series` over `date_trunc('week', min/max local_date)` read from `activity_events_local` → 27 rows | `ON CONFLICT (week_start) DO NOTHING` |
| 8 | `08_activity_events_clean.sql` | dedupe (D4) + exclusions (D1) + `location_id` join | `CREATE OR REPLACE VIEW` |
| 9 | `09_weekly_activity_facts.sql` | the dense fact **view** | `CREATE OR REPLACE VIEW` |

There is no step 10 or 11. A plain view needs no unique index and no `REFRESH`, which is the point — see
**Why a view, not a materialized view** below.

**Seed rows ship in the migration** because they are logic, not reference data. Without the exclusion row
`activity_events_clean` is a no-op and §3 fails on its first assertion (805 → 0). The exclusion row is
**account-scoped**: `(account_id=6, location NULL, event_type NULL, from_date=2026-06-03, to_date=2026-06-03,
reason='D1 · replayed bulk backfill (audit 2026-08-20)')`. Scoping matters — the day carries 882 events overall
but only 805 are the artifact; a global row would delete 77 legitimate rows from other accounts and break the
§3 assertion.

**`activity_events_clean`** (step 8), in outline:
```sql
CREATE OR REPLACE VIEW activity_events_clean AS
SELECT DISTINCT ON (l.account_id, l.location, l.event_type, l.occurred_at_utc)
       l.id, l.account_id, loc.id AS location_id, l.location, l.event_type,
       l.occurred_at_utc, l.occurred_at_local, l.local_date, l.outcome
       -- duration_seconds deliberately absent (D3, and §3's fourth assertion)
FROM   activity_events_local l
JOIN   locations loc ON loc.account_id = l.account_id AND loc.name = l.location
WHERE  NOT EXISTS (
         SELECT 1 FROM data_quality_exclusions x
         WHERE (x.account_id IS NULL OR x.account_id = l.account_id)
           AND (x.location   IS NULL OR x.location   = l.location)
           AND (x.event_type IS NULL OR x.event_type = l.event_type)
           AND l.local_date BETWEEN x.from_date AND x.to_date)
ORDER BY l.account_id, l.location, l.event_type, l.occurred_at_utc, l.id;
```
`duration_seconds` is dropped *at the view*, which is the cheapest possible way to make §3's "appears in no
response payload anywhere" true by construction rather than by vigilance.

**`weekly_activity_facts`** (step 9) is **dense**, which is the whole reason `iso_weeks` exists:
```
locations  ×  iso_weeks (clipped by opened_on/closed_on)  ×  outcome slots
```
where *outcome slots* = every `(event_type, code)` in `outcome_catalog` **plus** one `outcome IS NULL` slot per
event type, so the 398 missing-outcome rows have a home. Columns:

`account_id, location_id, week_start_local, event_type, outcome (nullable), event_count, days_included, expected_days`

- `event_count` is `0` where the left join finds nothing — that is the zero-event week the spine buys us.
- `expected_days` = 7 minus days before `opened_on` / after `closed_on` (all NULL in this seed → always 7).
- `days_included` = `expected_days` minus days matched by `data_quality_exclusions`, minus days outside the
  global data range `[2026-02-01, 2026-07-27]`, minus future days. This is what makes the first week
  (2026-01-26) and the last week (2026-07-27) read as 1-of-7 and drop out of every baseline — exactly the
  behaviour the spec wants for 2026-07-27.
- The two day columns repeat across the event-type/outcome rows of one location-week. **Never `SUM` them across
  event types.** Pooling across selected locations uses `SUM(days_included) / SUM(expected_days)`.

Size: 69 locations × ≤27 weeks × 10 slots ≈ 18.6k rows if fully enumerated.

### Why a view, not a materialized view

The spec calls for a materialized view with a unique index and a `REFRESH`. **Plan deviates: plain view.**
Two reasons, the first sufficient on its own:

- **There is no performance case.** `activity_events` is **12,626 rows** and the dense fact is **~18,600** —
  the aggregate is *bigger than the data it aggregates*. Materializing here stores a precomputed answer larger
  than the input, to avoid a scan of a table that fits in a couple of hundred kilobytes. The spec's own
  justification ("6,775 weekly rows stand in for 12,626 events, so every dashboard query scans a few hundred
  rows") holds for the *shape* of the fact, which the view keeps — it just does not require the rows to be
  stored.
- **Materializing is the sole cause of the machinery around it.** The unique index exists only so
  `REFRESH CONCURRENTLY` is possible; `REFRESH CONCURRENTLY` needs a prior non-concurrent refresh and cannot
  share a transaction with the `CREATE`; and `days_included` becomes a *stored* value that is correct only as
  of the last refresh — a completeness figure that silently goes stale is a worse failure than a slightly
  slower query, because nothing on the screen would look wrong. As a view, `days_included` is computed from
  `data_quality_exclusions` and the data range at query time and cannot be stale. Adding an exclusion row takes
  effect immediately, with no refresh step for anyone to forget.

What this removes: `10_weekly_activity_facts_index.sql`, the inline `REFRESH`, the `outcome_key`
(`COALESCE(outcome,'∅')`) column that existed only to give the unique index a non-null key, and the production
note about scheduling refreshes. What it does not change: the view's columns, its density, EF's
`.HasNoKey().ToView(…)` mapping, or any DTO.

One honest cost: the global data-range bounds (`min`/`max local_date`) are now a scalar sub-select over
`activity_events_clean` on every query. Postgres evaluates it once as an InitPlan, but it is the one part of
the query that always touches all ~11.8k cleaned rows. At this size that is single-digit milliseconds; it is
also the first thing to hoist into a tiny `data_range` table if it ever stops being.

### Pushdown check

The view only stays cheap if `WHERE account_id = 6` reaches the base tables instead of the planner building all
18.6k rows and filtering at the end. That is a planner behaviour, not a guarantee, so it gets asserted rather
than assumed — in `sql/verify_migration.sql`, and again as an integration test in Stage 3:

```sql
EXPLAIN (ANALYZE, BUFFERS)
SELECT event_type, outcome, sum(event_count)
FROM   weekly_activity_facts
WHERE  account_id = 6
  AND  week_start_local BETWEEN DATE '2026-05-25' AND DATE '2026-07-20'
GROUP  BY 1, 2;
```

**Pass** = the plan shows an *Index Scan* on `activity_events` using `(account_id, occurred_at)` — the index
migration step 3 adds — with the account predicate applied there, and total runtime in the low tens of
milliseconds. **Fail** = a `Seq Scan on activity_events` with the account filter applied only above the join,
or a plan that materialises the full cross join before filtering.

**Fallback if it fails: materialize it.** Restore `CREATE MATERIALIZED VIEW`, re-add
`10_weekly_activity_facts_index.sql` with the unique index on
`(account_id, location_id, week_start_local, event_type, outcome_key)` — reinstating `outcome_key` — and the
non-concurrent `REFRESH` as the last migration step. Nothing else in the plan moves: EF maps a materialized
view through the same `.HasNoKey().ToView(…)`, the DTOs are untouched, and `Down()` gains one drop. The cost of
being wrong here is one migration file, which is why starting with the simpler object is safe.

**Idempotency, in two layers.** Migration-level: `__EFMigrationsHistory` makes the second `dotnet run` silent.
Statement-level: every file above is independently re-runnable, which covers the case a reviewer creates
(running the SQL by hand, then letting EF run it too) and which EF history alone does not. Every view — the
clean view and the fact view alike — uses `CREATE OR REPLACE VIEW`, which is both idempotent *and*
definition-honest: re-running always installs the current SQL. (A materialized view has no `OR REPLACE` and
would have needed a `DROP … IF EXISTS` + `CREATE` dance to get the same property. One more thing the plain view
does not have to work around.) `CREATE OR REPLACE VIEW` cannot rename or reorder existing columns, so a column
change during development means `DROP VIEW … CASCADE` first — worth knowing before it surprises someone
mid-build.

**Keyless entities**: configured in Stage 1b with `.HasNoKey().ToView("…")` — the ordering makes this the one
Stage 1 decision that Stage 2 depends on.

**`Down()`** drops only additive objects, in reverse dependency order: `DROP VIEW weekly_activity_facts` →
`activity_events_clean` → the three check views → `activity_events_local` → the five tables + `iso_weeks` → the
enum type → the two indexes on `activity_events`. All four are plain `DROP VIEW IF EXISTS` now — no
`DROP MATERIALIZED VIEW`, and no index on the fact to drop, because neither exists. `accounts` and
`activity_events` keep every row.

---

## § Pure-C# core — detail

The contract that makes this testable: the database's job ends when it hands over **dense weekly observations**.
Everything after that is arithmetic on value types. That contract is not a convention — it is the
`IDashboardReader` port in **The read port** below, and the arch test in Stage 1 enforces it.

### `Relay.Domain` — no dependencies at all

```csharp
public enum OutcomePolarity { Good, Bad, Neutral }
public enum TileKind        { Count, Rate }
public enum TileStatus      { InsufficientData, PartialWeek, Breach, Warning, Normal }
public enum ReasonCode      { BaselineBelowMinEvents, BaselineZero, InsufficientHistory,
                              DenominatorBelowMin, ViewedWeekPartial, OutsideTolerance,
                              NearTolerance, WithinTolerance, GoodDirection, NeutralPolarity }

public sealed record ThresholdSet(
    int     MinBaselineEvents   = 5,      // audit: greys 51% of tiles, removes 62% of breaches
    int     MinRateDenominator  = 20,     // ±9.7pp SE at n=20
    int     MinHistoryWeeks     = 4,
    decimal MinWeekCompleteness = 6m/7m,
    decimal AmberFraction       = 0.8m,
    decimal TolerancePct        = 40m)    // audit: 25% → 42.7% red; 40% → 22.6%
{ public static ThresholdSet Defaults { get; } = new(); }

public readonly record struct WeekRange(DateOnly Start, DateOnly End)
{
    public static WeekRange FromIsoWeek(string s);   // "2026-W30", System.Globalization.ISOWeek
    public static WeekRange Containing(DateOnly d);
    public string ToIsoWeek();
    public WeekRange Previous();  public WeekRange Next();
    public IEnumerable<WeekRange> Preceding(int n);  // -n .. -1, viewed week excluded
    public string Label();                           // "Week of Mon 20 Jul – Sun 26 Jul 2026"
}

public sealed record WeekObservation(
    DateOnly WeekStart, decimal? Value, int? Denominator, int DaysIncluded, int ExpectedDays);
```

`WeekRange` is `DateOnly`-only: no `TimeZoneInfo`, no `DateTime.Now`, no `Kind`. Timezone conversion happened in
`activity_events_local`; by the time a date reaches C# it is already local. That single rule is what makes §4's
`TZ=Asia/Tokyo` assertion pass instead of being a debugging session. "Today" (for the future-day clamp and the
default week) comes from an injected `TimeProvider`, never `DateTime.Today`.

### `BaselineService` — pure

```csharp
BaselineResult Build(
    IReadOnlyList<WeekObservation> spine,   // dense, ascending, ends at the viewed week
    WeekRange viewedWeek, int requestedWindow, TileKind kind, ThresholdSet thresholds);

sealed record BaselineResult(
    decimal? Mean, decimal? BandLow, decimal? BandHigh,
    int WeeksRequested, int WeeksEffective, int WeeksContributing,
    IReadOnlyList<SeriesPoint> Series);
```

Owns, and owns exclusively:
- **Window selection** — weeks `-window .. -1`, viewed week always excluded (§2 #1).
- **Clamping** — if history is shorter than the window, shrink and report it in `WeeksEffective` (§2 #4).
- **Completeness drop** — count tiles drop weeks with `daysIncluded/expectedDays < MinWeekCompleteness`.
  It **never prorates**: scaling 3 events from 4 days to 7 invents 2.25 events.
- **Denominator drop** — rate tiles drop baseline weeks with `Denominator < MinRateDenominator`.
- **Zero vs null** — `Value = 0` contributes to the mean; `Value = null` (zero denominator) does not (§2 #2).
- **Rate baseline = mean of the weekly pooled rates**, not one pooled ratio across the window — so the baseline
  line is the average of the plotted points and does not read as a bug.
- **Band** = `Mean × (1 ∓ TolerancePct/100)`, clamped to `[0, 100]` on rate tiles. `window == 1` → `Mean` = the
  previous week's value, band `null` (§2 #5).
- **Series** — one `SeriesPoint` per calendar week over `window + 1`, each carrying `IncludedInBaseline` and an
  `ExclusionReason` of `PartialWeek | DataQualityExclusion | BelowMinDenominator | NoDenominator | null`.
  Nothing is ever omitted.

### `StatusEvaluator` — pure

```csharp
StatusResult Evaluate(
    decimal? viewedValue, int? viewedDenominator,
    int viewedDaysIncluded, int viewedExpectedDays,
    BaselineResult baseline, OutcomePolarity polarity,
    TileKind kind, ThresholdSet thresholds);

sealed record StatusResult(TileStatus Status, ReasonCode Reason, decimal? DeltaPct, decimal? DeltaPp);
```

Owns **only** the five-rung ladder, evaluated in order, first match wins — `InsufficientData` (baseline mean
`< MinBaselineEvents`, or `= 0`, or fewer than `MinHistoryWeeks` contributing weeks, or rate denominator
`< MinRateDenominator`) → `PartialWeek` (count tiles only) → `Breach` (`abs(dev) >= tolerance`, bad side,
inclusive) → `Warning` (`abs(dev) >= 0.8 × tolerance`, bad side) → `Normal`. `DeltaPct` is what the ladder
judges on, for both tile kinds; `DeltaPp` is populated on rate tiles only and is display-only.

**Why no `DbContext` in either.** They take value objects and return value objects, so the §1 and §2 tables
become `[Theory]` data with no fixture, no container, and no I/O — the rules most likely to put a wrong number
on a customer's screen get the fastest and most exhaustive tests in the suite. It also forbids, structurally,
the drift the spec warns about: there is exactly one implementation of the ladder and Angular cannot re-derive
it because the API hands it the answer.

### The read port — where EF stops

`Application` must orchestrate a database read without referencing a database. It does that through two ports
it owns and `Infrastructure` implements:

```csharp
// Relay.Domain — the shared vocabulary both sides of the port speak
public sealed record TileKey(string EventType, string? Outcome, TileKind Kind);

// Relay.Application/Abstractions — everything below
public sealed record TileSeries(TileKey Key, IReadOnlyList<WeekObservation> Observations);

public sealed record DashboardQuery(
    int AccountId, IReadOnlyList<int> LocationIds, WeekRange ViewedWeek, int Window);

public sealed record DashboardReadModel(
    AccountInfo Account,
    IReadOnlyList<LocationInfo> Locations,
    IReadOnlyList<TileSeries> Tiles,       // dense: window+1 observations per tile, spine-aligned
    DisclosureData Disclosures);           // nullOutcomeCount, exclusions overlapping the window

public interface IDashboardReader
{
    Task<DashboardReadModel?> ReadAsync(DashboardQuery query, CancellationToken ct);   // null = unknown account
}

public interface IAccountMetadataReader
{
    Task<AccountMeta?> ReadAsync(int accountId, WeekRange? week, CancellationToken ct);
    // AccountMeta: name, IANA timezone, locations[], firstWeek, latestWeekWithData,
    //              latestCompleteWeek, maxWindowForWeek, ThresholdSet
}
```

**`WeekObservation` is the boundary type**, which is why it lives in `Domain` alongside `WeekRange`: it is
simultaneously what the reader returns and what `BaselineService` consumes, so no mapping layer sits between
them. The rule the interface encodes is the one already stated above — *the database's job ends when it hands
over dense weekly observations.* Densification against the `iso_weeks` spine happens on the SQL side of the
port, so `Application` never has to detect and repair a sparse result.

Two constraints on implementations, both learned failure modes:
- **Never return `IQueryable`.** It looks convenient and it re-exports EF's evaluation model — and with it EF —
  through `Application`'s public surface. Return materialised lists.
- **Never return entities.** `WeeklyActivityFact` is an Infrastructure type; leaking it would put the view's
  column layout into the orchestrator and couple the DTOs to the schema.

`DashboardQueryService` (Application, takes `IDashboardReader` + `IAccountMetadataReader` + `TimeProvider`) is
the seam: it resolves thresholds (settings row ← query-param overrides), asks the reader for the dense
observations, calls the two pure services per tile, and assembles DTOs. It has no `DbContext`, no
`using Microsoft.EntityFrameworkCore`, and — per the Stage 1 arch test — no way to acquire one.

Cost of the port, recorded plainly: two interfaces and a handful of record types that a direct `DbContext`
dependency would not need, and one extra indirection when tracing a query end to end. Bought with it: an
EF-free unit suite that can actually be enforced, an orchestrator testable without Docker, and a Stage 1
checkpoint that can serve a real payload before the schema exists.

---

## § API contract — detail

### `GET /api/accounts/{id}/dashboard?locations=&week=&window=&tolerance=`

All four filters optional; each falls back to `account_dashboard_settings`. `locations` is a comma-separated
list of **location names** (as in the spec's example `Site+A,Site+C`) resolved against `locations` for that
account — names are only unique within an account, and resolving them account-scoped is the same discipline
the index rule enforces.

```jsonc
{
  "accountId": 6, "accountName": "Metro Collision Centers", "timezone": "America/New_York",
  "timezoneNote": "All figures in America/New_York (account timezone).",
  "week": { "isoWeek": "2026-W30", "start": "2026-07-20", "end": "2026-07-26",
            "label": "Week of Mon 20 Jul – Sun 26 Jul 2026",
            "hasPrevious": true, "hasNext": true },
  "window": { "requested": 8, "effective": 8 },
  "tolerancePct": 40,
  "locations": [ { "id": 12, "name": "Site A", "selected": true } ],
  "sections": [
    { "eventType": "call_received", "displayName": "Calls received", "sortOrder": 1,
      "countTile": { … }, "rateTiles": [ { … }, { … }, { … } ] }
  ],
  "disclosures": {
    "nullOutcomeCount": 27,
    "exclusions": [ { "fromDate": "2026-06-03", "toDate": "2026-06-03",
                      "reason": "D1 · replayed bulk backfill", "weeksAffected": ["2026-06-01"] } ]
  }
}
```

**Tile** — the spec's field list exactly, plus `baselineWeeksUsed` (see Open Question 2):

| Field | Notes |
|---|---|
| `key`, `label`, `kind` | `kind` ∈ `count` \| `rate` |
| `value` | **nullable** — a rate week with a zero denominator is `null`; a count is never null |
| `deltaPct` | nullable; what the status is judged on, both kinds |
| `deltaPp` | **nullable, rate tiles only** — `null` on every count tile, always |
| `baselineMean`, `bandLow`, `bandHigh` | tile level, never per point. Rate bands clamp at 100 on display. Null when `window == 1` |
| `denominator` | rate tiles only, the viewed week's |
| `status`, `reasonCode` | server-computed |
| `baselineWeeksUsed` | how many weeks actually contributed (§2 #3 requires reporting it) |
| `series[]` | `window + 1` points, dense over the `iso_weeks` spine |

**SeriesPoint** — `weekStart`, `value` (nullable), `denominator` (rate only), `daysIncluded`, `expectedDays`,
`includedInBaseline`, `exclusionReason`, `isViewedWeek`. The two look-alike cases stay distinct: a zero-event
week is `value: 0, includedInBaseline: true`; a zero-denominator week is `value: null, includedInBaseline:
false, exclusionReason: "NoDenominator"`.

### `GET /api/accounts/{id}/meta?week=`

```jsonc
{ "locations": [ { "id": 12, "name": "Site A", "openedOn": null, "closedOn": null } ],
  "firstWeek": "2026-W05", "latestWeekWithData": "2026-W31", "latestCompleteWeek": "2026-W30",
  "maxWindowForWeek": 24,
  "defaults": { "week": "2026-W30", "window": 8, "tolerancePct": 40,
                "minBaselineEvents": 5, "minRateDenominator": 20, "minHistoryWeeks": 4,
                "minWeekCompleteness": 0.857, "amberFraction": 0.8 } }
```

`maxWindowForWeek` is computed for the supplied `week` (defaulting to `defaults.week`) and shrinks as `week`
moves backwards — §5's first assertion. Account 20 returns `locations: []`, null weeks, `maxWindowForWeek: 0`.

**Errors** — unknown account → 404. `window < 1` or `> maxWindowForWeek`, `tolerance` outside `[1, 100]`,
unparseable `week`, `week` outside `[firstWeek, latestWeekWithData]`, or an unknown location name → 400 with a
message naming the parameter, the offending value and the permitted range.

---

## § Frontend — detail

### Component tree

```
DashboardPageComponent            smart; injects DashboardStore; owns nothing but layout
├─ FilterBarComponent
│  ├─ LocationSelectComponent     nz-select [nzMode]="multiple", options from /meta
│  ├─ WindowSelectComponent       nz-input-number, max = meta.maxWindowForWeek
│  ├─ ToleranceSliderComponent    nz-slider, debounced
│  └─ WeekPickerComponent         ◀ label ▶ ; arrows disabled at firstWeek / latestWeekWithData
├─ DisclosureBarComponent         tz note · exclusion list · "398 events have no recorded outcome"
├─ EventSectionComponent (×3, ordered by event_type_catalog.sort_order)
│  ├─ MetricTileComponent  kind=count   (top-left of the section)
│  └─ MetricTileComponent  kind=rate ×n (left → right, ordered by outcome_catalog.sort_order)
│     ├─ StatusBadgeComponent
│     └─ SparklineComponent
└─ EmptyStateComponent            account 20, or a selection with no locations
```

All standalone. Section and outcome labels/order come from the catalogs in the payload — no hardcoded
`'call_received'` in Angular.

### `DashboardStore` (signal-based)

```ts
readonly filters = signal<Filters>({ account, locations, week, window, tolerance });  // mirrored from route
private readonly response = toSignal(
  toObservable(this.filters).pipe(
    debounceTime(250),
    distinctUntilChanged(deepEqual),
    switchMap(f => this.api.dashboard(f).pipe(catchError(…)))));
readonly sections = computed(() => this.response()?.sections ?? []);
readonly loading  = signal(false);
readonly error    = signal<ApiError | null>(null);
```

Route is the single source of truth: the store reads `ActivatedRoute.queryParams`, and every filter change is a
`router.navigate([], { queryParams, queryParamsHandling: 'merge', replaceUrl: true })`. `replaceUrl` on the
tolerance drag keeps the back button meaningful — one entry per deliberate change, not one per pixel.
`switchMap` (not `mergeMap`) is what makes "last response wins" true (§6 #4).

### Sparkline — hand-rolled inline SVG

ng-zorro ships no charts, and ~9 points with a shaded band is not worth a chart library. One `<svg
viewBox="0 0 240 56" role="img" [attr.aria-label]="summary()">` per tile containing, back to front:

1. `<defs><pattern id="hatch">` — diagonal hatch, so the band reads without colour.
2. Band `<rect>` from `bandHigh` to `bandLow` (tint + hatch); omitted entirely when `window === 1`.
3. Baseline `<line stroke-dasharray="2 2">` at `baselineMean`.
4. Value `<polyline>`; `value === null` breaks the line rather than interpolating across it.
5. Per point a `<circle>` — dimmed at low `denominator`, dashed stroke when `!includedInBaseline`, ringed on
   `isViewedWeek` — each wrapping a `<title>` giving the tooltip: `"4 of 7 days · excluded from baseline"`.
6. Hatched `<rect>` segment over any week with a `DataQualityExclusion`.

Scales are computed in the component from `series`, `bandLow`, `bandHigh` — no d3.

### Status rendering — never colour alone

```html
<span class="status" [class]="'status--' + status" [attr.aria-label]="label">
  <span class="status__icon" aria-hidden="true">{{ icon }}</span>
  <span class="status__label">{{ label }}</span>
</span>
```
`InsufficientData ● "Not enough data"` · `PartialWeek ◐ "Partial week"` · `Breach ▲/▼ "Outside tolerance"` ·
`Warning ◆ "Near tolerance"` · `Normal ✓ "Normal"`. Icon and text always render; colour is the third channel.
The palette is checked against deuteranopia rather than taken from ant design's defaults, and the `▲`/`▼` for
`Breach` follows the direction of the deviation. Grey tiles still show their number, their `%` divergence and
their band — *shown but not judged*.

---

## § Test plan → stage mapping

| Spec § | Lands in | Kind | Notes |
|---|---|---|---|
| **§1** Status evaluation (12 rows) | **Stage 3** | Unit, no DB | `StatusEvaluatorTests`, one named case per table row. Written *after* the evaluator under this ordering — build the evaluator from the table as a checklist so this is confirmation |
| **§2** Baseline construction (5) | **Stage 3** | Unit, no DB | `BaselineServiceTests` |
| **§3** Data quality (805 / 12 / 398 / no `duration_seconds`) | **Stage 3** | Integration, throwaway seeded DB | 805 asserted both as a row count *and* as a measurable drop in the following weeks' baseline. Partly pre-verified by Stage 2's `verify_migration.sql` |
| **§4** Timezone & week boundaries | **Stage 3** | split: DST + ISO arithmetic → unit (`WeekRangeTests`); LA date shift + `TZ=Asia/Tokyo` byte-identical output → integration | the `TZ` run is a CI matrix axis, not a test |
| **§5** API contract (accounts 6/16/20, 404/400, snapshot) | **Stage 3** | Integration, `WebApplicationFactory` | snapshots committed under `PayloadSnapshots/`; the same cases are smoke-checked by hand at the end of Stage 2 |
| **§6** Frontend (URL round-trip, accessible name, badges/tooltips, cancellation) | **Stage 5** | Component/unit, Karma + Jasmine with `HttpTestingController` | assert accessible names, never colour classes |
| *Deliberately not tested* | — | — | SVG pixels (snapshot the series, not the markup), EF's own aggregation, coverage as a gate |

Three checks live outside their §: the architecture guard test and the pure-core smoke assertions are written in
**Stage 1**, because they are the only way that stage can prove anything at all; and `DashboardQueryServiceTests`
covers orchestration, which no spec § names but which every § depends on being right.

---

## Open questions / conflicts

Places where the spec is genuinely ambiguous or two parts pull against each other. Recommended resolution and
reasoning for each — none of these are silently picked. All are unaffected by the stage reordering.

**1 · Default viewed week vs. the real clock.** The spec says the default view week is "previous week (current
week is never available)". The wall clock is 2026-08-20; the data ends 2026-07-27. Literally applied, the
default lands on an empty week. Separately, `2026-07-27` is a 1-of-7-day week and would render every count tile
as `PartialWeek` on first load.
→ **Recommend** `defaults.week = latestCompleteWeek` (2026-07-20). `latestWeekWithData` stays reachable via the
next arrow and is the boundary at which the arrow disables. "Previous week" remains the rule; it is simply
clamped to the data. Both values ship in `/meta` so the choice is visible, not buried.

**2 · §2 requires reporting contributing weeks; the API field list does not include it.** §2 #3 and #4 say the
response reports how many weeks contributed and that a clamp "says so", but the field list is `value, deltaPct,
deltaPp, baselineMean, bandLow, bandHigh, status, reasonCode, series[]`.
→ **Recommend** adding `baselineWeeksUsed` to the tile and `window: { requested, effective }` to the response.
This is the only addition to the spec's field list, and §2 cannot be satisfied without it. Deriving it client-
side from `series[].includedInBaseline` is possible but re-derives business logic in Angular, which §"Status is
computed server-side" exists to prevent.

**3 · `/meta` takes no parameters but returns a week-dependent value.** The signature is
`GET /api/accounts/{id}/meta` while §5 asserts `maxWindowForWeek` shrinks as `week` moves backwards.
→ **Recommend** `GET /api/accounts/{id}/meta?week=2026-W30`, defaulting to `defaults.week`. The alternative —
returning a per-week map — is bulkier and the UI already knows which week it is showing.

**4 · `window=1` "returns no band", but the band is computable.** The band here is the *tolerance* band
(`mean × (1 ∓ tol)`), which is well-defined for a single-week baseline; the "no variance in one week" reasoning
belongs to a dispersion band, which is not what this design uses.
→ **Recommend** honour §2 literally: `bandLow`/`bandHigh` are `null` at `window=1` and the sparkline draws no
band. Status is still evaluated from `deltaPct`. The rationale to record is honesty rather than arithmetic — a
band drawn around one observation invites the reader to treat it as a range of typical values, which is exactly
what a single week cannot tell them.

**5 · Band definition conflicts with the data audit.** `docs/relay-data-audit.md` §05 recommends a
quasi-Poisson band `μ ± 2√(φμ)` with a per-account dispersion factor (φ runs 0.37–2.29). `RequirementsFinal.md`
uses a flat relative tolerance band — confirmed arithmetically by its own worked example (`completed` at 82.3%
baseline → `bandHigh` 115% = 82.3 × 1.4).
→ **Recommend** the tolerance band, per the authoritative spec: it is the thing the customer's own
`tolerance_pct` control moves, and a band the customer cannot move is a band they cannot reason about. Record
the dispersion band as the next iteration — it is the statistically better answer and would reduce the false
alarms `min_baseline_events` is currently absorbing.

**6 · Fact-table grain vs. the day columns.** `days_included`/`expected_days` are specified at
`(account_id, location_id, week_start_local)` but the fact grain is `(…, event_type, outcome)`.
→ **Recommend** denormalising them onto every fact row (repeated within a location-week) rather than splitting a
second view. It keeps the single-fact-view story and one join fewer per query. The cost is a real trap — summing them
across event types gives 3× the truth — so it is guarded by a naming convention (`days_included` is only ever
read via `MAX`/`DISTINCT` per location-week) and an integration test asserting a known week's completeness.
Multi-location completeness pools as `SUM(days_included) / SUM(expected_days)`.

**7 · "Days outside the data range" — whose range?** `days_included` subtracts days outside the data range;
the spec does not say whether that range is global or per account.
→ **Recommend** **global** (`min`/`max` `local_date` over `activity_events_clean` → 2026-02-01 … 2026-07-27),
computed **live in the view** on each query. A per-account range would mark a quiet account's genuinely low
early weeks as "partial", which is the opposite of the truth. Consequence to accept openly: the first week
(2026-01-26) is 1-of-7 for every account and drops from every baseline, exactly as the last week does.

Computing it live is the better half of the view-vs-materialized-view decision. Snapshotting the range into a
materialized fact would freeze it at refresh time: load one more day of events, forget to refresh, and every
week silently keeps the old completeness figures while the counts appear to move — a wrong `days_included` is
invisible on screen, unlike a wrong count. The price is a scalar sub-select per query, evaluated once as an
InitPlan over ~11.8k rows. If that ever matters it becomes a one-row `data_range` table refreshed on ingest,
which is a smaller and more honest thing to keep current than the whole fact.

**8 · Materialized view vs. plain view, and sparse vs. dense.** Two decisions in one place, because the second
is what settles the first. The spec asks for a **materialized** `weekly_activity_facts` with a unique index and
a `REFRESH`, and cites "6,775 weekly rows" (a sparse count — I measure 6,790 on the cleaned data, the small
difference being dedupe tie-breaking). It also requires dense series and zero-event weeks, which is why
`iso_weeks` exists — and dense is ≈18.6k rows, not 6.8k.

→ **Recommend dense, and a plain view** — a deliberate deviation from the spec's "materialized", detailed under
**Why a view, not a materialized view**. Densify in SQL rather than in C#: the spine is then applied once, in
one place, and the EF aggregation cannot return a sparse result that C# has to detect and repair.

Once dense, materializing stops making sense. `activity_events` is 12,626 rows and the dense fact is ~18,600 —
the aggregate is larger than the data it aggregates, so there is no read to accelerate, only a bigger object to
keep in sync. And every piece of ceremony the spec attaches to this object — the unique index, `REFRESH`,
`REFRESH CONCURRENTLY` and its ordering constraints, `outcome_key`, the staleness window on `days_included` —
exists *because* it is materialized. Dropping the materialization drops all of it at once. The spec's stated
row count changes; the reasoning it was supporting (a small weekly fact standing in for the event stream, a
few hundred rows scanned per dashboard) is exactly what the view delivers.

The one thing a materialized view genuinely buys is a guaranteed physical scan cost regardless of what the
planner does with the view definition — which is why this is a *checked* deviation rather than an assumed one.
The **Pushdown check** is the check, `EXPLAIN`-based and asserted in `verify_migration.sql` and in Stage 3; if
it fails, materializing is the documented fallback and costs one migration file.

**9 · Exclusion row scope.** 2026-06-03 carries 882 events; only 805 are the account-6 backfill.
→ **Recommend** an **account-scoped** row (`account_id = 6`). A global row would silently delete 77 legitimate
rows from other accounts and would fail §3's 805 assertion. The wildcard columns exist precisely so scope is
data, not code.

**10 · Tables via `CreateTable` vs. `Sql("CREATE TABLE IF NOT EXISTS")`.** The spec notes tables are the one
thing the model differ writes on its own; it also demands idempotency throughout.
→ **Recommend** generating the DDL with `Add-Migration` first (to get the types and constraints right), then
moving it into `02_tables.sql` as `IF NOT EXISTS` so that `Up()` is uniformly `Sql()` and the whole migration
survives being run against a partially built database. Trade-off recorded: the EF model snapshot must be kept
honest by the `DbContext` configuration rather than by the differ, and future migrations are hand-written.
Given this is one migration against a fixed seed, that is a cheap price for "the second run is silent" being
true unconditionally.

**11 · The "location panel".** §"Status is computed server-side" mentions "tile, sparkline, location panel" as
the three places a rule would otherwise be re-implemented, but no location panel appears in the layout, the API
contract, or the test plan — and the audit's principal finding is that ranking locations by outcome rate
surfaces the unlucky site, not the struggling one.
→ **Recommend** **no location panel**. Locations stay a filter. If one is wanted later it must rank by
significance (distance outside a confidence interval), never by rate, per audit §05 #5 — which in this dataset
would flag 1 of 69 sites and probably that one by chance.

**12 · Account 20 — "empty sections".** Ambiguous between `sections: []` and three sections containing no tiles.
→ **Recommend** `sections: []` plus an explicit empty state in the UI ("No locations are reporting for this
account"). Three sections of nothing implies the data is missing rather than that the account has no sites.

**13 · Unstated defaults.** `min_history_weeks` has no stated default (§1 tests it at 4); `tolerance` has no
stated valid range.
→ **Recommend** `min_history_weeks = 4` and `tolerance ∈ [1, 100]`, both in `account_dashboard_settings` /
validation and both surfaced in `/meta.defaults` so the choice is inspectable rather than folklore.

---

## Verification — end to end

Per-stage checkpoints are above. The full gate, run at the end:

```bash
# 1 · pristine database, migration applies from cold          (Stage 2)
docker compose down -v && docker compose up -d
dotnet run --project src/Relay.Api

# 2 · migration facts                                          (Stage 2)
docker exec -i relay_takehome_postgres psql -U relay -d relay_takehome < sql/verify_migration.sql

# 3 · the tests that would catch a wrong number on a screen    (Stage 3)
dotnet test                                  # §1 §2 unit · §3 §4 §5 integration (Testcontainers)
TZ=Asia/Tokyo dotnet test                    # §4 — byte-identical snapshots

# 4 · frontend                                                 (Stages 4–5)
cd web && npm test
npm start                                    # http://localhost:4200
```

Manual pass, in this order, because each one catches a different class of mistake:
1. Account **6** (Metro Collision, 15 locations), week 2026-W23 — the 2026-06-03 exclusion is disclosed in the
   bar and hatched on the sparkline, and the week's baseline is visibly lower than with the exclusion removed.
2. Account **16** (Old Town Barbers, 1 location, 167 events) — mostly grey, never a wall of red.
3. Account **20** (Quiet Harbor Spa) — 200, empty state, no console errors.
4. Any account at the last data week — every count tile reads `PartialWeek` with its raw number and no colour;
   rate tiles are judged normally.
5. Change all four filters, copy the URL into a new tab — identical dashboard.
6. Drag the tolerance slider hard — no flicker, no out-of-order render, last response wins.
