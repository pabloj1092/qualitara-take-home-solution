#### Background:

- take a look fo @docs/relay-data-audit.md thats the base data that I have
- Each account can have multiple locations
- Weeks start on Monday and finish on Sunday

#### What I want:

- Using that data I need to create a dashboard to show the data per account

#### Data to show:

- Count of event types and its percentage divergence from the baseline mean as a secondary number.
- Rate of outcome per event type and its percentage divergence from the baseline mean as a secondary number.

#### How to show it:
- At the top show some filters:
  - multiselect per location: can show the data for all the locations or only some of the locations
  - comparison window: number of weeks to compare. 1 is compare against last week. Max number is the max of weeks available on the data for that account. The comparison window mixed with the view week defines the chunk of data to take. use the timezone from the account to define the boundaries for the week
    - `window = 1` means the baseline **is** the previous week — a single value, not a mean. There is no variance in one week, so no band can be drawn and the comparison is fragile: weekly volatility runs to a variance-to-mean ratio of 2.29 for the largest account. Allowed, but the default is 8.
  - %tolerance: % of divergence from the mean to show the data as an issue (red). 
  - view week: by default to previous week (current week is never available) but allow the user to go back in time and see metrics from previous weeks
- Label the viewed week explicitly — `Week of Mon 20 Jul – Sun 26 Jul 2026`, never "this week". Prev/next arrows to move between weeks, disabled at the data boundary. Disclose the timezone the boundaries were computed in: *"All figures in America/New_York (account timezone)."*
- Each event type will be a section of the dashboard
- Inside the event type section show the metrics for the last week or whatever is configure on the time window, one metric per event type count and one per outcome type rate.
- show the event type count to the top left of the section and the outcome type rate metrics across the screen from left to right
- Below each number show a graph with a time window of the comparison used to calculate the baseline
- Each outcome will categorized as good or bad outcome:
  - if is a bad outcome and is over the tolerance window it will show on red
  - if is a good outcome and is under the tolerance window it will show on red
  - if an outcome is very close to the red line (last 20% of the defined tolerance) it will show on orange
  - Otherwise will show on green

#### Status ladder

Evaluated in order, first match wins. `status` is computed server-side and returned alongside the numbers.

| # | Status | Condition | Colour | Icon | Label |
|---|---|---|---|---|---|
| 1 | `InsufficientData` | baseline mean `< min_baseline_events`, or baseline mean = 0, or fewer than `min_history_weeks` complete weeks before the viewed week. Rate tiles: denominator `< min_rate_denominator` | grey | ● | "Not enough data" |
| 2 | `PartialWeek` | viewed week `days_included < expected_days` — **count tiles only** | grey | ◐ | "Partial week" |
| 3 | `Breach` | deviation on the bad side and `abs(dev) >= tolerance_pct` | red | ▲ / ▼ | "Outside tolerance" |
| 4 | `Warning` | deviation on the bad side and `abs(dev) >= amber_fraction × tolerance_pct` (default 0.8) | orange | ◆ | "Near tolerance" |
| 5 | `Normal` | everything else, including any movement in the good direction | green | ✓ | "Normal" |

- **`InsufficientData` is never red.** It outranks every other rule. This is the single biggest false-alarm killer in the whole design.
- Grey means *shown but not judged* — the tile still displays its number and its `%` divergence. A mostly-grey dashboard still carries every figure; it just declines to make a claim about them.
- "Bad side" is polarity-dependent: above baseline for a bad outcome, below baseline for a good one. Neutral outcomes (`voicemail`, `open`) can only ever reach 1, 2 or 5.
- **Never colour alone** — WCAG 1.4.1. Every status renders its icon *and* its text label next to the colour; the colour is the third channel, not the only one. Check the red/green pair against deuteranopia, ant design's defaults are borderline. The sparkline tolerance band carries a fill pattern as well as a tint.

##### Defining `min_baseline_events`

Measured on the cleaned seed — 8-week baseline, 25% tolerance, all locations, per account × event type:

| Baseline mean | Tiles | Breaching 25% |
|---|---:|---:|
| < 3 | 303 | **64.7%** |
| 3 – 5 | 221 | **67.9%** |
| 5 – 8 | 197 | **55.8%** |
| 8 – 10 | 38 | 36.8% |
| 10 – 20 | 177 | 37.9% |
| 20 + | 84 | 25.0% |

