BEGIN;
-- Card history follows the stable Card identity across source Boards.
CREATE INDEX ix_work_events_card_history ON work_events(tenant_id,entity_id,created_at DESC,event_id DESC)
 WHERE entity_type='Card';
CREATE INDEX ix_work_events_watch_history ON work_events(tenant_id,watch_subscription_id,created_at DESC,event_id DESC)
 WHERE watch_subscription_id IS NOT NULL;
CREATE INDEX ix_work_events_reminder_history ON work_events(tenant_id,reminder_id,created_at DESC,event_id DESC)
 WHERE reminder_id IS NOT NULL;
INSERT INTO schema_migrations(version) VALUES('065_activity_history_indexes');
COMMIT;
