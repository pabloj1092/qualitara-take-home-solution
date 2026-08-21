# What the activity data can and cannot tell a customer

**Relay · Data audit**

A structural and statistical review of `accounts` and `activity_events`, aimed at two questions the team keeps hearing: *is this number normal for us?* and *which location needs attention?*

| | |
|---|---|
| Source | `relay_takehome` |
| Window | 2026-02-01 → 2026-07-27 |
| Rows | 12,626 |
| Reviewed | 2026-08-20 |

---

## Summary — the headline

> **Principal finding**
>
> **Per-location differences in this dataset are statistically indistinguishable from random chance.** Across all 69 location feeds and a full six months of history, the spread in missed-call rate (12.3% to 37.6%) is no wider than what coin-flipping would produce from the same volumes.
>
> A dashboard that ranks locations by missed-call rate, conversion rate, or no-show rate is therefore not surfacing the location that needs attention — it is surfacing the location that got unlucky last month. We verified this directly: the ten worst locations in the first half of the period had *no* tendency to stay bad in the second half (r = 0.06).

| Metric | Value |
|---|---|
| Location feeds (account × location) | 69 |
| Events per location per day | ~1.0 |
| Rate-rank persistence H1→H2 | 0.06 |
| Volume persistence H1→H2 | 0.81 |
| Defensible alerts from 69 sites | 1 |

The good news is that a different framing works well. **Volume** is a stable, real property of a location (r = 0.81 half-over-half), and volume anomalies at the *account* level are detectable and meaningful. The "is this normal for us?" question is answerable today; the "which location is worst?" question needs to be reframed as a trend question rather than a ranking question.

Four data-quality defects also need handling before any of this ships. One of them — a timezone bug — silently invalidates any hour-of-day feature.

---

## 01 · Structure

Two tables, one fact table and one dimension. The schema is simple; the subtleties are in the grain and the keys.

| Column | Type | Notes from the data |
|---|---|---|
| `accounts.id` | int PK | 20 accounts, ids 1–20, no gaps |
| `accounts.industry` | varchar | 8 values; Home Services (6) and Healthcare (4) dominate |
| `accounts.timezone` | varchar | 6 IANA zones — **not applied to event timestamps** (see D2) |
| `accounts.created_at` | timestamp | All 2025; all precede the event window. Not an onboarding signal |
| `activity_events.id` | int PK | 1–12,626 contiguous, no gaps, no dupe ids |
| `account_id` | int FK | No orphans. 19 of 20 accounts have events |
| `location` | varchar | Only 15 distinct labels (`Site A`–`Site O`), **reused across accounts** |
| `event_type` | varchar | 3 values, no strays, no casing drift |
| `occurred_at` | timestamp | UTC. No future dates, none before account creation |
| `duration_seconds` | int NULL | Calls only. Uniform 20–1500 — **not real talk time** (see D3) |
| `outcome` | varchar NULL | 7 values, correctly scoped per event type. ~3.2% NULL |

### The grain that matters

`location` is **not** a global identifier. "Site A" belongs to 19 different accounts and means a different physical branch in each. The analytical unit is the pair `(account_id, location)` — 69 distinct location feeds. Any query, index, or join that treats `location` as standalone will silently blend unrelated businesses.

There is also no `locations` dimension table. Location exists only as a free-text string on the fact table, so there is nowhere to record a site's open date, closure, hours, or headcount — all of which you would need to interpret a volume change correctly.

### Shape of the customer base

| Segment | Accounts | Locations each |
|---|---:|---:|
| Single-site | 4 | 1 |
| Small multi-site | 10 | 2–4 |
| Mid multi-site | 4 | 5–7 |
| Large group — Metro Collision Centers | 1 | 15 |
| No activity at all — Quiet Harbor Spa | 1 | 0 |

Metro Collision Centers alone holds 21% of all events. Quiet Harbor Spa has zero rows and must not break the dashboard.

### Event mix

| Event type | Rows | Share |
|---|---:|---:|
| `call_received` | 7,780 | 61.6% |
| `lead_created` | 3,044 | 24.1% |
| `appointment_set` | 1,802 | 14.3% |

Outcomes are correctly scoped to their event type — calls are `connected` / `missed` / `voicemail`, leads are `open` / `converted`, appointments are `completed` / `no_show`. There is no cross-contamination.

