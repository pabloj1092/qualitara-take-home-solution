-- Seed rows are logic, not reference data (PLAN.md § Migration 001 — detail). Without the
-- exclusion row below, activity_events_clean is a no-op and the account-6 dedupe/exclusion
-- assertions (805 rows) fail on a fresh database.

INSERT INTO event_type_catalog (code, display_name, sort_order) VALUES
    ('call_received',   'Calls received',   1),
    ('lead_created',    'Leads created',    2),
    ('appointment_set', 'Appointments set', 3)
ON CONFLICT (code) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    sort_order   = EXCLUDED.sort_order;

-- Polarity table (Requirements §Data-quality): voicemail and open are neutral — "worse than
-- connected, better than missed" / "pending, not an outcome" are not claims either colour can
-- defend.
INSERT INTO outcome_catalog (event_type, code, display_name, polarity, sort_order) VALUES
    ('call_received',   'connected', 'Connected', 'good',    1),
    ('call_received',   'missed',    'Missed',    'bad',     2),
    ('call_received',   'voicemail', 'Voicemail', 'neutral', 3),
    ('lead_created',    'converted', 'Converted', 'good',    1),
    ('lead_created',    'open',      'Open',      'neutral', 2),
    ('appointment_set', 'completed', 'Completed', 'good',    1),
    ('appointment_set', 'no_show',   'No-show',   'bad',     2)
ON CONFLICT (event_type, code) DO UPDATE SET
    display_name = EXCLUDED.display_name,
    polarity     = EXCLUDED.polarity,
    sort_order   = EXCLUDED.sort_order;

-- Defaults mirror Relay.Domain.ThresholdSet.Defaults (default_comparison_weeks = 8 per PLAN.md's
-- /meta example; every other figure is the audit-calibrated value from the same record).
--
-- min_week_completeness is a LITERAL, not `6.0 / 7`: Postgres's numeric division rounds that
-- expression to 0.85714285714285714286 (20 fractional digits, rounded up at the last one), which
-- is fractionally GREATER than the true 6/7 and than C#'s `6m / 7m` (0.8571428571428571428571428571,
-- computed to decimal's full 28-digit precision). A location-week with exactly 6-of-7 days present
-- has completeness == 6/7 exactly, and StatusEvaluator's `completeness < MinWeekCompleteness` is
-- meant to treat that as AT the floor (included, not excluded) — but with the rounded-up SQL value
-- as the seeded threshold, `6/7 < 0.85714285714285714286` is true, so every exactly-6-of-7 week in
-- the account's history would be wrongly excluded as PartialWeek. The literal below matches C#'s
-- value digit-for-digit so the seeded threshold and the in-process default can never disagree at
-- this boundary.
-- DO NOTHING, not DO UPDATE: unlike the catalogs above, this table is customer-tunable
-- (Requirements §Architecture — "lets the customer own the definition of 'normal'"). A DO UPDATE
-- here would silently reset a customer's tuned thresholds back to the seed defaults on every
-- migration re-run, which is exactly the kind of quiet drift the idempotency requirement is
-- supposed to prevent, not cause. Only a row that doesn't exist yet gets the seed default.
INSERT INTO account_dashboard_settings (
    account_id, default_comparison_weeks, tolerance_pct, min_baseline_events,
    min_rate_denominator, min_history_weeks, min_week_completeness, amber_fraction
)
SELECT a.id, 8, 40, 5, 20, 4, 0.8571428571428571428571428571, 0.8
FROM accounts a
ON CONFLICT (account_id) DO NOTHING;

-- The D1 backfill artifact (audit 2026-08-20): account-scoped, not global — 2026-06-03 carries
-- 882 events overall but only 805 belong to Metro Collision Centers' replay. A global row would
-- delete 77 legitimate rows from other accounts and break the 805-row assertion (PLAN.md Open
-- Question 9). `id` is a surrogate identity column with no natural key to conflict on, so
-- idempotency here is a NOT EXISTS guard rather than ON CONFLICT.
INSERT INTO data_quality_exclusions (account_id, location, event_type, from_date, to_date, reason)
SELECT 6, NULL, NULL, DATE '2026-06-03', DATE '2026-06-03',
       'D1 · replayed bulk backfill (audit 2026-08-20)'
WHERE NOT EXISTS (
    SELECT 1 FROM data_quality_exclusions
    WHERE account_id = 6
      AND location IS NULL
      AND event_type IS NULL
      AND from_date = DATE '2026-06-03'
      AND to_date = DATE '2026-06-03'
);
