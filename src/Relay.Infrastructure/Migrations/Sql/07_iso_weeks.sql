-- The week spine, built from the min/max local_date across ALL activity (global, not
-- per-account — PLAN.md Open Question 7: a per-account range would mark a quiet account's
-- genuinely low early weeks as "partial"). date_trunc('week', ·) truncates to Monday, which both
-- matches the ISO week convention and is why this spine is gapless across
-- [firstWeek, latestWeekWithData] by construction — EfDashboardReader's gap guard should never
-- fire against this seed.
INSERT INTO iso_weeks (week_start, week_end, iso_year, iso_week_number)
SELECT
    gs::date                      AS week_start,
    (gs + INTERVAL '6 day')::date AS week_end,
    extract(isoyear FROM gs)::int AS iso_year,
    extract(week    FROM gs)::int AS iso_week_number
FROM generate_series(
    (SELECT date_trunc('week', min(local_date)) FROM activity_events_local),
    (SELECT date_trunc('week', max(local_date)) FROM activity_events_local),
    INTERVAL '7 day'
) AS gs
ON CONFLICT (week_start) DO NOTHING;
