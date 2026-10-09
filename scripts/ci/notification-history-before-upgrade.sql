BEGIN;
CREATE TABLE notification_history_upgrade_fixture AS
 SELECT to_jsonb(e) AS body FROM notification_events e;
DO $$ DECLARE affected integer; BEGIN
 IF NOT EXISTS(SELECT FROM notification_history_upgrade_fixture) THEN
  RAISE EXCEPTION 'Notification history upgrade fixture is empty';
 END IF;
 -- Demonstrate the pre-upgrade gap without retaining a fabricated fact.
 BEGIN
  UPDATE notification_events SET created_at=created_at+interval '1 microsecond'
   WHERE event_id=(SELECT event_id FROM notification_events ORDER BY event_id LIMIT 1);
  GET DIAGNOSTICS affected=ROW_COUNT;
  IF affected<>1 THEN RAISE EXCEPTION 'Notification clock mutation baseline did not execute'; END IF;
  RAISE no_data_found;
 EXCEPTION WHEN no_data_found THEN NULL; END;
 BEGIN
  DELETE FROM notification_events WHERE event_id=(SELECT event_id FROM notification_events ORDER BY event_id LIMIT 1);
  GET DIAGNOSTICS affected=ROW_COUNT;
  IF affected<>1 THEN RAISE EXCEPTION 'Notification deletion baseline did not execute'; END IF;
  RAISE no_data_found;
 EXCEPTION WHEN no_data_found THEN NULL; END;
END $$;
COMMIT;
