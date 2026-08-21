-- Never index `location` alone (Requirements: "Site A" belongs to 19 accounts — a bare index on
-- location silently blends unrelated businesses). Both indexes are account-scoped for that reason.
CREATE INDEX IF NOT EXISTS ix_activity_events_account_id_occurred_at
    ON activity_events (account_id, occurred_at);

CREATE INDEX IF NOT EXISTS ix_activity_events_account_id_location_event_type_occurred_at
    ON activity_events (account_id, location, event_type, occurred_at);
