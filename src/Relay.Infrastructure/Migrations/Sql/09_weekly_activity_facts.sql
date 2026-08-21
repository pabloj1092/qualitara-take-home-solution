-- The dense fact — a plain view, not materialized (PLAN.md § Why a view, not a materialized
-- view). Dense over locations × iso_weeks (clipped by opened_on/closed_on) × outcome slots,
-- where a "slot" is every (event_type, code) in outcome_catalog plus one outcome-IS-NULL slot
-- per event type, so the null-outcome rows have a home. Every non-recursive CTE below is
-- referenced exactly once, which lets Postgres inline rather than materialize it — that inlining
-- is what lets an outer `WHERE account_id = ...` reach the index on activity_events (see
-- sql/verify_migration.sql's pushdown check).
--
-- days_included/expected_days are a (location, week) fact, computed once in `completeness` and
-- joined onto every event_type/outcome row of that location-week — they repeat identically across
-- those rows by design (PLAN.md Open Question 6). Never SUM them across an individual location's
-- own rows; pooling across locations is SUM(days_included) / SUM(expected_days).
CREATE OR REPLACE VIEW weekly_activity_facts AS
WITH slots AS (
    -- Catalog codes and the null-outcome slot, plus (defensively) any real (event_type, outcome)
    -- pair actually present in the cleaned data that ISN'T in the catalog yet — a typo'd, retired,
    -- or not-yet-cataloged outcome value must still get a dense slot. Without this branch such a
    -- row has no matching slot at all and the final LEFT JOIN (below) drops it from the view
    -- entirely: not counted in the event-type total, not in any rate tile, not even surfaced as a
    -- null outcome. UNION (not UNION ALL) dedupes across all three branches, so a real value that
    -- already matches a catalog code doesn't create a second slot.
    SELECT event_type, code AS outcome
    FROM outcome_catalog
    UNION
    SELECT code AS event_type, NULL::text AS outcome
    FROM event_type_catalog
    UNION
    SELECT event_type, outcome
    FROM activity_events_clean
    WHERE outcome IS NOT NULL
),
spine AS (
    SELECT l.account_id, l.id AS location_id, l.name, w.week_start
    FROM locations l
    CROSS JOIN iso_weeks w
    WHERE (l.opened_on IS NULL OR w.week_end   >= l.opened_on)
      AND (l.closed_on IS NULL OR w.week_start <= l.closed_on)
),
dense AS (
    SELECT s.account_id, s.location_id, s.name, s.week_start, sl.event_type, sl.outcome
    FROM spine s
    CROSS JOIN slots sl
),
-- Day completeness for the location-week, regardless of event type: expected_days is 7 minus
-- days outside the location's opened_on/closed_on window; days_included further subtracts days
-- outside the global data range (computed live, not snapshotted — PLAN.md Open Question 7) and
-- days matched by an account/location-scoped data_quality_exclusions row. This is what makes both
-- the first calendar week (2026-01-26) and the last (2026-07-27) read as 1-of-7.
--
-- The exclusion match below deliberately checks `x.event_type IS NULL` rather than mirroring
-- activity_events_clean's event-type-aware match: days_included/expected_days are a shared
-- location-week fact, repeated identically across every event_type/outcome row of that week
-- (PLAN.md Open Question 6) — so an event-type-SCOPED exclusion should shrink only that event
-- type's numerator (already handled correctly in activity_events_clean), never the shared
-- denominator every other event type also reads. Only an exclusion with no event_type (applying
-- to the whole location-day) may reduce this shared value.
completeness AS (
    SELECT
        l.account_id,
        l.id AS location_id,
        w.week_start,
        count(*) FILTER (
            WHERE (l.opened_on IS NULL OR day >= l.opened_on)
              AND (l.closed_on IS NULL OR day <= l.closed_on)
        ) AS expected_days,
        count(*) FILTER (
            WHERE (l.opened_on IS NULL OR day >= l.opened_on)
              AND (l.closed_on IS NULL OR day <= l.closed_on)
              AND day BETWEEN (SELECT min(local_date) FROM activity_events_clean)
                           AND (SELECT max(local_date) FROM activity_events_clean)
              AND NOT EXISTS (
                    SELECT 1 FROM data_quality_exclusions x
                    WHERE (x.account_id IS NULL OR x.account_id = l.account_id)
                      AND (x.location   IS NULL OR x.location   = l.name)
                      AND x.event_type IS NULL
                      AND day BETWEEN x.from_date AND x.to_date)
        ) AS included_days
    FROM locations l
    CROSS JOIN iso_weeks w
    CROSS JOIN LATERAL generate_series(w.week_start, w.week_start + 6, INTERVAL '1 day') AS day
    GROUP BY l.account_id, l.id, w.week_start
),
events_agg AS (
    SELECT
        account_id,
        location_id,
        date_trunc('week', local_date)::date AS week_start_local,
        event_type,
        outcome,
        count(*) AS event_count
    FROM activity_events_clean
    GROUP BY account_id, location_id, date_trunc('week', local_date)::date, event_type, outcome
)
SELECT
    d.account_id,
    d.location_id,
    d.week_start                AS week_start_local,
    d.event_type,
    d.outcome,
    COALESCE(e.event_count, 0)  AS event_count,
    c.included_days             AS days_included,
    c.expected_days             AS expected_days
FROM dense d
JOIN completeness c
  ON c.account_id = d.account_id AND c.location_id = d.location_id AND c.week_start = d.week_start
LEFT JOIN events_agg e
  ON e.account_id = d.account_id AND e.location_id = d.location_id
 AND e.week_start_local = d.week_start
 AND e.event_type = d.event_type
 AND e.outcome IS NOT DISTINCT FROM d.outcome;
