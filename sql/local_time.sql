-- Local-time layer for activity_events
--
-- occurred_at is stored in UTC (per schema.sql). Every customer-facing figure is
-- read in the account's own local time, so the conversion belongs in one place
-- rather than in each dashboard query.
--
-- IMPORTANT: in the current seed data this conversion is mechanically correct but
-- does NOT produce plausible local hours -- accounts.timezone was never applied
-- when the timestamps were generated (see check_local_hour_plausibility below).
-- Use local_date for day bucketing; do not build hour-of-day features until the
-- plausibility check passes on real data.

CREATE OR REPLACE VIEW activity_events_local AS
SELECT
    e.id,
    e.account_id,
    a.name                AS account_name,
    a.timezone            AS account_timezone,
    e.location,
    e.event_type,
    e.occurred_at         AS occurred_at_utc,
    -- occurred_at is a naive TIMESTAMP holding UTC: label it UTC, then read it
    -- in the account's zone. Handles DST automatically via the IANA database.
    (e.occurred_at AT TIME ZONE 'UTC' AT TIME ZONE a.timezone) AS occurred_at_local,
    (e.occurred_at AT TIME ZONE 'UTC' AT TIME ZONE a.timezone)::date        AS local_date,
    extract(hour  FROM e.occurred_at AT TIME ZONE 'UTC' AT TIME ZONE a.timezone)::int AS local_hour,
    extract(isodow FROM e.occurred_at AT TIME ZONE 'UTC' AT TIME ZONE a.timezone)::int AS local_dow,
    (extract(isodow FROM e.occurred_at AT TIME ZONE 'UTC' AT TIME ZONE a.timezone)::int >= 6) AS is_weekend,
    e.duration_seconds,
    e.outcome
FROM activity_events e
JOIN accounts a ON a.id = e.account_id;

COMMENT ON VIEW activity_events_local IS
  'activity_events with the account timezone applied. local_date is safe for day '
  'bucketing; local_hour is only meaningful once check_local_hour_plausibility passes.';


-- ---------------------------------------------------------------------------
-- Check 1: are local hours plausible for an inbound-activity business?
-- Genuine local business hours put almost nothing between 01:00 and 05:00.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW check_local_hour_plausibility AS
SELECT
    account_timezone,
    count(*)                                                     AS events,
    round(avg(local_hour)::numeric, 2)                           AS mean_local_hour,
    round(100.0 * count(*) FILTER (WHERE local_hour BETWEEN 1 AND 5) / count(*), 1)
                                                                 AS pct_overnight,
    round(100.0 * count(*) FILTER (WHERE local_hour BETWEEN 8 AND 18) / count(*), 1)
                                                                 AS pct_business_hours,
    CASE WHEN 100.0 * count(*) FILTER (WHERE local_hour BETWEEN 1 AND 5) / count(*) > 5.0
         THEN 'FAIL - implausible overnight volume'
         ELSE 'pass' END                                         AS verdict
FROM activity_events_local
GROUP BY 1;


-- ---------------------------------------------------------------------------
-- Check 2: was the timezone applied at write time at all?
-- If it was, accounts in zones 3h apart sit ~3h apart in UTC. If the spread is
-- near zero, every account shares one UTC curve and local hours are fiction.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW check_timezone_applied AS
WITH m AS (
    SELECT a.timezone, avg(extract(hour FROM e.occurred_at)) AS mean_utc_hour
    FROM activity_events e
    JOIN accounts a ON a.id = e.account_id
    GROUP BY 1
)
SELECT
    round((max(mean_utc_hour) - min(mean_utc_hour))::numeric, 2) AS observed_utc_spread_hours,
    3.00                                                         AS expected_spread_hours,
    CASE WHEN max(mean_utc_hour) - min(mean_utc_hour) < 1.0
         THEN 'FAIL - timezone never applied at write time'
         ELSE 'pass' END                                         AS verdict
FROM m;


-- ---------------------------------------------------------------------------
-- Check 3: how many events change calendar day under the conversion?
-- This is the part of the fix that matters for daily/weekly aggregates.
-- ---------------------------------------------------------------------------
CREATE OR REPLACE VIEW check_date_shift AS
SELECT
    account_timezone,
    count(*)                                                        AS events,
    count(*) FILTER (WHERE occurred_at_utc::date <> local_date)      AS rows_shifting_day,
    round(100.0 * count(*) FILTER (WHERE occurred_at_utc::date <> local_date) / count(*), 2)
                                                                     AS pct_shifting_day
FROM activity_events_local
GROUP BY 1
ORDER BY pct_shifting_day DESC;