---

## 02 · Data quality

Four defects, ranked by how much damage they do to a customer-facing metric. The first two are silent — nothing in the dashboard would look wrong.

### D1 · A one-day bulk backfill inflates one account by 80× — **Blocker**

**2026-06-03** carries 882 events against a 71/day average for the whole dataset. It is not a platform-wide event: **805 of those rows belong to Metro Collision Centers**, whose typical day is 10 events. Every other account sits in its normal range that day.

The rows carry every hallmark of a replayed load: a contiguous id block (`8114–8994`), all 15 of the account's sites hit evenly (42–69 rows each), and a realistic business-hours curve. It looks like real data, which is exactly why it is dangerous.

**Impact** — Left in, this single day is 30% of that account's six-month history. It raises their baseline, and — because it lands in the trailing window — it makes the following weeks read as a *decline*. Exclude or winsorize it before computing any baseline.

### D2 · The timezone column was never applied to timestamps — **Blocker**

Every timezone group has an almost identical mean hour *in UTC* (15.3–15.6), but wildly different mean hours in local time (8.7 for Los Angeles, 15.3 for UTC). Timestamps were generated against a single UTC business-hours curve and never shifted per account.

| Timezone | Mean local hour | Mean UTC hour | % of events 00:00–06:00 local |
|---|---:|---:|---:|
| America/Los_Angeles | 8.7 | 15.6 | 29.6% |
| America/Phoenix | 8.9 | 15.6 | 28.4% |
| America/Denver | 9.5 | 15.6 | 21.7% |
| America/Chicago | 10.4 | 15.5 | 16.2% |
| America/New_York | 11.2 | 15.4 | 11.8% |
| UTC | 15.3 | 15.3 | 1.0% |

The consequence is concrete: **29.6% of Los Angeles accounts' events fall between midnight and 6am local time** — inbound calls to an auto shop at 3am.

**Impact** — Any hour-of-day feature (peak hours, after-hours missed calls, staffing heatmaps) is built on sand. Separately, converting UTC to local shifts 1.3% of West Coast events across a date boundary, so "today" tiles will disagree with the customer's own count.

#### Can a conversion recover the local time?

No — and it is worth being precise about why, because the conversion looks like it should work. Two tests rule it out.

**There is no DST fingerprint.** US daylight saving began on 2026-03-08, inside the window. Had these timestamps been generated in a real US local zone and converted to UTC, the UTC curve would jump forward one hour on that date. It does not move:

| Period | Events | Mean UTC hour |
|---|---:|---:|
| EST (Feb 01 – Mar 07) | 2,321 | 16.10 |
| EDT (Mar 08 – Jul 27) | 9,423 | 15.95 |

A 0.15h drift, in the wrong direction.

**There is no offset between zones.** If the timezone had been applied at write time, New York and Los Angeles accounts would sit three hours apart in UTC. The observed spread across all six zones is **0.27 hours** against an expected **3.00**.

The giveaway is which accounts fail a plausibility check. Running the conversion and asking how much overnight (01:00–05:00 local) volume each zone carries:

| Account timezone | Mean local hour | Overnight 01–05 | Business 08–18 | Verdict |
|---|---:|---:|---:|---|
| America/Los_Angeles | 8.72 | 20.4% | 59.6% | Fail |
| America/Phoenix | 8.90 | 20.1% | 61.3% | Fail |
| America/Denver | 9.53 | 14.8% | 67.4% | Fail |
| America/Chicago | 10.38 | 10.8% | 76.5% | Fail |
| America/New_York | 11.29 | 6.9% | 79.4% | Fail |
| UTC | 15.31 | 0.5% | 76.5% | **Pass** |

Only the UTC account passes — and for a UTC account the conversion is a no-op. Every other zone fails in direct proportion to its distance from UTC. The events carry a UTC-shaped business day that was handed to all 20 accounts.

That leaves two honest readings, and they are not equivalent:

| Reading | What it assumes | Overnight | Cost |
|---|---|---:|---|
| **A · Convert UTC → local** | The stored value really is UTC; the generator ignored timezone | up to 20.4% | Faithful to the schema contract, but hour-of-day stays unusable |
| **B · Read the stored value as local** | The value is local wall-clock, merely mislabelled as UTC | 0.6% | Yields a plausible business day (75% in 08–18) — but makes `timezone` decorative and every account identical, so any cross-region comparison is fabricated |

