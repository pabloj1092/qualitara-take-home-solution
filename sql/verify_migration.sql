-- Stage 2 checkpoint (PLAN.md § Stage 2 · Migrations). Run against a pristine seed, immediately
-- after `dotnet run` applies the migration:
--
--   docker compose down -v && docker compose up -d
--   dotnet run --project src/Relay.Api
--   docker exec -i relay_takehome_postgres psql -U relay -d relay_takehome -f - < sql/verify_migration.sql
--
-- Every check below prints as its own row with a `verdict` column that must read PASS.
--
-- A note on the two headline numbers from PLAN.md's Verified facts table (12 duplicate pairs,
-- 805 rows on 2026-06-03 for account 6): both are RAW-DATA facts, asserted directly below
-- (checks 8 and 10). They are NOT a clean row-count delta formula — D1 (exclusion) and D4
-- (dedupe) interact, because one of the 12 duplicate pairs happens to fall on the excluded day.
-- Concretely: activity_events_local (12,626) minus activity_events_clean (11,810) is 816, not
-- 805 + 12 = 817 or a plain 805; and for account 6 alone, local (2,641) minus clean (1,833) is
-- 808, not 805 — three of account 6's four duplicate pairs sit on OTHER days and are removed by
-- dedupe independently of the exclusion. Checks 9 and 11 assert the two mechanisms did their job
-- precisely (no duplicates survive; the excluded day is fully gone) rather than asserting a
-- combined delta that the interaction makes untrue.

-- 1 · locations row count (Verified facts: 69 distinct (account_id, location) pairs).
SELECT '1 · locations = 69 rows' AS check,
       CASE WHEN count(*) = 69 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM locations;

-- 2 · every (account_id, location) actually seen in activity_events has a matching locations row.
SELECT '2 · locations covers every (account_id, location) in activity_events' AS check,
       CASE WHEN count(*) = 0 THEN 'PASS' ELSE 'FAIL - ' || count(*) || ' unmatched pairs' END AS verdict
FROM (
    SELECT DISTINCT e.account_id, e.location
    FROM activity_events e
    LEFT JOIN locations l ON l.account_id = e.account_id AND l.name = e.location
    WHERE l.id IS NULL
) AS unmatched;

-- 3 · catalog / settings / exclusion row counts.
SELECT '3a · event_type_catalog = 3 rows' AS check,
       CASE WHEN count(*) = 3 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM event_type_catalog;

SELECT '3b · outcome_catalog = 7 rows' AS check,
       CASE WHEN count(*) = 7 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM outcome_catalog;

SELECT '3c · account_dashboard_settings = 20 rows' AS check,
       CASE WHEN count(*) = 20 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM account_dashboard_settings;

SELECT '3d · data_quality_exclusions = 1 row' AS check,
       CASE WHEN count(*) = 1 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM data_quality_exclusions;

-- 4 · iso_weeks spine: 27 rows, 2026-01-26 .. 2026-07-27, gapless.
SELECT '4a · iso_weeks = 27 rows, 2026-01-26 .. 2026-07-27' AS check,
       CASE WHEN count(*) = 27 AND min(week_start) = DATE '2026-01-26'
                 AND max(week_start) = DATE '2026-07-27'
            THEN 'PASS'
            ELSE 'FAIL - count=' || count(*) || ' min=' || min(week_start) || ' max=' || max(week_start)
       END AS verdict
FROM iso_weeks;

SELECT '4b · iso_weeks is gapless (no missing Monday in range)' AS check,
       CASE WHEN count(*) = 0 THEN 'PASS' ELSE 'FAIL - ' || count(*) || ' gap(s)' END AS verdict
FROM (
    SELECT week_start,
           week_start - LAG(week_start) OVER (ORDER BY week_start) AS gap
    FROM iso_weeks
) AS gaps
WHERE gap IS NOT NULL AND gap <> 7;

-- 5 · dedupe (D4): exactly 12 raw duplicate pairs in activity_events, and none survive in
-- activity_events_clean.
SELECT '5a · activity_events has exactly 12 duplicate (account,location,event_type,occurred_at) pairs' AS check,
       CASE WHEN count(*) = 12 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM (
    SELECT account_id, location, event_type, occurred_at
    FROM activity_events
    GROUP BY account_id, location, event_type, occurred_at
    HAVING count(*) > 1
) AS dups;

