DO $$ DECLARE mutation text; refused boolean; BEGIN
 IF (SELECT jsonb_agg(body ORDER BY body->>'event_id') FROM notification_history_upgrade_fixture)
  IS DISTINCT FROM (SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id::text) FROM notification_events e) THEN
  RAISE EXCEPTION 'Notification history upgrade changed retained facts';
 END IF;
 FOREACH mutation IN ARRAY ARRAY[
  'UPDATE notification_events SET created_at=created_at+interval ''1 microsecond''',
  'UPDATE notification_events SET event_id=gen_random_uuid()',
  'UPDATE notification_events SET metadata=metadata',
  'DELETE FROM notification_events'] LOOP
  refused:=false;
  BEGIN EXECUTE mutation; EXCEPTION WHEN check_violation THEN refused:=true; END;
  IF NOT refused THEN RAISE EXCEPTION 'Notification retained history mutation was admitted'; END IF;
 END LOOP;
 IF (SELECT jsonb_agg(body ORDER BY body->>'event_id') FROM notification_history_upgrade_fixture)
  IS DISTINCT FROM (SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id::text) FROM notification_events e) THEN
  RAISE EXCEPTION 'Refused notification history mutation changed retained facts';
 END IF;
END $$;
DROP TABLE notification_history_upgrade_fixture;
