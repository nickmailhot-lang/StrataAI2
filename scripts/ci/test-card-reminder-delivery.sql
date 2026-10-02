\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_reminder_delivery_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_reminder_delivery_ci;
GRANT EXECUTE ON FUNCTION public.deliver_card_reminder(uuid,uuid,uuid,uuid,uuid,uuid,bigint,boolean) TO strataai_reminder_delivery_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('03800000-0000-0000-0000-000000000001','Reminder A',now(),now()),
 ('03800000-0000-0000-0000-000000000002','Reminder B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 SELECT id,'reminder-'||id||'@example.test',upper('reminder-'||id||'@example.test'),'Reminder fixture','ACTIVE',true,'unused-reminder-hash',now(),now()
 FROM unnest(ARRAY['03800000-0000-0000-0000-000000000041','03800000-0000-0000-0000-000000000042']::uuid[]) ids(id);
INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'03800000-0000-0000-0000-000000000001','03800000-0000-0000-0000-000000000041','MEMBER','ACTIVE'),
 (gen_random_uuid(),'03800000-0000-0000-0000-000000000002','03800000-0000-0000-0000-000000000042','MEMBER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('03800000-0000-0000-0000-000000000011','03800000-0000-0000-0000-000000000001','Reminder private Board',now(),now());
INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES
 (gen_random_uuid(),'03800000-0000-0000-0000-000000000001','03800000-0000-0000-0000-000000000011',
 '03800000-0000-0000-0000-000000000041','MEMBER','ACTIVE',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('03800000-0000-0000-0000-000000000021','03800000-0000-0000-0000-000000000001',
 '03800000-0000-0000-0000-000000000011','Reminder List','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at,due_at,due_timezone) VALUES
 ('03800000-0000-0000-0000-000000000031','03800000-0000-0000-0000-000000000001','03800000-0000-0000-0000-000000000011',
 '03800000-0000-0000-0000-000000000021','Reminder Card','500000000000000000000000000000',now(),now(),now()-interval '1 minute','UTC');
INSERT INTO card_reminders(tenant_id,id,user_id,card_id,interval_code,enabled,due_at,trigger_at,status,generation,created_at,updated_at,version)
 SELECT tenant_id,'03800000-0000-0000-0000-000000000051','03800000-0000-0000-0000-000000000041',id,'AT_DUE',true,
 due_at,due_at,'SCHEDULED',1,now(),now(),1 FROM cards WHERE id='03800000-0000-0000-0000-000000000031';
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,
 state,attempt_count,worker_id,lease_id,lease_expires_at) VALUES
 ('03800000-0000-0000-0000-000000000061','03800000-0000-0000-0000-000000000001','CARD_REMINDER',
 'card-reminder/03800000000000000000000000000051/1','03800000-0000-0000-0000-000000000041','card-reminder-delivery','reminder-sql-test',
 '{"reminderId":"03800000-0000-0000-0000-000000000051","generation":1}', 'RUNNING',1,
 '03800000-0000-0000-0000-000000000081','03800000-0000-0000-0000-000000000071',clock_timestamp()+interval '2 minutes');

CREATE FUNCTION pg_temp.attempt_reminder(expected text, generation bigint DEFAULT 1,
 worker uuid DEFAULT '03800000-0000-0000-0000-000000000081',
 lease uuid DEFAULT '03800000-0000-0000-0000-000000000071', verified boolean DEFAULT true) RETURNS void LANGUAGE plpgsql AS $$
DECLARE actual text;
BEGIN
 actual=public.deliver_card_reminder('03800000-0000-0000-0000-000000000061','03800000-0000-0000-0000-000000000001',
  '03800000-0000-0000-0000-000000000041',worker,lease,'03800000-0000-0000-0000-000000000051',generation,verified);
 IF actual IS DISTINCT FROM expected THEN RAISE EXCEPTION 'Reminder result %, expected %',actual,expected; END IF;
END $$;
CREATE FUNCTION pg_temp.no_reminder_effects() RETURNS void LANGUAGE plpgsql AS $$ BEGIN
 IF EXISTS(SELECT 1 FROM work_events WHERE tenant_id='03800000-0000-0000-0000-000000000001') OR
  EXISTS(SELECT 1 FROM card_assignment_notifications WHERE tenant_id='03800000-0000-0000-0000-000000000001') OR
  EXISTS(SELECT 1 FROM audit_events WHERE tenant_id='03800000-0000-0000-0000-000000000001') OR
  EXISTS(SELECT 1 FROM work_event_streams WHERE tenant_id='03800000-0000-0000-0000-000000000001' AND last_sequence<>0) OR
  EXISTS(SELECT 1 FROM card_reminders WHERE tenant_id='03800000-0000-0000-0000-000000000001' AND status='FIRED')
  THEN RAISE EXCEPTION 'Rejected Reminder left effects'; END IF;
END $$;

SET LOCAL ROLE strataai_reminder_delivery_ci;
SELECT set_config('app.tenant_id','',true);
SELECT pg_temp.attempt_reminder('LEASE_LOST');
SELECT set_config('app.tenant_id','03800000-0000-0000-0000-000000000002',true);
SELECT pg_temp.attempt_reminder('LEASE_LOST');
SELECT set_config('app.tenant_id','03800000-0000-0000-0000-000000000001',true);
SELECT pg_temp.attempt_reminder('LEASE_LOST',1,'03800000-0000-0000-0000-000000000082');
SELECT pg_temp.attempt_reminder('LEASE_LOST',1,'03800000-0000-0000-0000-000000000081','03800000-0000-0000-0000-000000000072');
SELECT pg_temp.attempt_reminder('LEASE_LOST',2);
DO $$ BEGIN
 BEGIN PERFORM id FROM cards; RAISE EXCEPTION 'Worker read Cards' USING ERRCODE='check_violation'; EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN PERFORM id FROM users; RAISE EXCEPTION 'Worker read accounts' USING ERRCODE='check_violation'; EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN DELETE FROM card_reminders; RAISE EXCEPTION 'Worker erased Reminders' USING ERRCODE='check_violation'; EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
RESET ROLE;
SELECT pg_temp.no_reminder_effects();

-- Change current canonical state AFTER the job exists. No snapshot authority.
DO $$
DECLARE mutation text;
BEGIN
 FOREACH mutation IN ARRAY ARRAY[
  'UPDATE organization_members SET status=''REMOVED'' WHERE tenant_id=''03800000-0000-0000-0000-000000000001''',
  'UPDATE board_members SET status=''REMOVED'' WHERE tenant_id=''03800000-0000-0000-0000-000000000001''',
  'DELETE FROM board_members WHERE tenant_id=''03800000-0000-0000-0000-000000000001''',
  'UPDATE users SET status=''SUSPENDED'' WHERE id=''03800000-0000-0000-0000-000000000041''',
  'UPDATE users SET email_verified=false WHERE id=''03800000-0000-0000-0000-000000000041''',
  'UPDATE organizations SET status=''ARCHIVED'' WHERE id=''03800000-0000-0000-0000-000000000001''',
  'UPDATE boards SET lifecycle_state=''ARCHIVED'' WHERE id=''03800000-0000-0000-0000-000000000011''',
  'UPDATE board_lists SET lifecycle_state=''ARCHIVED'' WHERE id=''03800000-0000-0000-0000-000000000021''',
  'UPDATE cards SET lifecycle_state=''ARCHIVED'' WHERE id=''03800000-0000-0000-0000-000000000031''',
  'UPDATE cards SET due_complete=true WHERE id=''03800000-0000-0000-0000-000000000031''',
  'UPDATE cards SET due_at=due_at+interval ''1 minute'' WHERE id=''03800000-0000-0000-0000-000000000031''',
  'UPDATE card_reminders SET generation=2,version=2 WHERE id=''03800000-0000-0000-0000-000000000051''',
  'UPDATE card_reminders SET enabled=false,status=''CANCELLED'',due_at=NULL,trigger_at=NULL,generation=2,version=2 WHERE id=''03800000-0000-0000-0000-000000000051'''
 ] LOOP
  BEGIN
   EXECUTE mutation;
   EXECUTE 'SET LOCAL ROLE strataai_reminder_delivery_ci';
   PERFORM pg_temp.attempt_reminder('SUPERSEDED');
   EXECUTE 'RESET ROLE';
   PERFORM pg_temp.no_reminder_effects();
   -- A deliberate subtransaction rollback restores the canonical fixture.
   RAISE EXCEPTION 'rollback fixture' USING ERRCODE='serialization_failure';
  EXCEPTION WHEN serialization_failure THEN NULL;
  END;
 END LOOP;
END $$;

UPDATE background_jobs SET safe_metadata=safe_metadata||'{"recipientId":"forged"}' WHERE id='03800000-0000-0000-0000-000000000061';
SET LOCAL ROLE strataai_reminder_delivery_ci;
SELECT pg_temp.attempt_reminder('LEASE_LOST');
RESET ROLE;
UPDATE background_jobs SET safe_metadata='{"reminderId":"03800000-0000-0000-0000-000000000051","generation":1}',
 lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='03800000-0000-0000-0000-000000000061';
SET LOCAL ROLE strataai_reminder_delivery_ci;
SELECT pg_temp.attempt_reminder('LEASE_LOST');
RESET ROLE;
UPDATE background_jobs SET lease_expires_at=clock_timestamp()+interval '2 minutes' WHERE id='03800000-0000-0000-0000-000000000061';
SELECT pg_temp.no_reminder_effects();

-- A future deadline cannot fire through a forged early claim.
SAVEPOINT future_due;
UPDATE cards SET due_at=clock_timestamp()+interval '1 day' WHERE id='03800000-0000-0000-0000-000000000031';
UPDATE card_reminders SET due_at=c.due_at,trigger_at=c.due_at FROM cards c
 WHERE card_reminders.card_id=c.id AND card_reminders.tenant_id='03800000-0000-0000-0000-000000000001';
SET LOCAL ROLE strataai_reminder_delivery_ci;
SELECT pg_temp.attempt_reminder('LEASE_LOST');
RESET ROLE;
SELECT pg_temp.no_reminder_effects();
ROLLBACK TO future_due;

-- Notification failure rolls back event/audit/revision/sequence allocation too.
CREATE FUNCTION pg_temp.fail_reminder_notification() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NEW.notification_type='REMINDER_FIRED' THEN RAISE EXCEPTION 'fixture notification failure'; END IF; RETURN NEW;
END $$;
CREATE TRIGGER reminder_notification_failure BEFORE INSERT ON card_assignment_notifications
 FOR EACH ROW EXECUTE FUNCTION pg_temp.fail_reminder_notification();
SET LOCAL ROLE strataai_reminder_delivery_ci;
DO $$ BEGIN
 BEGIN
  PERFORM pg_temp.attempt_reminder('DELIVERED');
  RAISE EXCEPTION 'Failed notification was accepted' USING ERRCODE='check_violation';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM<>'fixture notification failure' THEN RAISE; END IF;
 END;
END $$;
RESET ROLE;
DROP TRIGGER reminder_notification_failure ON card_assignment_notifications;
SELECT pg_temp.no_reminder_effects();

-- Lease expiry DURING effect production must roll back every tentative effect.
CREATE FUNCTION pg_temp.delay_reminder_notification() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
 IF NEW.notification_type='REMINDER_FIRED' THEN
  UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()+interval '0.01 second' WHERE id=NEW.event_id AND tenant_id=NEW.tenant_id;
  PERFORM pg_sleep(0.02);
 END IF; RETURN NEW;
END $$;
CREATE TRIGGER reminder_notification_delay BEFORE INSERT ON card_assignment_notifications
 FOR EACH ROW EXECUTE FUNCTION pg_temp.delay_reminder_notification();
UPDATE background_jobs SET lease_expires_at=clock_timestamp()+interval '2 minutes' WHERE id='03800000-0000-0000-0000-000000000061';
SET LOCAL ROLE strataai_reminder_delivery_ci;
DO $$ BEGIN
 BEGIN
  PERFORM pg_temp.attempt_reminder('DELIVERED');
  RAISE EXCEPTION 'Expired effect was accepted' USING ERRCODE='check_violation';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM<>'Reminder lease fence failed' THEN RAISE; END IF;
 END;
END $$;
RESET ROLE;
DROP TRIGGER reminder_notification_delay ON card_assignment_notifications;
SELECT pg_temp.no_reminder_effects();
UPDATE background_jobs SET lease_expires_at=clock_timestamp()+interval '2 minutes' WHERE id='03800000-0000-0000-0000-000000000061';
SET LOCAL ROLE strataai_reminder_delivery_ci;
SELECT pg_temp.attempt_reminder('DELIVERED');
SELECT pg_temp.attempt_reminder('DELIVERED');
RESET ROLE;
-- A later process recovering the same committed effect gets no second event.
UPDATE background_jobs SET worker_id='03800000-0000-0000-0000-000000000082',
 lease_id='03800000-0000-0000-0000-000000000072',lease_expires_at=clock_timestamp()+interval '2 minutes'
 WHERE id='03800000-0000-0000-0000-000000000061';
SET LOCAL ROLE strataai_reminder_delivery_ci;
SELECT pg_temp.attempt_reminder('LEASE_LOST');
SELECT pg_temp.attempt_reminder('DELIVERED',1,'03800000-0000-0000-0000-000000000082','03800000-0000-0000-0000-000000000072');
RESET ROLE;
DO $$ BEGIN
 IF (SELECT count(*) FROM work_events WHERE tenant_id='03800000-0000-0000-0000-000000000001')<>1 OR
  (SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='03800000-0000-0000-0000-000000000001')<>1 OR
  (SELECT count(*) FROM audit_events WHERE tenant_id='03800000-0000-0000-0000-000000000001')<>1 OR
  NOT EXISTS(SELECT 1 FROM card_reminders WHERE id='03800000-0000-0000-0000-000000000051' AND status='FIRED' AND generation=1 AND version=2) OR
  NOT EXISTS(SELECT 1 FROM work_event_streams WHERE tenant_id='03800000-0000-0000-0000-000000000001' AND last_sequence=1) OR
  NOT EXISTS(SELECT 1 FROM work_events WHERE event_id='03800000-0000-0000-0000-000000000061'
    AND entity_type='Reminder' AND event_type='REMINDER_FIRED' AND entity_version=2 AND ready_at IS NOT NULL) OR
  NOT EXISTS(SELECT 1 FROM card_assignment_notifications WHERE id='03800000-0000-0000-0000-000000000061'
    AND notification_type='REMINDER_FIRED' AND actor_id=recipient_id AND card_version=1) OR
  NOT EXISTS(SELECT 1 FROM background_jobs WHERE id='03800000-0000-0000-0000-000000000061' AND state='RUNNING') OR
  EXISTS(SELECT 1 FROM pg_proc p CROSS JOIN LATERAL aclexplode(p.proacl) a
    WHERE p.proname='deliver_card_reminder' AND a.grantee=0 AND a.privilege_type='EXECUTE')
  THEN RAISE EXCEPTION 'Reminder atomic/deduplicated/private delivery failed'; END IF;
END $$;
ROLLBACK;
\echo 'Reminder delivery: restricted Worker, current eligibility, generation/deadline fences, atomic effects, lost leases and idempotent self reminders passed.'
