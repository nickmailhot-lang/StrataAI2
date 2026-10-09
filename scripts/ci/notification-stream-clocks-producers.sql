-- Appended before the unchanged source-admission fixture's final rollback.
-- Real parent INSERT/read triggers allocate all journal entries; no clock or
-- journal row is fabricated here. The temporary read grant rolls back too.
GRANT UPDATE(read_at) ON card_assignment_notifications TO strataai_api_runtime;
SET LOCAL ROLE strataai_api_runtime;
SELECT set_config('app.tenant_id','02100000-0000-0000-0000-000000000011',true);
UPDATE card_assignment_notifications SET read_at=created_at+interval '1 day'
 WHERE recipient_id IN ('11100000-0000-0000-0000-000000000001','11100000-0000-0000-0000-000000000002','11100000-0000-0000-0000-000000000003');
DO $$ DECLARE original_streams jsonb; original_events jsonb;
BEGIN
 SELECT jsonb_agg(to_jsonb(s) ORDER BY recipient_id) INTO original_streams FROM notification_event_streams s;
 SELECT jsonb_agg(to_jsonb(e) ORDER BY recipient_id,sequence) INTO original_events FROM notification_events e;
 UPDATE card_assignment_notifications SET read_at=read_at
  WHERE recipient_id IN ('11100000-0000-0000-0000-000000000001','11100000-0000-0000-0000-000000000002','11100000-0000-0000-0000-000000000003');
 IF original_streams IS DISTINCT FROM (SELECT jsonb_agg(to_jsonb(s) ORDER BY recipient_id) FROM notification_event_streams s)
 OR original_events IS DISTINCT FROM (SELECT jsonb_agg(to_jsonb(e) ORDER BY recipient_id,sequence) FROM notification_events e) THEN
  RAISE EXCEPTION 'Notification read replay changed journal or clocks';
 END IF;
 BEGIN
  UPDATE card_assignment_notifications SET read_at=read_at+interval '1 second' WHERE recipient_id='11100000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Notification first read replacement admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
RESET ROLE;
SET CONSTRAINTS ALL IMMEDIATE;
DO $$ BEGIN
 IF (SELECT count(*) FROM notification_event_streams WHERE recipient_id IN
  ('11100000-0000-0000-0000-000000000001','11100000-0000-0000-0000-000000000002','11100000-0000-0000-0000-000000000003'))<>3
 OR EXISTS(SELECT 1 FROM notification_event_streams s WHERE
  s.created_at IS DISTINCT FROM (SELECT created_at FROM notification_events WHERE tenant_id=s.tenant_id AND recipient_id=s.recipient_id AND sequence=1)
  OR s.updated_at IS DISTINCT FROM (SELECT max(created_at) FROM notification_events WHERE tenant_id=s.tenant_id AND recipient_id=s.recipient_id)
  OR s.last_sequence IS DISTINCT FROM (SELECT count(*) FROM notification_events WHERE tenant_id=s.tenant_id AND recipient_id=s.recipient_id))
 OR EXISTS(SELECT 1 FROM card_assignment_notifications n WHERE recipient_id IN
  ('11100000-0000-0000-0000-000000000001','11100000-0000-0000-0000-000000000002','11100000-0000-0000-0000-000000000003')
  AND (read_at IS NULL OR NOT EXISTS(SELECT 1 FROM notification_events e WHERE e.tenant_id=n.tenant_id
   AND e.notification_id=n.id AND e.event_type='NOTIFICATION_READ' AND e.created_at=n.read_at))) THEN
  RAISE EXCEPTION 'Notification producers did not retain exact source clocks and read facts';
 END IF;
 IF EXISTS(SELECT 1 FROM (VALUES('strataai_api_runtime'),('strataai_worker_runtime')) r(role_name)
  WHERE has_function_privilege(r.role_name,'refresh_notification_stream_clocks()','EXECUTE')
   OR has_function_privilege(r.role_name,'enforce_notification_stream_clocks()','EXECUTE')) THEN
  RAISE EXCEPTION 'Runtime received private notification clock execution';
 END IF;
END $$;