With no gate, **54.7% of all tiles breach in an average week**. A dashboard that is more than half red is noise wearing the costume of signal.

**Default `min_baseline_events = 5`** — a deliberate lean toward showing data rather than withholding it. It greys 51% of tiles and removes **62% of the breaches**, which is the two worst buckets, where nearly two in three tiles breach on sampling luck alone.

A gate of 8 is the better statistical answer — it removes 82% of breaches — but it costs too much of the dashboard to be the right starting point:

| Gate | Tiles shown | `call_received` | `lead_created` | `appointment_set` | Red at 40% tolerance |
|---:|---:|---:|---:|---:|---:|
| **5** | **48.6%** | 85.3% | 43.8% | 16.8% | 22.6% |
| 8 | 29.3% | 65.0% | 17.9% | 5.0% | 16.7% |

At 8, two of the three event-type sections are effectively empty — `lead_created` visible on 17.9% of account-weeks, `appointment_set` on 5.0%. At 5 they reach 43.8% and 16.8%: the difference between a section that works and a section that is grey by default. The price is a red rate of 22.6% rather than 16.7%.

**Revisit once there is real traffic.** This is a product call, not a statistical one, and it lives in `account_dashboard_settings.min_baseline_events` precisely so it can move without a deploy. Recorded plainly rather than buried: at a baseline mean of 5 a single extra event is a 20% move, so tiles in the 5–8 band are showing a colour we can only half-defend.

Two further consequences to accept openly rather than discover later:

- **`appointment_set` stays mostly grey even at 5** — 16.8% of its account-weeks clear the gate. That is the honest reading of ~1 event per location per day, not a defect to engineer around.
- **25% is too tight as a default tolerance even above the gate.** At `mean >= 5`: 25% → 42.7% of shown tiles red, **40% → 22.6%**, 50% → 14.5%, 60% → 7.9%. **Default `tolerance_pct = 40`**, which lands nearest the 4–10% alert rate the data audit identified as actionable. Customers can tighten it; the default must not cry wolf.
- **Rate tiles gate on the denominator**, not the mean: `min_rate_denominator`, default **20**. A rate on n=10 carries a ±13.7pp standard error, on n=20 ±9.7pp, on n=30 ±7.9pp. Below 20 the point estimate moves more than the tolerance does.

##### Percentage points, not percentages, on rate tiles

**The tolerance stays relative** — one `tolerance_pct` governing count and rate tiles alike. A fixed percentage-point tolerance was rejected: base rates in the cleaned seed run from **12.4%** (`voicemail`) to **82.3%** (`completed`), so a 10pp tolerance is a 12% relative move on `completed` and an **81%** one on `voicemail` — one control carrying three incompatible meanings depending on which tile the customer happens to be looking at. `min_rate_denominator = 20` is calibrated against a relative band: the ±9.7pp standard error at n = 20 sits just inside the ±10.3pp half-width of a 40% band on the 25.7% `missed` base rate. That calibration holds only while the band is relative.

What a tile **displays** is a separate question from what it is **judged on**:

- Count tiles render `deltaPct`. Rate tiles render `deltaPp`, the change in percentage points. Both are judged on `deltaPct`.
- Reason: a relative percentage printed beneath a percentage value is read as the new value. "22.4% missed · +25%" invites exactly that; "22.4% missed · +4.5pp" does not.
- The tile also shows the band as an absolute range — "typical 15–36%" — since `bandLow` / `bandHigh` are already in the payload, and an absolute range is the one form that cannot be misread.
- **Rate bands clamp at 100% on display.** `completed` at an 82.3% baseline yields a `bandHigh` of 115%. It never affects a verdict, because good outcomes are judged on the downside, but it must not reach the screen.

##### Rates pool across the selected locations

A rate over a multi-location selection is **the sum of numerators over the sum of denominators** — never the mean of per-location rates. Three reasons, each sufficient alone:

- Mean-of-rates weights every site equally, which up-weights the smallest and noisiest. Median per-location weekly volume is 6 events, and the audit found location-level rates statistically indistinguishable from noise (r = 0.06 half-over-half, χ² p ≈ 0.19). Equal weighting hands the loudest voice to the least reliable sites.
- The count tile directly above it in the same section is a pooled total. On any other basis the rate describes a different population than the number immediately above it.
- At these volumes mean-of-rates lets one added location move the headline in the opposite direction to every constituent site.