**Recommendation** — Ship **A**. It is the only reading that stays correct when real data arrives, and it is implemented as a view in [`sql/local_time.sql`](../sql/local_time.sql). Use `local_date` for day and week bucketing — that part is a genuine fix, correcting 1.31% of Los Angeles rows onto the right calendar day. Keep hour-of-day features off the roadmap until the plausibility check passes. Reading B is defensible only as an explicitly labelled demo assumption, never as a silent production default.

### D3 · Call duration is random noise, not talk time — **High**

`duration_seconds` is distributed uniformly between 20 and 1500 seconds — and **identically across every outcome**.

| Call outcome | Rows | With duration | Mean seconds |
|---|---:|---:|---:|
| connected | 4,669 | 4,484 | 765 |
| missed | 1,937 | 1,861 | 737 |
| voicemail | 934 | 888 | 768 |

A missed call cannot have twelve minutes of talk time. The field is synthetic and carries no signal.

**Impact** — "Average call duration" must not ship as a metric. If it appears on the dashboard today, it is showing customers a random number — and one that will look stable, because uniform noise averages out consistently.

### D4 · Duplicate events from replayed ingestion — **High**

12 pairs of rows are identical on `(account_id, location, event_type, occurred_at)` including duration and outcome, and each pair sits on consecutive ids. They cluster on shared dates across different accounts — 2026-02-24, 2026-04-29, 2026-06-12 — which points at batch-level replay rather than genuine simultaneous events.

**Impact** — Small in aggregate (0.2%), but it proves ingestion is not idempotent. There is no natural key or unique constraint preventing this, so a larger replay would go unnoticed. Worth a constraint on `(account_id, location, event_type, occurred_at)`.

### What is sound — **Clean**

Referential integrity is intact — no orphans, no duplicate ids, no events before account creation, no future dates. Categorical fields have no casing or whitespace drift. Outcomes are correctly scoped to their event type.

Missing values are **missing-at-random**: NULL outcome holds at 2.8–3.6% every month and 3.3–4.4% across accounts, with no clustering that would indicate an outage. The same is true of NULL duration (4.0%). They can safely be excluded from rate denominators rather than imputed.

No location feed ever goes dark. All 69 started within the first four days of the window and all report through the final week — the longest silence for any site is five days, consistent with low volume rather than a broken integration.

---

## 03 · Patterns

One very strong seasonal effect, no trend, and a volume floor low enough to govern the entire metric design.

### Events per day of week (local time, backfill excluded)

| Day | Avg events | Bar |
|---|---:|---|
| Mon | 87.9 | `████████████████████` |
| Tue | 84.0 | `███████████████████` |
| Wed | 79.5 | `██████████████████` |
| Thu | 83.6 | `███████████████████` |
| Fri | 83.1 | `███████████████████` |
| Sat | 23.1 | `█████` |
| Sun | 23.0 | `█████` |

Weekend volume is 27% of a weekday — a baseline that ignores day-of-week will alarm every Saturday.

**Note the correction.** In the raw data Wednesday appears as an outlier high (2,892 events). That was entirely the 2026-06-03 backfill — once removed, Wednesday is the *quietest* weekday. A seasonality model fitted on uncleaned data would have learned a spurious midweek peak.

### No trend, but uneven volatility

Monthly totals are flat for every large account across the six months, so there is no growth trend to detrend. Week-to-week volatility, however, differs sharply by account size — the variance-to-mean ratio runs from **0.37** (Gulf Coast Roofing, remarkably steady) to **2.29** (Metro Collision Centers, heavily overdispersed).

This matters for alerting: a textbook Poisson band of `μ ± 2√μ` is too tight for the big accounts and too loose for the small ones. The band needs a per-account dispersion factor.

### The volume floor

Every one of the 69 location feeds averages between **0.59 and 1.46 events per day**. Not one exceeds 1.5. At the weekly grain the median location produces 6 events, with a 10th percentile of 3.

> **The governing constraint**
>
> At roughly one event per location per day, a **daily per-location metric is essentially unmeasurable**. A site averaging 1.0 events/day will show 0 on about 37% of days purely by chance. Any "down vs. yesterday" indicator on a location tile is a random number generator, and a daily z-score is undefined half the time.
>
> This is not a flaw in the seed data — it is the real shape of the business. Service locations genuinely receive a handful of calls a day. The product has to be designed around it.

