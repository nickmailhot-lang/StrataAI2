BEGIN;
CREATE TABLE notification_stream_clock_upgrade_fixture AS SELECT * FROM notification_event_streams;
CREATE TABLE notification_event_clock_upgrade_fixture AS SELECT * FROM notification_events;
CREATE TABLE notification_body_clock_upgrade_fixture AS SELECT * FROM card_assignment_notifications;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM notification_events) THEN RAISE EXCEPTION 'Notification clock history fixture is empty'; END IF;
END $$;
UPDATE notification_event_streams SET last_sequence=last_sequence+1
 WHERE (tenant_id,recipient_id)=(SELECT tenant_id,recipient_id FROM notification_stream_clock_upgrade_fixture ORDER BY tenant_id,recipient_id LIMIT 1);
COMMIT;
