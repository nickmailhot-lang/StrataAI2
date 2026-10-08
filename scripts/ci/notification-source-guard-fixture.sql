-- The populated migration-runner fixture has a Card event, Reminder event and
-- an unrelated same-Board Card. All additional publication effects roll back.
BEGIN;
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 SELECT ('11100000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid,
 'source-guard-'||n||'@example.test',upper('source-guard-'||n||'@example.test'),
 'Source guard fixture','ACTIVE','unused-fixture',now(),now() FROM generate_series(1,3) n;
INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),'02100000-0000-0000-0000-000000000011',id,'MEMBER','ACTIVE'
 FROM users WHERE email LIKE 'source-guard-%@example.test';
-- The migration-runner database deliberately has no application provisioning.
-- Grant only the normal publication/read capabilities needed by this fixture.
GRANT SELECT,INSERT ON card_assignment_notifications TO strataai_api_runtime;
GRANT SELECT ON work_events,card_reminders,notification_events,notification_event_streams TO strataai_api_runtime;
SET LOCAL ROLE strataai_api_runtime;
SELECT set_config('app.tenant_id','02100000-0000-0000-0000-000000000011',true);
DO $$
DECLARE before_notifications bigint; before_events bigint; before_streams jsonb;
BEGIN
 SELECT count(*) INTO before_notifications FROM card_assignment_notifications;
 SELECT count(*) INTO before_events FROM notification_events;
 SELECT jsonb_agg(to_jsonb(s) ORDER BY recipient_id) INTO before_streams FROM notification_event_streams s;
 -- A late invalid row must reject the whole statement, including the valid
 -- row's previously executed private journal and sequence increments.
 BEGIN
  INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
  SELECT e.tenant_id,gen_random_uuid(),e.board_id,
   CASE WHEN n=1 THEN e.entity_id ELSE '06600000-0000-0000-0000-000000000099'::uuid END,
   e.event_id,('11100000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid,e.actor_id,e.entity_version,e.created_at,e.event_type
  FROM work_events e CROSS JOIN generate_series(1,2) n WHERE e.event_id='06600000-0000-0000-0000-000000000091' ORDER BY n;
  RAISE EXCEPTION 'Mixed source statement was accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 IF (SELECT count(*) FROM card_assignment_notifications)<>before_notifications
  OR (SELECT count(*) FROM notification_events)<>before_events
  OR (SELECT jsonb_agg(to_jsonb(s) ORDER BY recipient_id) FROM notification_event_streams s) IS DISTINCT FROM before_streams
 THEN RAISE EXCEPTION 'Rejected source statement retained publication effects'; END IF;
 -- Valid batches and exact ON CONFLICT replay retain one journal per intent.
 FOR sample IN 1..2 LOOP
  INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
  SELECT e.tenant_id,gen_random_uuid(),e.board_id,e.entity_id,e.event_id,
   ('11100000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid,e.actor_id,e.entity_version,e.created_at,e.event_type
  FROM work_events e CROSS JOIN generate_series(1,2) n WHERE e.event_id='06600000-0000-0000-0000-000000000091'
  ORDER BY n ON CONFLICT(tenant_id,event_id,recipient_id) DO NOTHING;
 END LOOP;
 IF (SELECT count(*) FROM card_assignment_notifications)<>before_notifications+2
  OR (SELECT count(*) FROM notification_events)<>before_events+2
 THEN RAISE EXCEPTION 'Valid source batch/replay did not retain exact effects'; END IF;
 BEGIN
  UPDATE card_assignment_notifications SET card_id='06600000-0000-0000-0000-000000000099'
   WHERE recipient_id='11100000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Historical Card source update was accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 -- Reminder source identity is the Reminder ID, never the Card ID.
 BEGIN
  INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
  SELECT e.tenant_id,gen_random_uuid(),e.board_id,'06600000-0000-0000-0000-000000000099',e.event_id,
   '11100000-0000-0000-0000-000000000003',e.actor_id,e.entity_version,e.created_at,e.event_type
  FROM work_events e WHERE e.event_id='06600000-0000-0000-0000-000000000093';
  RAISE EXCEPTION 'Wrong Reminder source Card was accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
 SELECT e.tenant_id,gen_random_uuid(),e.board_id,r.card_id,e.event_id,
  '11100000-0000-0000-0000-000000000003',e.actor_id,e.entity_version,e.created_at,e.event_type
 FROM work_events e JOIN card_reminders r ON r.tenant_id=e.tenant_id AND r.id=e.entity_id
 WHERE e.event_id='06600000-0000-0000-0000-000000000093';
 IF (SELECT count(*) FROM card_assignment_notifications)<>before_notifications+3
  OR (SELECT count(*) FROM notification_events)<>before_events+3
 THEN RAISE EXCEPTION 'Reminder source batch did not retain exact effects'; END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 BEGIN
  UPDATE card_assignment_notifications SET card_id='06600000-0000-0000-0000-000000000099'
   WHERE recipient_id='11100000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Privileged historical Card source update was accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
END $$;
ROLLBACK;