SELECT '5b · activity_events_clean has zero surviving duplicates' AS check,
       CASE WHEN count(*) = 0 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM (
    SELECT account_id, location, event_type, occurred_at_utc
    FROM activity_events_clean
    GROUP BY account_id, location, event_type, occurred_at_utc
    HAVING count(*) > 1
) AS surviving_dups;

-- 6 · exclusion (D1): 805 raw rows on 2026-06-03 for account 6, and none of them survive in
-- activity_events_clean.
SELECT '6a · 805 raw rows on 2026-06-03 for account 6' AS check,
       CASE WHEN count(*) = 805 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM activity_events
WHERE account_id = 6 AND occurred_at::date = DATE '2026-06-03';

SELECT '6b · activity_events_clean has zero rows for account 6 on 2026-06-03' AS check,
       CASE WHEN count(*) = 0 THEN 'PASS' ELSE 'FAIL - got ' || count(*) END AS verdict
FROM activity_events_clean
WHERE account_id = 6 AND local_date = DATE '2026-06-03';

-- 7 · weekly_activity_facts is dense: locations × iso_weeks × outcome slots (7 outcome rows +
-- 1 null-outcome row per event type = 10 slots). opened_on/closed_on are NULL throughout this
-- seed, so no location-week is clipped.
SELECT '7 · weekly_activity_facts row count = locations × iso_weeks × 10 slots' AS check,
       CASE WHEN (SELECT count(*) FROM weekly_activity_facts)
                 = (SELECT count(*) FROM locations) * (SELECT count(*) FROM iso_weeks) * 10
            THEN 'PASS'
            ELSE 'FAIL - got ' || (SELECT count(*) FROM weekly_activity_facts)
                 || ', expected ' || ((SELECT count(*) FROM locations) * (SELECT count(*) FROM iso_weeks) * 10)
       END AS verdict;

-- 8 · D2 record: timezone was never applied at write time. This check must keep FAILing — that
-- FAIL is the documented audit finding, not a bug.
SELECT '8 · check_timezone_applied still reads the documented D2 FAIL' AS check,
       CASE WHEN verdict LIKE 'FAIL%' THEN 'PASS (D2 confirmed still present)' ELSE 'FAIL - D2 unexpectedly absent: ' || verdict END AS verdict
FROM check_timezone_applied;

-- 9 · pushdown check (PLAN.md § Pushdown check). Pass = an Index Scan on activity_events using
-- ix_activity_events_account_id_occurred_at, with the account predicate applied there. Fail = a
-- Seq Scan on activity_events, or a plan that materialises the cross join before filtering.
-- Printed as text for a human (or the next reviewer) to read directly, plus a best-effort
-- automated verdict below.
CREATE OR REPLACE FUNCTION pg_temp.pushdown_plan() RETURNS TABLE(line text) AS $$
BEGIN
    RETURN QUERY EXECUTE $q$
        EXPLAIN (ANALYZE, BUFFERS)
        SELECT event_type, outcome, sum(event_count)
        FROM   weekly_activity_facts
        WHERE  account_id = 6
          AND  week_start_local BETWEEN DATE '2026-05-25' AND DATE '2026-07-20'
        GROUP  BY 1, 2
    $q$;
END;
$$ LANGUAGE plpgsql;

SELECT * FROM pg_temp.pushdown_plan();

-- Either activity_events index is an acceptable pushdown target: the planner may prefer
-- (account_id, occurred_at) or the more specific (account_id, location, event_type, occurred_at)
-- depending on how many locations are selected. What matters is that the account predicate
-- reaches an Index Scan on activity_events itself, and that no Seq Scan on it appears anywhere.
SELECT '9 · predicate pushes down to an Index Scan on activity_events' AS check,
       CASE
           WHEN full_plan ~ 'Seq Scan on activity_events\b'
               THEN 'FAIL - Seq Scan on activity_events (see plan printed above)'
           WHEN full_plan ~ 'Index Scan using ix_activity_events_\S+ on activity_events'
               THEN 'PASS'
           ELSE 'FAIL - no Index Scan on activity_events via either activity_events index found (see plan printed above)'
       END AS verdict
FROM (SELECT string_agg(line, E'\n') AS full_plan FROM pg_temp.pushdown_plan()) AS p;

DROP FUNCTION pg_temp.pushdown_plan();
