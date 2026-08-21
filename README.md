# Relay — Account Activity Dashboard

Relay is a fictional B2B SaaS for service businesses (calls, leads, appointments across one or
more locations). This repo answers a support-team ask — *"is this number normal for us?"* — with
a dashboard API that compares each account's current week against its own recent history, rather
than a raw-totals view with no baseline.

## Status

| Layer | State |
|---|---|
| Database schema & migrations | Done — additive migration, seed-derived catalogs, views |
| Backend (`Relay.Domain` / `Application` / `Infrastructure` / `Api`) | Done — full `/dashboard` and `/meta` endpoints, 63 tests passing |
| Frontend (`web/`, Angular 21 + ng-zorro) | Done — dashboard, filters (location/window/tolerance/week, URL-persisted), sparkline, empty states, 23 tests passing |
| Data audit & requirements | Done — see [docs/relay-data-audit.md](docs/relay-data-audit.md), [RequirementsFinal.md](RequirementsFinal.md) |

The task was built backend-first: understand the data → write requirements → plan the schema and
API → implement and test the backend → build and verify the Angular UI against the running API
(`PLAN.md` Stages 0–5). Deferred: Stage 6 polish — see [What I'd do with another
day](#what-id-do-with-another-day) below.

## Local database

A Postgres 16 database runs locally in Docker, seeded from
[Qualitara/tv-analytics-takehome](https://github.com/Qualitara/tv-analytics-takehome)
(`schema.sql` + `seed.sql`, copied into this repo).

| | |
|---|---|
| Container | `relay_takehome_postgres` |
| Host | `localhost` |
| Port | `5432` |
| Database | `relay_takehome` |
| User | `relay` |
| Password | `relay` |
| Connection string | `postgresql://relay:relay@localhost:5432/relay_takehome` |

Defined in [docker-compose.yml](docker-compose.yml).

```bash
docker compose up -d      # start
docker compose down       # stop
docker exec -it relay_takehome_postgres psql -U relay -d relay_takehome   # connect via psql
```

Tables: `accounts` (20 rows), `activity_events` (12,626 rows). Schema in
[schema.sql](schema.sql). Treat the seed data as-is — don't regenerate, extend, or replace it (per
the source repo's README). Everything the app needs on top of it — locations, catalogs, views,
the weekly fact — is added additively by the app's own migration; `accounts` and `activity_events`
are never altered.

## Quick start

**Database**

```bash
docker compose up -d
```

**Backend**

```bash
dotnet restore
dotnet ef database update --project src/Relay.Infrastructure --startup-project src/Relay.Api
dotnet run --project src/Relay.Api
```

The API applies pending migrations automatically on startup in `Development`
([Program.cs](src/Relay.Api/Program.cs)), so the explicit `dotnet ef database update` above is a
belt-and-suspenders step, not a strict requirement. Swagger UI is at `/swagger` in Development.
Try it:

```bash
curl "http://localhost:5000/api/accounts/6/dashboard?week=2026-W29&window=8&tolerance=40"
```

**Tests**

```bash
dotnet test tests/Relay.Tests.Unit          # pure C#, no I/O, no database
dotnet test tests/Relay.Tests.Integration   # spins up its own Postgres via Testcontainers
```

**Frontend**

```bash
cd web && npm install && npm start
```

Serves at `http://localhost:4200`, defaulting to `?account=6`. Dashboard state (locations, window,
tolerance, week) lives in the URL query string, so a reload or a pasted link reproduces the exact
same view.

## Architecture

```
Relay.Api            ASP.NET Core controllers, DTOs, request validation, JSON shaping,
                      global exception → ProblemDetails mapping
      ↓
Relay.Application     DashboardQueryService / MetaQueryService — orchestration only
                      BaselineService  — pure: builds the N-week baseline from a series
                      StatusEvaluator  — pure: the 5-rung status ladder, one rule, no I/O
      ↓
Relay.Domain          OutcomePolarity, ThresholdSet, TileKind/Key/Status, WeekRange
                      Zero dependencies — not even on Relay.Application
      ↓
Relay.Infrastructure  EF Core DbContext, keyless entities mapped onto SQL views,
                      hand-written idempotent migrations
      ↓
PostgreSQL 16         activity_events_local → activity_events_clean → weekly_activity_facts
```

The split that matters most: **`BaselineService` and `StatusEvaluator` have no database
dependency.** The rule most likely to put a wrong colour on a customer's screen — the status
ladder — gets pure, exhaustive unit tests instead of being buried in a LINQ query or a component.
An [`ArchitectureTests`](tests/Relay.Tests.Unit/ArchitectureTests.cs) test asserts `Relay.Domain`
takes no dependency on anything above it, so this boundary can't silently erode.

EF Core is used for what it's good at — flat `GroupBy`/`Count` against the pre-aggregated fact
view. The statistics that need window functions (`AVG() OVER (... ROWS BETWEEN 8 PRECEDING AND 1
PRECEDING)`, `VAR_SAMP`, `DISTINCT ON`) live in hand-written SQL views instead of being forced
through LINQ.

### Data flow

`activity_events` (raw, untouched — see [Local database](#local-database)) is never queried
directly by the app. Four SQL views sit in front of it, each solving one problem found during the
[data audit](docs/relay-data-audit.md):

1. **`activity_events_local`** — adds account-local timestamp fields (`occurred_at` is UTC only in
   the source schema).
2. **`activity_events_clean`** — dedupes (`DISTINCT ON`) and applies `data_quality_exclusions`
   (removes the 805-row backfill spike on 2026-06-03 for account 6, which would otherwise poison
   that account's baseline for months). `duration_seconds` is dropped here, deliberately — it's
   pure noise per the audit, and this makes "never shipped as a metric" true by construction
   rather than by vigilance elsewhere.
3. **`weekly_activity_facts`** — a *dense* fact over `locations × iso_weeks × outcome slots`
   (`(account_id, location_id, week_start_local, event_type, outcome) → event_count`), so a
   zero-event week is a real row, not a gap the reader has to detect. Deliberately a plain view,
   not materialized — see PLAN.md's "Why a view, not a materialized view" section for the full
   reasoning and the `EXPLAIN`-based pushdown check that guards it
   ([verify_migration.sql](sql/verify_migration.sql),
   [FactViewPushdownTests](tests/Relay.Tests.Integration/FactViewPushdownTests.cs)).
4. **`iso_weeks`** — a gapless Monday-start week spine, generated once from the global data range.
   Without it, zero-event weeks vanish from a `GROUP BY` and a baseline silently shrinks.

## Key architectural decisions

The full reasoning for each of these lives in PLAN.md's "Open questions / conflicts" section and,
where noted, in the fix commits that followed implementation. This table is the index; treat
PLAN.md as the source of truth for *why*.

| # | Decision | Why |
|---|---|---|
| 1 | Default viewed week = latest **complete** week, not "previous calendar week" | The wall clock (2026-08-20) is a month past the data. Literal "previous week" lands on an empty week and a 1-day partial week both flag every tile red on first load. |
| 2 | Added `baselineWeeksUsed` + `window: {requested, effective}` to the API contract | The spec requires disclosing a clamp; the original field list had nowhere to put it. |
| 3 | `GET /meta?week=` takes a week parameter | `maxWindowForWeek` genuinely depends on which week is being viewed — it shrinks walking backwards through an account's history. |
| 4 | `window=1` draws no tolerance band | A band drawn around a single observation implies a range of typical values that one week cannot support. Status still evaluates from `deltaPct`. |
| 5 | Flat relative-tolerance band, not a Poisson dispersion band | The tolerance band is the thing the customer's own slider moves. A statistically superior band the customer can't reason about or adjust is a worse product decision even though it's a better model — recorded as the next iteration. |
| 6 | `days_included`/`expected_days` denormalized onto every fact row per location-week | Avoids a second view and an extra join; guarded by a naming convention (only ever read via `MAX`/`DISTINCT`, never summed across a location's own outcome rows) plus an integration test. |
| 7 | Data-completeness range is **global**, computed live in the view | Per-account ranges would mislabel a quiet account's genuinely low early weeks as "partial". Computing it live (not snapshotting into a materialized fact) means a late-arriving day of events can never leave stale completeness numbers next to fresh counts. |
| 8 | `weekly_activity_facts` is dense and a **plain view**, deviating from the spec's "materialized" | Densifying (required for zero-event weeks) already makes the fact *larger* than the raw event table it aggregates — there's no read left to accelerate, only an object to keep in sync. Checked, not assumed: guarded by the pushdown test; materializing is the documented fallback if that ever fails. |
| 9 | The 2026-06-03 backfill exclusion is **account-scoped** (`account_id = 6`), not global | 882 events landed that day; only 805 are the backfill. A global exclusion would silently delete 77 legitimate rows belonging to other accounts. |
| 10 | Migration DDL generated once via `dotnet ef migrations add`, then hand-moved into `IF NOT EXISTS` SQL files | Makes the whole migration safely re-runnable (by EF's history table *and* by hand) against a partially-applied database. Trade-off: future migrations are hand-written, not differ-generated. |
| 11 | No "location ranking" panel | The audit's headline finding is that per-location outcome *rates* don't persist half-over-half (r = 0.057) — a rank-by-rate panel would surface the location that got unlucky, not the one that's struggling. Locations stay a filter only. |
| 12 | Account with zero locations returns `sections: []`, not three empty sections | Empty sections read as "data is missing"; an empty array plus an explicit UI empty state reads as "this account has no sites". |
| 13 | `min_history_weeks = 4` default, `tolerance ∈ [1, 100]` | Both unstated in the source spec; both surfaced in `/meta.defaults` so the choice is inspectable rather than folklore. |
| 14 | Count-tile "bad direction" = a **drop** (`OutcomePolarity.Good`) | Count tiles have no `outcome_catalog.polarity` of their own, but the status ladder needs one. On a monitoring dashboard, "calls stopped coming in" is the actionable direction; a spike is not. |
| 15 | `min_baseline_events` gate set to **5**, not the originally-computed 8 | Measured trade-off, not a guess: gate 8 leaves two of three dashboard sections (`lead_created`, `appointment_set`) grey on 82–95% of account-weeks — the product looks broken. Gate 5 grosses up visible tiles from 29% to 49% at the cost of a higher red rate (16.7% → 22.6%). Revisitable; both figures are in [ThresholdSet.cs](src/Relay.Domain/ThresholdSet.cs). |
| 16 | Rate-tile `InsufficientData` checks the viewed week's own denominator *before* baseline history | Low volume drags the viewed week's denominator and the baseline candidate weeks below threshold together (the common case, not the rare one) — checking history first was reporting the less specific, less actionable reason (fix commit `0d044a8`). |
| 17 | Default week resolves via `requestedWeek ?? LatestCompleteWeek ?? LatestWeekWithData` | `LatestCompleteWeek` is genuinely nullable (an account whose locations never all report a full week at once has none); the orchestrator now mirrors the reader's own fallback instead of force-unwrapping. |
| 18 | Out-of-range `window` **clamps** to `Math.Max(1, maxWindowForWeek)` instead of 400ing | The earliest viewable week has `maxWindowForWeek = 0`, which the default `window=8` can never satisfy — that's a real, viewable week, not a malformed request. Only `window < 1` still validates as an error. |

### Status ladder

Five rungs, evaluated in order, first match wins ([StatusEvaluator.cs](src/Relay.Application/Status/StatusEvaluator.cs)):

1. **InsufficientData** — thin history, thin rate denominator, or a baseline mean below
   `MinBaselineEvents`. Never red — a small number is a data problem, not a bad-week verdict.
2. **PartialWeek** — count tiles only; a rate's numerator and denominator lose the same days, so
   rates survive an incomplete week.
3. **Breach** — `|deltaPct| ≥ TolerancePct`, on the bad side of the outcome's polarity.
4. **Warning** — `|deltaPct| ≥ 0.8 × TolerancePct`, bad side only. Movement in the good direction
   never goes amber.
5. **Normal** — everything else, including all movement on the good side and every neutral-polarity
   outcome (`voicemail`, `open` — "worse than X, better than Y" isn't a claim either colour can
   defend).

Defaults ([ThresholdSet.cs](src/Relay.Domain/ThresholdSet.cs)): `MinBaselineEvents = 5`,
`MinRateDenominator = 20`, `MinHistoryWeeks = 4`, `TolerancePct = 40`, `AmberFraction = 0.8`. Each
is a measured trade-off against the seeded data, not an arbitrary choice — see decision #15 above
and the comments in the file.

## API reference

```
GET /api/accounts/{id}/dashboard?locations=Site+A,Site+C&week=2026-W30&window=8&tolerance=40
GET /api/accounts/{id}/meta?week=2026-W30
GET /health
```

`/dashboard` returns one payload per load — account info, the resolved week/window (with clamp
disclosure), locations, a section per `event_type` (a volume tile plus its rate tiles), and
data-quality disclosures (excluded-period ranges, null-outcome count). `status` and `reasonCode`
are computed server-side and shipped alongside the raw numbers — Angular is expected to render,
never re-derive, the verdict. Full DTO shapes: [DashboardResponseDto.cs](src/Relay.Api/Dtos/DashboardResponseDto.cs),
[MetaResponseDto.cs](src/Relay.Api/Dtos/MetaResponseDto.cs). Full contract detail: PLAN.md's
"§ API contract — detail" section.

`/meta` returns what the UI needs *before* it can render filters — locations, first/latest week
with data, the max comparison window for the requested week, and every tunable default
(`account_dashboard_settings`), so a client never has to hardcode a threshold.

Validation errors return `400` as `ProblemDetails` with a `parameter` extension naming the bad
field; unhandled exceptions return `500` (message included only in Development). See
[Program.cs](src/Relay.Api/Program.cs) and
[DashboardRequestValidator.cs](src/Relay.Api/Validation/DashboardRequestValidator.cs).

## Testing

86 tests total, functionality-first (see [RequirementsFinal.md § Test
plan](RequirementsFinal.md#test-plan) for the backend rationale):

| Project | Focus | Notable coverage |
|---|---|---|
| [Relay.Tests.Unit](tests/Relay.Tests.Unit) (42) | Pure C#, no I/O | `StatusEvaluatorTests` — all 5 rungs, both tolerance boundaries exactly; `BaselineServiceTests`; `DashboardQueryServiceTests` against stub ports; `WeekRangeTests` — ISO week math, DST; `ArchitectureTests` — `Relay.Domain` has zero outgoing dependencies |
| [Relay.Tests.Integration](tests/Relay.Tests.Integration) (21) | Real Postgres via Testcontainers | `DataQualityTests` — the 805-row exclusion asserted on row count *and* its effect on a tile; `TimezoneBoundaryTests` — whole suite re-run under a non-UTC `TZ`; `FactViewPushdownTests` — `EXPLAIN`-based proof the account filter reaches the index through the view stack; `ApiContractTests` — full HTTP round trip via `WebApplicationFactory` |
| [web/src/app](web/src/app) (23, Vitest) | Component/store, `HttpTestingController` | `dashboard-store.spec` — URL ↔ state round-trip; `event-section.spec`, `sparkline.spec`, `status-badge.spec` — rendering by status/reason code |

Run the frontend suite with:

```bash
cd web && npm test
```

Integration tests provision their own database (Testcontainers) — they don't touch the
`docker-compose` instance, so `dotnet test` works with or without `docker compose up` already
running.

Deliberately not tested: SVG pixel-level output, EF Core's own aggregation correctness, and
coverage percentage as a gate.

## Project structure

```
src/
  Relay.Domain/          value objects, enums — zero dependencies
  Relay.Application/     BaselineService, StatusEvaluator (pure); *QueryService (orchestration)
  Relay.Infrastructure/  DbContext, entities, migrations (Sql/*.sql applied via migrationBuilder.Sql)
  Relay.Api/             controllers, DTOs, validation, Program.cs
tests/
  Relay.Tests.Unit/
  Relay.Tests.Integration/
web/                      Angular 21 + ng-zorro — dashboard UI (see web/src/app/dashboard, /core)
sql/                      verify_migration.sql (pushdown check), local_time.sql (audit-time exploration)
docs/                     relay-data-audit.{md,html} — the data-quality and statistics report
schema.sql, seed.sql      upstream, unmodified — see Local database above
```

## What I deferred, and why

* **PLAN.md's Stage 6 (polish/hand-off).** Response caching headers, a `docs/decisions.md`
  write-up, and a final re-sync pass of PLAN.md against what actually got built. None of these
  change behavior — cutting them kept the budget on the parts that affect correctness or the
  product read of the dashboard.
* **A location-ranking panel.** Considered and explicitly rejected, not merely postponed — see
  decision #11 above. The audit found per-location outcome *rates* don't persist half-over-half
  (r = 0.057), so ranking by rate would surface the location that got unlucky this week, not the
  one that's actually struggling. Locations stay a filter, not a leaderboard.
* **Weekday/weekend-split baselines.** Flagged during the data audit (weekend volume is visibly
  lower) as a real refinement, but folding it in would have meant a second baseline dimension and
  more edge cases (a location with almost no weekend volume at all) than the time budget allowed
  to test properly. The flat trailing-N-week baseline was the one I could actually verify end to
  end.
* **Root-causing the timestamp/timezone anomaly.** The audit surfaced that `occurred_at` looks
  like local time stored as UTC (activity clusters outside business hours once "converted"). I
  deliberately did not infer or auto-correct this in the pipeline — it reads as a possible
  upstream data-capture bug, and silently reinterpreting timestamps on a hunch is exactly the kind
  of "confident but wrong" move this exercise is checking for. No time-of-day metric is shipped
  because of it.
* **Auth, alerting/ML, and infra** — out of scope per the ticket; not attempted.

## What I'd do with another day

1. Finish Stage 6: response caching headers on `/dashboard` and `/meta` (both are safe to cache
   for a viewed week that isn't the current one), and re-sync PLAN.md's stage list against the
   final implementation.
2. Prototype the weekday/weekend-split baseline behind the existing tolerance model, and check
   whether it actually changes verdicts on the seeded accounts often enough to justify the added
   complexity — right now that's a hypothesis from the audit, not a measured result.
3. Add a location-level "needs attention" view that doesn't rely on the rejected rate-ranking
   idea — e.g. surfacing locations currently in `Breach`/`Warning` on any tile, which is a direct
   readout of data already computed server-side rather than a new derived ranking.
4. Tighten the `min_baseline_events` gate (currently 5, see decision #15) with a couple more weeks
   of hypothetical data to see whether the visible-tiles/red-rate trade-off moves in a nicer
   direction — it's currently a measured compromise, not a settled number.

## Tools & models

Claude Code throughout. Opus 5 for planning and design work (the data audit, requirements,
PLAN.md, and the open architectural questions); Sonnet for implementation (backend, frontend,
tests, fix commits). I reviewed and directed both — see [AI_LOG.md](AI_LOG.md) and
[web/AI_LOG.md](web/AI_LOG.md) for the raw session logs, including the moments I caught it wrong
(e.g. a floating-point baseline bug, a mis-ordered status rung, a stale status-ladder branch).

## Further reading

Documents produced during this project, in the order they were written:

1. [docs/relay-data-audit.md](docs/relay-data-audit.md) — the initial data analysis: schema,
   data-quality defects, and the statistical case for what "normal" can and can't mean on this
   dataset.
2. [RequirementsDraft.md](RequirementsDraft.md) → [RequirementsFinal.md](RequirementsFinal.md) —
   product requirements, refined from the audit's findings.
3. [PLAN.md](PLAN.md) — the staged implementation plan: schema design, API contract, and every
   ambiguity resolved with reasoning before code was written.
4. The Logbook below — running notes and the numbered implementation-time decisions, kept exactly
   as written during the exercise.

---

## tLogbook

In this section I'm going to add my thoughts about this exercise, some decisions and the why

Right now I have some time constrains so I'll try to aim more for the 4 hours time window rather than the 6 hours. I'm planning of doing in chunks during the day (1 or 2 hour chunks, we'll see). For time reasons I'll assume any question I may have and document the reasoning here.

At first glance the exercise look bit ambiguous, is not clear what info is on the dashboard on how is presented to the customer. Also the presented ticket assumes that we already have a base dashboard but we don't so that adds to the creativity part of the solver. I wont going to do the "original" dashboard and then implement the ticket over it, I'll go straight to the "good" dashboard.

The ticket raise me some questions about whats the baseline to compare and whats does "normal" means. Since we are SaaS and every customer can have a different opinion about it I would assume that the best approach is to let the customer take that decision by itself, maybe add some dashboard config so he can setup time window and % divergence

I'll create an stophook to auto generate the AI log after every answer from claude. For now, besides that I dont see a benefit of adding new skills or agents

So before doing anything, I'll have a session with claude to analyze the data. I'm sure that understanding the data will give me all the insights I need to understand how to craft an usable solution for this. For that I'll create a DB and restore the seed

Insights form the session

- volume on weekends is noticeable lower that weekdays
- There are spikes that can impact the baseline
- Look like something is wrong with the timestamps since they are not in business hours. In a normal day to day we should run something to fix the data issue and work on a solution to avoid keep gathering wrong data. Looks like local time was stored as UTC but thats something dangerous to infer to for now I'm not going to generate metrics based on hours
- I'm assuming that the events are fixed or is always to be a short list

volume on weekends is noticeable lower that weekdays -> since the mean is ˜1 per day I would like to separate weekdays and weekends for the baseline comparisons

There are spikes that can impact the baseline -> we should clean that data to avoid affect the baseline, thats a business decision and I'll make the call on deleting data that goes way over the mean (P95)

Now Im going to do a planning session to define what do I want to show in the dashboard and the overall architecture of the solution

I created a requirements draft and asked claude to review it

In a real world scenario we may have separate dbs per client or any other SaaS multi tenant strategy but for the purpose of this exercise I'll leave it as it is

Some decisions I took like EF Core, ant design, Postgres DB, they work nice and fit perfect for this purpose. I choose to use an ORM because is more maintainable, easy to use an scalable than using plain SQL. Same with Postgres, its free, works nice with IANA time zones

I told claude to review my requirements draft and give me recommendations. Took some of them and modify others. ALso the schema defined from claude looks ok.

With a final requirement I created a plan.md

Before implementing the plan I downloaded some skills to make sure to follow best practices for C#, Angular and Postgres

I ran al the different steps of the plan. Had time issues with it. I'm used to work with better models that needs less back and forth. 

From the architectural point of view is a very simple project. Very straight forward so I didn't have to fight much with the agent.

Due to time constraints I'll leave the UI as it is. a next step will be to add a ranking of locations from good to bad in a side panel so the manager has a better view of how each location is doing.

Timewise I had to dive

## Implementation decisions

Calls made during the build that PLAN.md's 13 "Open questions / conflicts" didn't cover — same
format, continuing the numbering.

**14 · Count-tile polarity.** Count tiles have no outcome and therefore no
`outcome_catalog.polarity` of their own — polarity is only defined per `(event_type, outcome)`.
Yet `tolerance_pct` explicitly "govern[s] count and rate tiles alike"
(RequirementsFinal.md §"Percentage points, not percentages, on rate tiles"), and the status
ladder's Breach/Warning rules say "deviation on the bad side" without restricting themselves to
rate tiles. Something has to decide which direction is "bad" for a raw count.
→ **Decided** `OutcomePolarity.Good` — a volume drop is the actionable direction on a monitoring
dashboard ("calls stopped coming in"), a spike is not. Implemented in
`src/Relay.Infrastructure/Reading/EfDashboardReader.cs`. Documented here, not just in the code
comment, because it's a real product judgment call in the same family as the 13 above, not an
implementation detail.