**The rate baseline is the mean of the weekly pooled rates over the window**, not a single pooled ratio across the whole window. Pooling across the window has lower variance and is the better estimator; the sparkline plots weekly rates, and a baseline line that is not the average of the plotted points reads as a bug. Legibility wins over a variance gain that is small at these volumes.

`min_rate_denominator` applies to baseline weeks as well as the viewed week. Weeks below it drop out and the effective window shrinks — the pattern **Week completeness** already establishes for count tiles — and if fewer than `min_history_weeks` survive, the tile is `InsufficientData`.

#### Data-quality
- All dashboard reads go through a cleaned view (dedupe + exclusions applied). Excluded periods are disclosed in the UI, never applied silently.
- IF an account is empty it should show empty, not crash
- Definitions
| Event type | Outcome | Polarity | Note |
|---|---|---|---|
| `call_received` | `connected` | good | |
| `call_received` | `missed` | bad | |
| `call_received` | `voicemail` | **neutral** | Worse than connected, better than missed — coloring it either way is a claim we can't defend |
| `lead_created` | `converted` | good | |
| `lead_created` | `open` | **neutral** | Pending, not an outcome |
| `appointment_set` | `completed` | good | |
| `appointment_set` | `no_show` | bad | |
| *(any)* | `NULL` — 3.2%, 398 rows | excluded | Missing-at-random per the data audit; exclude from rate denominators, disclose as a footnote count |


#### Week completeness

`weekly_activity_facts` carries `days_included` and `expected_days` at `(account_id, location_id, week_start_local)` grain. `expected_days` is 7, reduced by days the location was not yet open or already closed (`locations.opened_on` / `closed_on`). `days_included` is that figure minus days removed by `data_quality_exclusions`, minus days outside the data range or still in the future.

- **Count tiles** — `BaselineService` **drops** any week with `days_included / expected_days < min_week_completeness` (default `6/7 ≈ 0.857`) from the baseline.
- **It does not prorate.** Scaling a count of 3 from 4 days up to 7 invents 2.25 events that never happened, and it amplifies noise precisely where the counts are smallest. Dropping is the lossless choice; the effective window simply shrinks and the response reports how many weeks actually contributed.
- **Viewed week incomplete** → `PartialWeek`: the raw count is shown, with no comparison and no colour. This is what protects the last week in the dataset (2026-07-27 — a Monday, 1 of 7 days) from reading as a total collapse.
- **Rate tiles are unaffected** — numerator and denominator lose the same days, so the ratio survives. One caveat worth knowing: that holds only while the missing days are a representative day-of-week mix. In this dataset weekend missed-call rate is 28.3% against 25.4% on weekdays, so a week missing its weekend shifts the rate ~3pp. Well inside the tolerance, but it is not exactly zero.
- **Always disclosed.** A dropped or flagged week is marked on the sparkline — dashed point, hatched band segment — with a tooltip reading "4 of 7 days · excluded from baseline". Never silently omitted, or the chart lies by omission.

#### Architecture