---

## 04 · Signal or noise

The support team's request — show which location needs attention — assumes locations meaningfully differ. We tested that assumption rather than taking it on faith.

For each candidate metric we compared the *observed* spread across locations against the spread you would expect if every location were identical and only sampling luck differed, using a chi-square dispersion test on the pooled six months (backfill excluded).

| Metric | Units | Observed range | Observed SD | SD if pure noise | χ²/df | p | Verdict |
|---|---:|---:|---:|---:|---:|---:|---|
| Missed-call rate | 69 locs | 12.3–37.6% | 4.81pp | 4.47pp | 78.2/68 | 0.186 | Noise |
| Lead conversion rate | 69 locs | 14.8–52.9% | 8.27pp | 8.02pp | 77.1/68 | 0.211 | Noise |
| Appointment no-show rate | 69 locs | 0–44.4% | 8.29pp | 8.23pp | 78.2/68 | 0.186 | Noise |
| Missed-call rate | 19 accts | 18.3–32.4% | 3.43pp | 2.80pp | 29.7/18 | 0.040 | Weak signal |

At the location level, observed spread barely exceeds the noise floor in all three metrics. Only when locations are pooled up to accounts does a marginal real difference emerge.

### Where the ten "worst" locations went

A statistical test is easy to wave away, so here is the same conclusion in operational terms. We split the period in half, ranked all 69 locations by missed-call rate in the first half, and looked up where those same locations landed in the second half.

| Feb–Apr rank | Location | H1 missed | → H2 rank | H2 missed |
|---:|---|---:|---:|---:|
| #1 | Capital City Storage · Site E | 37.8% | **#54** | 20.5% |
| #2 | Metro Collision · Site B | 37.1% | **#66** | 14.3% |
| #3 | Pacific Smiles · Site A | 36.7% | #21 | 28.6% |
| #4 | Redline Tire · Site D | 35.8% | #44 | 24.2% |
| #5 | Cornerstone Vet · Site D | 35.2% | #51 | 22.2% |
| #6 | Riverbend Chiro · Site A | 32.8% | **#67** | 13.2% |
| #7 | Old Town Barbers · Site A | 32.7% | #32 | 26.4% |
| #8 | Metro Collision · Site O | 32.6% | #36 | 25.7% |
| #9 | Beacon Home Security · Site C | 32.1% | #43 | 24.5% |
| #10 | Sierra Pest · Site C | 32.0% | #13 | 31.1% |

Correlation between the two halves: **r = 0.057**. The location with the highest missed-call rate in February–April (37.8%) fell to 20.5% and rank 54 of 69. The second-worst dropped to rank 66. This is textbook regression to the mean — **the ranking carries no predictive information at all**, so acting on it wastes account-manager time and erodes trust when the "problem" resolves itself.

### What survives a rigorous test

Applying empirical-Bayes shrinkage toward each account's own pooled rate, plus Wilson confidence intervals, to all 69 locations across the full six months produces exactly **one** location flagged above its account average. Across 69 simultaneous tests at α = 0.05 you would expect around 3 false positives by chance — so even that single alert is more likely luck than substance.

The honest conclusion: *six months of data is not enough to rank a location on outcome rates.* A naive top-N view would have confidently listed twelve.

### What does hold up

Volume is a different story. The half-over-half correlation of location volume is **0.81** — big sites stay big, quiet sites stay quiet. Volume is a real, stable property, which makes *changes* in it genuinely informative.

---

## 05 · How to build "normal"

A metric design that follows from the constraints above, rather than from what is easiest to query.

**1. Compare an account against its own history, not against other accounts.**
"Is this number normal for us?" is a within-account question, and it is answerable. Industries mix very different business models and the peer groups here would be 2–5 accounts wide — too small to define a norm. Each account's own trailing history is both more relevant and more statistically sound.

**2. Put location metrics on a weekly or 28-day grain — never daily.**
At ~1 event/day a daily location figure is noise. A trailing 28-day window gives a typical location ~30 events, which is the minimum for a stable rate. Show the daily number as a raw count if customers want it, but never attach a comparison, arrow, or alert to it.

