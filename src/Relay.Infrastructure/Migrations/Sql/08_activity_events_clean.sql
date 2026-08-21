-- Dedupe (D4) + data-quality exclusions (D1) + location_id join. duration_seconds is dropped at
-- the view, deliberately — the cheapest way to make "appears in no response payload anywhere"
-- true by construction rather than by vigilance (Requirements §Data-quality note D3).
CREATE OR REPLACE VIEW activity_events_clean AS
SELECT DISTINCT ON (l.account_id, l.location, l.event_type, l.occurred_at_utc)
       l.id, l.account_id, loc.id AS location_id, l.location, l.event_type,
       l.occurred_at_utc, l.occurred_at_local, l.local_date, l.outcome
FROM   activity_events_local l
JOIN   locations loc ON loc.account_id = l.account_id AND loc.name = l.location
WHERE  NOT EXISTS (
         SELECT 1 FROM data_quality_exclusions x
         WHERE (x.account_id IS NULL OR x.account_id = l.account_id)
           AND (x.location   IS NULL OR x.location   = l.location)
           AND (x.event_type IS NULL OR x.event_type = l.event_type)
           AND l.local_date BETWEEN x.from_date AND x.to_date)
ORDER BY l.account_id, l.location, l.event_type, l.occurred_at_utc, l.id;
