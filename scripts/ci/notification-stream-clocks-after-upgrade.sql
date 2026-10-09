DO $$ BEGIN
 IF (SELECT count(*) FROM notification_stream_clock_upgrade_fixture)<>(SELECT count(*) FROM notification_event_streams)
 OR EXISTS(SELECT 1 FROM notification_stream_clock_upgrade_fixture f LEFT JOIN notification_event_streams s USING(tenant_id,recipient_id)
  WHERE s.tenant_id IS NULL OR to_jsonb(s)-'created_at'-'updated_at' IS DISTINCT FROM to_jsonb(f)
   OR s.created_at IS DISTINCT FROM (SELECT created_at FROM notification_events WHERE tenant_id=s.tenant_id AND recipient_id=s.recipient_id AND sequence=1)
   OR s.updated_at IS DISTINCT FROM (SELECT max(created_at) FROM notification_events WHERE tenant_id=s.tenant_id AND recipient_id=s.recipient_id))
 OR (SELECT jsonb_agg(to_jsonb(e) ORDER BY tenant_id,recipient_id,sequence) FROM notification_events e)
  IS DISTINCT FROM (SELECT jsonb_agg(to_jsonb(e) ORDER BY tenant_id,recipient_id,sequence) FROM notification_event_clock_upgrade_fixture e)
 OR (SELECT jsonb_agg(to_jsonb(n) ORDER BY tenant_id,id) FROM card_assignment_notifications n)
  IS DISTINCT FROM (SELECT jsonb_agg(to_jsonb(n) ORDER BY tenant_id,id) FROM notification_body_clock_upgrade_fixture n) THEN
  RAISE EXCEPTION 'Notification clock upgrade changed retained history';
 END IF;
END $$;
BEGIN;
DO $$ DECLARE scope record; original jsonb;
BEGIN
 SELECT tenant_id,recipient_id INTO STRICT scope FROM notification_event_streams ORDER BY tenant_id,recipient_id LIMIT 1;
 SELECT to_jsonb(s) INTO original FROM notification_event_streams s WHERE tenant_id=scope.tenant_id AND recipient_id=scope.recipient_id;
 BEGIN
  UPDATE notification_event_streams SET created_at=created_at-interval '1 second' WHERE tenant_id=scope.tenant_id AND recipient_id=scope.recipient_id;
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Notification creation clock replacement admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE notification_event_streams SET updated_at=updated_at+interval '100 years' WHERE tenant_id=scope.tenant_id AND recipient_id=scope.recipient_id;
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Notification update clock replacement admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE notification_event_streams SET last_sequence=last_sequence+1 WHERE tenant_id=scope.tenant_id AND recipient_id=scope.recipient_id;
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Notification counter without source admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE notification_event_streams SET updated_at='infinity' WHERE tenant_id=scope.tenant_id AND recipient_id=scope.recipient_id;
  RAISE EXCEPTION 'Notification infinite clock admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 UPDATE notification_event_streams SET last_sequence=last_sequence WHERE tenant_id=scope.tenant_id AND recipient_id=scope.recipient_id;
 SET CONSTRAINTS ALL IMMEDIATE;
 IF original IS DISTINCT FROM (SELECT to_jsonb(s) FROM notification_event_streams s WHERE tenant_id=scope.tenant_id AND recipient_id=scope.recipient_id) THEN
  RAISE EXCEPTION 'Notification no-op changed history';
 END IF;
END $$;
ROLLBACK;
DROP TABLE notification_stream_clock_upgrade_fixture,notification_event_clock_upgrade_fixture,notification_body_clock_upgrade_fixture;