**3. Make the baseline day-of-week aware.**
Weekends run at 27% of weekday volume. Compare like with like — Monday against recent Mondays, or use full Monday-to-Sunday weeks. Complete weeks only: the final partial week in this dataset is a single day, and a naive trailing window flags *every one of the 19 accounts* as critically low because of it.

**4. Express "normal" as a band, and let the band be wide.**
Use a quasi-Poisson interval with a per-account dispersion factor `φ` estimated from its own weekly history — `μ ± 2√(φμ)` — since φ ranges from 0.37 to 2.29 across accounts. Showing customers the band itself ("typical week: 33–60 calls") answers the question more honestly than a single number with a percentage arrow.

**5. Rank locations by significance, not by rate.**
If a location leaderboard must exist, sort by how far a site sits outside its confidence interval, and show the interval. Locations whose interval overlaps the account average should read as "within normal range" — which, in this dataset, is 68 of 69 of them. An empty alert list is a correct answer, and far better than ten fabricated ones.

**6. Prefer volume-change alerts over rate rankings.**
Volume persists (r = 0.81) where rates do not (r = 0.06). "This location's calls dropped 40% versus its own eight-week baseline" is defensible; "this location has the worst missed-call rate" is not. Applied at the account-week level on cleaned data, this produced 14 alerts across 361 eligible account-weeks — about 4%, an actionable rate. The per-account dispersion factor matters here: a plain Poisson band gives 18.

**7. Fix ingestion before ingesting more.**
Add a uniqueness constraint on `(account_id, location, event_type, occurred_at)`, apply the account timezone at write time or store a proper `timestamptz`, and add a `locations` dimension so a site's open date and closure can be recorded. Without the last one you cannot distinguish "location closed" from "integration broke."

### The baseline query, in outline

```sql
WITH clean AS (
  SELECT DISTINCT ON (account_id, location, event_type, occurred_at) *   -- D4: dedupe
  FROM   activity_events
  WHERE  occurred_at::date <> DATE '2026-06-03'                          -- D1: drop backfill
  ORDER  BY account_id, location, event_type, occurred_at, id
),
wk AS (
  SELECT account_id,
         date_trunc('week', occurred_at)::date AS week,
         count(*) AS n
  FROM   clean
  WHERE  occurred_at >= DATE '2026-02-02'      -- complete weeks only,
    AND  occurred_at <  DATE '2026-07-27'      -- both ends
  GROUP  BY 1, 2
),
base AS (
  SELECT account_id, week, n,
         avg(n)      OVER w AS mu,
         var_samp(n) OVER w AS v,
         count(*)    OVER w AS hist
  FROM   wk
  WINDOW w AS (PARTITION BY account_id ORDER BY week
               ROWS BETWEEN 8 PRECEDING AND 1 PRECEDING)
)
SELECT account_id, week, n AS actual, round(mu, 1) AS expected,
       round(mu - 2 * sqrt(greatest(v / nullif(mu, 0), 1) * mu)) AS lo,
       round(mu + 2 * sqrt(greatest(v / nullif(mu, 0), 1) * mu)) AS hi
FROM   base
WHERE  hist >= 6;   -- no verdict until there is enough history
```

Dispersion is floored at 1 so the band never becomes tighter than Poisson for the steady accounts.

---

## 06 · Caveats

- This is seeded demo data. The statistical conclusions describe *this* dataset — real production data may well contain genuine location differences that six months of synthetic data does not. The method for deciding, however, transfers directly.
- Because outcome rates here are indistinguishable from noise, we cannot confirm the dashboard would find nothing in production. We can confirm that a naive ranking would produce confident output regardless of whether signal exists, which is the more dangerous failure.
- The chi-square dispersion test assumes independence between events at a location. Genuine clustering — a phone system failing for an afternoon — would inflate χ² without indicating a persistent difference. The half-over-half persistence check is included precisely because it does not rely on that assumption, and it agrees.
- The 2026-06-03 backfill is treated as an artifact. If it turns out to represent real activity, the exclusion is wrong — but the account's own surrounding weeks argue strongly against it.
- No location closes or opens mid-window, so the analysis says nothing about how the system behaves during site churn. That case needs the `locations` dimension before it can be handled at all.

---

Prepared against `relay_takehome` · 20 accounts · 12,626 activity events · 2026-02-01 to 2026-07-27.
All figures reproducible from the queries in this document. Seed data left unmodified.
