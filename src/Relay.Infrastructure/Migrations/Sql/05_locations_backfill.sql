-- 69 distinct (account_id, location) pairs in activity_events (PLAN.md Verified facts).
-- opened_on/closed_on stay NULL — nothing in the seed says otherwise.
INSERT INTO locations (account_id, name)
SELECT DISTINCT account_id, location
FROM activity_events
ON CONFLICT (account_id, name) DO NOTHING;
