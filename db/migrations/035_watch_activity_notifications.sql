BEGIN;
-- Keep the existing recipient store, RLS, API column grants and dedupe tuple.
-- The historical table name no longer restricts it to assignment activity.
ALTER TABLE card_assignment_notifications DROP CONSTRAINT card_assignment_notifications_notification_type_check;
ALTER TABLE card_assignment_notifications ADD CONSTRAINT card_assignment_notifications_notification_type_check
 CHECK(notification_type IN ('CARD_ASSIGNED','CARD_CREATED','CARD_UPDATED','CARD_MOVED','CARD_ARCHIVED','CARD_RESTORED',
   'CARD_MEMBER_ADDED','CARD_MEMBER_REMOVED','LABEL_ADDED','LABEL_REMOVED'));
ALTER TABLE work_events ADD CONSTRAINT uq_work_events_notification_source UNIQUE(tenant_id,board_id,event_id,event_type);
ALTER TABLE card_assignment_notifications ADD COLUMN source_event_type text
 GENERATED ALWAYS AS (CASE WHEN notification_type='CARD_ASSIGNED' THEN 'CARD_MEMBER_ADDED' ELSE notification_type END) STORED;
ALTER TABLE card_assignment_notifications ADD CONSTRAINT notification_activity_source_fk
 FOREIGN KEY(tenant_id,board_id,event_id,source_event_type)
 REFERENCES work_events(tenant_id,board_id,event_id,event_type) ON DELETE RESTRICT;
INSERT INTO schema_migrations(version) VALUES('035_watch_activity_notifications');
COMMIT;