- .NET 8+ (C#) on the backend
  - EF Core as the ORM - favor aggregations to generate the metrics
- Angular on the frontend
  - Use Router query params to select the account and locations
  - ant design for the ui
- Postgres DB - use the one we already have
  - Build activity_events_clean on top: dedupe (D4) + apply exclusions (D1) driven by a table, not a hardcoded <> DATE '2026-06-03'
  - Materialize weekly_activity_facts at grain (account_id, location_id, week_start_local, event_type, outcome) → event_count
  - EF Core does the flat aggregation (GroupBy → Count, fully translatable) against the weekly fact. A pure C# BaselineService / StatusEvaluator does the windowing, band and verdict — no DbContext dependency, so the rules that matter most get unit tests with no database.


Relay.Api            controllers, DTOs, response caching
Relay.Application    DashboardQueryService → orchestrates
                     BaselineService       → pure, unit-tested
                     StatusEvaluator       → pure, unit-tested
Relay.Domain         OutcomePolarity, ThresholdSet, WeekRange
Relay.Infrastructure DbContext, keyless entities on views, migrations

##### API contract

- One endpoint, one round trip. The whole dashboard comes from a single call — never one request per tile.

```
GET /api/accounts/{id}/dashboard
      ?locations=Site+A,Site+C&week=2026-W30&window=8&tolerance=25
```

  Returns sections → event type count tile → outcome rate tiles, each carrying
  `value, deltaPct, deltaPp, baselineMean, bandLow, bandHigh, status, reasonCode, series[]`.
  `deltaPp` is nullable and populated on rate tiles only — see **Percentage points, not percentages, on rate tiles**.
  Keeps Angular dumb and makes the whole response snapshot-testable.

- `series[]` carries `window + 1` points — the baseline weeks plus the viewed week — dense over the `iso_weeks` spine. Nothing is ever omitted; a week with no data is a point, not a gap.

| Field | Notes |
|---|---|
| `weekStart` | local Monday, ISO date |
| `value` | the unit the tile displays — a count, or a rate as a percentage. **Nullable**: a rate week with a zero denominator is `null` |
| `denominator` | rate tiles only. Drives the "3 of 12" tooltip and lets low-n points render dimmed |
| `daysIncluded`, `expectedDays` | see **Week completeness** |
| `includedInBaseline` | whether this point contributed to `baselineMean` |
| `exclusionReason` | `null`, or one of `PartialWeek`, `DataQualityExclusion`, `BelowMinDenominator`, `NoDenominator` |
| `isViewedWeek` | the point the tile's number is taken from |

Two distinctions worth stating, because they look alike and are not:

- A zero-event week is `value: 0`, `includedInBaseline: true` — that is what the `iso_weeks` spine exists for. It is **not** the zero-denominator case, which is `value: null` and excluded.
- `bandLow` / `bandHigh` stay at tile level, never per point. The band is flat across the window, and repeating it per point implies it varies.

- A metadata endpoint, because the UI cannot render the filters until it knows what is available:

```
GET /api/accounts/{id}/meta
      → locations[], firstWeek, latestWeekWithData, maxWindowForWeek, defaults{}
```

##### Status is computed server-side

- `status` and `reasonCode` are business logic, not presentation. The API returns them alongside the raw numbers so Angular renders them but never re-derives them.
- Otherwise the same rule gets implemented three times — tile, sparkline, location panel — and they drift apart.

##### Frontend

- **All** filters live in query params, not just account and locations. A fully shareable URL is a real support win ("send me the link") and makes the back button work.
- Signal-based `DashboardStore`: route params → HTTP → render. Standalone components.
- ant design (ng-zorro) ships no charts. For ~9-point sparklines with a shaded band, hand-rolled inline SVG beats pulling in a chart library — fewer bytes, exact control over the band rendering, no theming fight.
- `switchMap` + debounce on filter changes; the %tolerance control will fire constantly and in-flight requests must be cancelled.

##### Seed data constraint

- The data audit recommends a `UNIQUE` constraint on `activity_events (account_id, location, event_type, occurred_at)`. **It cannot be applied** — the 12 existing duplicate pairs would fail it, and the seed data is to be left as-is. Recorded as the forward ingestion fix; dedupe happens in `activity_events_clean` instead.
- Indexes are non-destructive, so those are applied (see schema below).


##### Migrations

`docker-compose.yml` mounts only `schema.sql` and `seed.sql` into `docker-entrypoint-initdb.d`, which Postgres runs once, on an empty volume. Every object in **Database schema to add** is therefore missing from a fresh clone, and `sql/local_time.sql` is today a hand-run step — `activity_events_local` exists on one machine, not in the repo's definition of the database.

- **EF Core migrations, but `Up()` is almost entirely `migrationBuilder.Sql()`** against embedded `.sql` files. The model differ generates none of what matters here: the two views, the materialized view, its unique index, the `polarity` enum type, the `locations` backfill, the `iso_weeks` spine, the two indexes on `activity_events`, or a single seed row. Tables are the only part it writes on its own.
- **Seed rows ship in the migration, because they are logic, not reference data** — `event_type_catalog`, `outcome_catalog`, `account_dashboard_settings`, and the 2026-06-03 row in `data_quality_exclusions`. Without that last row `activity_events_clean` is a no-op and Test plan §3 fails on its first assertion: 805 rows removed for Metro Collision Centers becomes 0.
- **Order is load-bearing**, because `activity_events_clean` reads `data_quality_exclusions` and cannot be created before the rows it filters on exist: tables → seed rows → `locations` backfill from the 69 distinct `(account_id, location)` pairs → `activity_events_clean` → `weekly_activity_facts` → unique index → initial `REFRESH`.
- **`sql/local_time.sql` folds into migration 001** and stops being a manual step; its three plausibility-check views come with it. The file stays as the written record of the D2 reasoning, but the migration is what creates the objects.
- **Idempotent throughout** — `CREATE OR REPLACE VIEW`, `IF NOT EXISTS`, upserts on seed rows. A reviewer runs setup more than once, and the second run has to be silent.
- **Applied at startup in Development** via `db.Database.Migrate()`, so `docker compose up -d && dotnet run` is the entire setup.
- **Keyless entities need `.HasNoKey().ToView(...)`.** Without `ToView`, EF treats `weekly_activity_facts` as a table and the next `Add-Migration` emits a `CREATE TABLE` that collides with the hand-written view.
- **The materialized view refreshes once, inside the migration.** The data is static, so nothing schedules a refresh. `REFRESH CONCURRENTLY` needs the unique index plus one prior non-concurrent refresh and cannot share a transaction with the `CREATE` — recorded as the production concern, not exercised here.
- **`Down()` drops only the additive objects.** `accounts` and `activity_events` keep every row; the two indexes drop with the rest.
- **Integration tests migrate a throwaway database per run.** Test plan §3 and §5 assert on 805 rows, 12 duplicate pairs and 398 NULL outcomes — figures that hold only against a pristine seed with migrations applied. Pointed at a developer's live container they stop testing the code and start recording that container's history.


#### Database schema to add

All additive. `accounts` and `activity_events` take no schema or data changes; two indexes are added.

| Object | Shape | Why |
|---|---|---|
| `locations` | `(id, account_id FK, name, opened_on NULL, closed_on NULL, created_at)`, `UNIQUE(account_id, name)` | Backfilled from the 69 distinct `(account_id, location)` pairs. Without open/close dates you cannot distinguish "location closed" from "integration broke" — the difference between a correct alert and a false one |
| `event_type_catalog` | `(code PK, display_name, sort_order)` | Drives section order and labels; removes hardcoded `'call_received'` strings from Angular |
| `outcome_catalog` | `(event_type, code, display_name, polarity ENUM('good','bad','neutral'), sort_order)`, `UNIQUE(event_type, code)` | Holds the polarity table from **Data-quality** above. Makes `voicemail`'s polarity a customer setting rather than an argument in code |
| `account_dashboard_settings` | `(account_id PK FK, default_comparison_weeks, tolerance_pct, min_baseline_events, min_rate_denominator, min_history_weeks, min_week_completeness, amber_fraction, updated_at)` | Lets the customer own the definition of "normal". Supplies the defaults that the URL params override. The `min_*` fields drive the grey `InsufficientData` and `PartialWeek` states — see **Status ladder** |
| `data_quality_exclusions` | `(id, account_id NULL, location NULL, event_type NULL, from_date, to_date, reason, created_at)` | Turns the hardcoded `<> DATE '2026-06-03'` into auditable data, feeds `activity_events_clean`, and gives the UI something to disclose |
| Views | `activity_events_local` (migration 001, from `sql/local_time.sql`) → `activity_events_clean` → **materialized** `weekly_activity_facts`, with a unique index so it can `REFRESH CONCURRENTLY`. The fact carries `days_included` and `expected_days` alongside `event_count` | The read path. 6,775 weekly rows stand in for 12,626 events, so every dashboard query scans a few hundred rows. The two day columns are what let the baseline drop incomplete weeks instead of quietly averaging them in |
| `iso_weeks` | `(week_start, week_end, iso_year, iso_week)` week spine | Not cosmetic. Without a dense spine, zero-event weeks vanish from the result set — the sparkline skips them and an 8-week window quietly becomes a 6-week window, biased upward because the missing weeks were the quiet ones |
| Indexes | on `activity_events`: `(account_id, occurred_at)` and `(account_id, location, event_type, occurred_at)` | Never index `location` alone — it is not a global key ("Site A" belongs to 19 accounts), so any index or join on it in isolation silently blends unrelated businesses |


#### Test plan

Tests that would catch a wrong number on a customer's screen. Not coverage-driven — if a test cannot fail for a reason a user would notice, it is not worth writing.

##### 1 · Status evaluation — pure unit tests, no database

Table-driven against `StatusEvaluator`. Every row is a real failure mode:

| Case | Expected |
|---|---|
| baseline mean 4.9, `min_baseline_events` 5 | `InsufficientData` — not a colour |
| baseline mean 0, viewed week 5 | `InsufficientData` — never `+∞%` |
| deviation exactly `= tolerance_pct` | `Breach` (boundary inclusive) |
| deviation exactly `= 0.8 × tolerance_pct` | `Warning` |
| deviation a hair under `0.8 × tolerance_pct` | `Normal` |
| bad outcome **60% below** baseline | `Normal` — good-direction movement never warns |
| good outcome 60% below baseline | `Breach` |
| `voicemail` / `open` at +200% | `Normal` — neutral polarity can never breach |
| 3 weeks of history, `min_history_weeks` 4 | `InsufficientData` |
| viewed week 4 of 7 days, **count** tile | `PartialWeek` |
| viewed week 4 of 7 days, **rate** tile | evaluated normally |
| rate denominator 19, `min_rate_denominator` 20 | `InsufficientData` |

##### 2 · Baseline construction

- A window of 8 ending at the viewed week uses weeks `-8..-1` and **excludes the viewed week**. Construct a case where including it flips a `Breach` to `Normal`.
- A zero-event week is included as `0`, not skipped — build the case where skipping it raises the mean enough to flip `Normal` → `Breach`. This is the `iso_weeks` spine earning its place.
- A week below `min_week_completeness` is dropped and the effective window shrinks; the response reports how many weeks actually contributed.
- Viewed week 3 of an account's history with `window=8` clamps to 2 and says so.
- `window=1` uses the previous week's value as the baseline and returns no band.

##### 3 · Data quality — integration, against the seeded database

- The 2026-06-03 exclusion removes **805 rows** for Metro Collision Centers, and that account's baseline for the following weeks is measurably lower with the exclusion than without. Assert both halves — the row count *and* the effect on a tile.
- Dedupe collapses exactly the **12 duplicate pairs**.
- NULL outcomes (**398 rows**) are excluded from rate denominators but still counted in the event-type total. Assert the two deliberately do not reconcile, and that the footnote count explains the gap.
- `duration_seconds` appears in no response payload anywhere (D3 — uniform noise).

##### 4 · Timezone and week boundaries

- Boundaries come from the account's IANA zone, never the server's — run the whole suite under `TZ=Asia/Tokyo` and assert byte-identical output.
- An event at 2026-03-15 23:30 UTC for an `America/Los_Angeles` account falls on the previous local day, and in the previous week when that day is a Sunday.
- The 2026-03-08 DST transition does not produce a 6-day or 8-day week.

##### 5 · API contract

- `/meta` returns a `maxWindowForWeek` that shrinks as `week` moves backwards.
- **Account 20 (Quiet Harbor Spa, 0 events, 0 locations)** → 200 with empty sections. Not 404, not 500.
- **Account 16 (Old Town Barbers, 1 location, 167 events)** → mostly `InsufficientData`, never a wall of red.
- **Account 6 (Metro Collision, 15 locations)** exercises the exclusion path and the largest location set in one request.
- Unknown account → 404. `window` or `tolerance` out of range → 400 with a message a developer can act on.
- The same query string twice → identical payload (snapshot test).

##### 6 · Frontend

- Every filter round-trips through the URL: change all four, reload, get the identical dashboard.
- Each status renders its icon **and** its text label — assert on the accessible name, which is what makes this test worth having rather than a colour-class check.
- The partial-week badge and the "excluded from baseline" tooltip appear on the affected sparkline point.
- Rapid `%tolerance` changes cancel in-flight requests and the last response wins.

##### Deliberately not tested

- Chart pixel rendering — snapshot the series data, not the SVG.
- EF Core's own aggregation. Test the SQL our views produce, not the ORM's.
- Coverage percentage as a merge gate.
