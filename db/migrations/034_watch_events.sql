BEGIN;
ALTER TABLE work_events DROP CONSTRAINT work_events_entity_type_check;
ALTER TABLE work_events ADD CONSTRAINT work_events_entity_type_check
    CHECK(entity_type IN ('Board','List','Card','Label','WatchSubscription'));
ALTER TABLE work_events ADD CONSTRAINT work_events_watch_type_check
    CHECK((entity_type='WatchSubscription')=(event_type IN ('WATCH_CREATED','WATCH_REMOVED')));
ALTER TABLE work_events ADD COLUMN watch_subscription_id uuid
    GENERATED ALWAYS AS (CASE WHEN entity_type='WatchSubscription' THEN entity_id END) STORED;
ALTER TABLE work_events ADD CONSTRAINT work_events_watch_scope_fk
    FOREIGN KEY(tenant_id,watch_subscription_id) REFERENCES watch_subscriptions(tenant_id,id) ON DELETE RESTRICT;
CREATE INDEX ix_work_events_watch_reference ON work_events(tenant_id,watch_subscription_id) WHERE watch_subscription_id IS NOT NULL;
INSERT INTO schema_migrations(version) VALUES('034_watch_events');
COMMIT;
