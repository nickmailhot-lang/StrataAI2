-- PRD-15 FR-008/009/010: actual restricted journal storage, not HTTP admission.
\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_activity_storage_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_activity_storage_ci;
GRANT SELECT,INSERT,UPDATE ON work_events TO strataai_activity_storage_ci;
CREATE ROLE strataai_activity_delivery_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_activity_delivery_ci;
GRANT SELECT(tenant_id,event_id,board_id,actor_id,ready_at),UPDATE(ready_at) ON work_events TO strataai_activity_delivery_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('06200000-0000-0000-0000-000000000001','Activity A',now(),now()),
 ('06200000-0000-0000-0000-000000000002','Activity B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at) VALUES
 ('06200000-0000-0000-0000-000000000041','activity-a@example.test','ACTIVITY-A@EXAMPLE.TEST','Original name','ACTIVE',true,'fixture',now(),now()),
 ('06200000-0000-0000-0000-000000000042','activity-b@example.test','ACTIVITY-B@EXAMPLE.TEST','Private other actor','ACTIVE',true,'fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000041','MEMBER','ACTIVE'),
 (gen_random_uuid(),'06200000-0000-0000-0000-000000000002','06200000-0000-0000-0000-000000000042','MEMBER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('06200000-0000-0000-0000-000000000011','06200000-0000-0000-0000-000000000001','Board A',now(),now()),
 ('06200000-0000-0000-0000-000000000012','06200000-0000-0000-0000-000000000002','Board B',now(),now());
INSERT INTO work_event_streams(tenant_id,board_id) VALUES
 ('06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000011'),
 ('06200000-0000-0000-0000-000000000002','06200000-0000-0000-0000-000000000012');
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('06200000-0000-0000-0000-000000000002','06200000-0000-0000-0000-000000000052',
 '06200000-0000-0000-0000-000000000012',1,'06200000-0000-0000-0000-000000000042',
 'BOARD_UPDATED','Board','06200000-0000-0000-0000-000000000012',1,'activity-storage',now());
SET LOCAL ROLE strataai_activity_storage_ci;
SELECT set_config('app.tenant_id','06200000-0000-0000-0000-000000000001',true);
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at,activity_actor_label)
 VALUES('06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000051',
 '06200000-0000-0000-0000-000000000011',1,'06200000-0000-0000-0000-000000000041',
 'BOARD_UPDATED','Board','06200000-0000-0000-0000-000000000011',1,'activity-storage',now(),'Forged caption');
DO $$ DECLARE mutation text; baseline jsonb; affected integer; BEGIN
 IF (SELECT count(*) FROM work_events)<>1 OR
  (SELECT activity_actor_label FROM work_events)<>'Original name' THEN
  RAISE EXCEPTION 'Activity caption forged or tenant read widened'; END IF;
 SELECT to_jsonb(e)-'ready_at'-'updated_at' INTO baseline FROM work_events e;
 FOREACH mutation IN ARRAY ARRAY[
  'event_id=gen_random_uuid()','tenant_id=gen_random_uuid()','board_id=gen_random_uuid()',
  'sequence=sequence+1','actor_id=gen_random_uuid()','event_type=''CARD_UPDATED''',
  'entity_type=''Card''','entity_id=gen_random_uuid()','entity_version=entity_version+1',
  'correlation_id=''rewritten''','metadata=''{}''::jsonb||jsonb_build_object(''body'',''Private comment'')',
  'created_at=created_at+interval ''1 second''','activity_actor_label=''Rewritten name'''] LOOP
  BEGIN
   EXECUTE 'UPDATE work_events SET '||mutation;
   RAISE EXCEPTION 'Historical activity mutated: %',mutation;
  EXCEPTION WHEN check_violation OR foreign_key_violation OR insufficient_privilege THEN NULL; END;
  IF (SELECT to_jsonb(e)-'ready_at'-'updated_at' FROM work_events e) IS DISTINCT FROM baseline THEN
   RAISE EXCEPTION 'Rejected activity mutation changed historical state'; END IF;
 END LOOP;
 UPDATE work_events SET ready_at=clock_timestamp();
 IF (SELECT ready_at IS NOT NULL FROM work_events) IS NOT TRUE OR
  (SELECT to_jsonb(e)-'ready_at'-'updated_at' FROM work_events e) IS DISTINCT FROM baseline THEN
  RAISE EXCEPTION 'Worker readiness changed activity identity'; END IF;
 UPDATE work_events SET ready_at=ready_at;
 UPDATE work_events SET activity_actor_label='Foreign' WHERE tenant_id='06200000-0000-0000-0000-000000000002';
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Foreign activity updated'; END IF;
 BEGIN
  DELETE FROM work_events;
  RAISE EXCEPTION 'Runtime deleted historical activity';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
   SELECT '06200000-0000-0000-0000-000000000002',gen_random_uuid(),'06200000-0000-0000-0000-000000000012',2,
    actor_id,event_type,entity_type,'06200000-0000-0000-0000-000000000012',entity_version,correlation_id,created_at FROM work_events;
  RAISE EXCEPTION 'Foreign activity inserted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 IF (SELECT count(*) FROM work_events)<>1 THEN RAISE EXCEPTION 'Rejected activity effects survived'; END IF;
END $$;
-- A foreign account ID must not expose that account's caption to this tenant.
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000053',
 '06200000-0000-0000-0000-000000000011',2,'06200000-0000-0000-0000-000000000042',
 'BOARD_UPDATED','Board','06200000-0000-0000-0000-000000000011',1,'activity-storage',now());
DO $$ BEGIN
 IF (SELECT activity_actor_label FROM work_events WHERE event_id='06200000-0000-0000-0000-000000000053')<>
  'Member 06200000-0000-0000-0000-000000000042' THEN RAISE EXCEPTION 'Foreign account caption disclosed'; END IF;
END $$;
RESET ROLE;
-- Generated Watch/Reminder references must be materialized before comparing
-- the historical row. Exercise the actual Worker column privilege shape.
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('06200000-0000-0000-0000-000000000021','06200000-0000-0000-0000-000000000001',
 '06200000-0000-0000-0000-000000000011','Activity list','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('06200000-0000-0000-0000-000000000031','06200000-0000-0000-0000-000000000001',
 '06200000-0000-0000-0000-000000000011','06200000-0000-0000-0000-000000000021',
 'Activity Card','500000000000000000000000000000',now(),now());
INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,board_id,watching,created_at,updated_at,version) VALUES
 ('06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000061',
 '06200000-0000-0000-0000-000000000041','BOARD','06200000-0000-0000-0000-000000000011',
 '06200000-0000-0000-0000-000000000011',true,now(),now(),1);
INSERT INTO card_reminders(tenant_id,id,user_id,card_id,interval_code,enabled,status,generation,created_at,updated_at,version) VALUES
 ('06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000062',
 '06200000-0000-0000-0000-000000000041','06200000-0000-0000-0000-000000000031',
 'AT_DUE',true,'SUSPENDED',1,now(),now(),1);
SET LOCAL ROLE strataai_activity_storage_ci;
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at) VALUES
 ('06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000054',
 '06200000-0000-0000-0000-000000000011',3,'06200000-0000-0000-0000-000000000041',
 'WATCH_CREATED','WatchSubscription','06200000-0000-0000-0000-000000000061',1,'activity-generated',now()),
 ('06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000055',
 '06200000-0000-0000-0000-000000000011',4,'06200000-0000-0000-0000-000000000041',
 'REMINDER_SCHEDULED','Reminder','06200000-0000-0000-0000-000000000062',1,'activity-generated',now());
SET LOCAL ROLE strataai_activity_delivery_ci;
UPDATE work_events SET ready_at=clock_timestamp()
 WHERE event_id IN ('06200000-0000-0000-0000-000000000054','06200000-0000-0000-0000-000000000055');
SET LOCAL ROLE strataai_activity_storage_ci;
DO $$ BEGIN
 IF (SELECT count(*) FROM work_events WHERE correlation_id='activity-generated' AND ready_at IS NOT NULL
  AND activity_actor_label='Original name' AND (watch_subscription_id=entity_id OR reminder_id=entity_id))<>2 THEN
  RAISE EXCEPTION 'Generated source references or attribution prevented readiness'; END IF;
 BEGIN
  UPDATE work_events SET entity_version=2 WHERE correlation_id='activity-generated';
  RAISE EXCEPTION 'Generated activity source was rewritten';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF EXISTS(SELECT 1 FROM work_events WHERE correlation_id='activity-generated' AND entity_version<>1) THEN
  RAISE EXCEPTION 'Refused generated source update survived'; END IF;
END $$;
RESET ROLE;
UPDATE users SET display_name='Later name',status='DEACTIVATED' WHERE id='06200000-0000-0000-0000-000000000041';
-- Valid same-tenant replacement identities must fail the identity guard, not
-- merely an unrelated FK. Ordinary intent transitions must still succeed.
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('06200000-0000-0000-0000-000000000043','private-target@example.test','PRIVATE-TARGET@EXAMPLE.TEST','Other personal owner','ACTIVE','fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 VALUES(gen_random_uuid(),'06200000-0000-0000-0000-000000000001','06200000-0000-0000-0000-000000000043','MEMBER','ACTIVE');
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
 VALUES('06200000-0000-0000-0000-000000000032','06200000-0000-0000-0000-000000000001',
 '06200000-0000-0000-0000-000000000011','06200000-0000-0000-0000-000000000021',
 'Other activity Card','600000000000000000000000000000',now(),now());
GRANT SELECT,UPDATE ON watch_subscriptions,card_reminders TO strataai_activity_storage_ci;
SET LOCAL ROLE strataai_activity_storage_ci;
DO $$ DECLARE mutation text; baseline jsonb; BEGIN
 SELECT to_jsonb(w) INTO baseline FROM watch_subscriptions w;
 FOREACH mutation IN ARRAY ARRAY[
  'user_id=''06200000-0000-0000-0000-000000000043''',
  'entity_type=''CARD'',entity_id=''06200000-0000-0000-0000-000000000031'',board_id=NULL,card_id=''06200000-0000-0000-0000-000000000031''',
  'created_at=created_at-interval ''1 second'''] LOOP
  BEGIN
   EXECUTE 'UPDATE watch_subscriptions SET '||mutation;
   RAISE EXCEPTION 'Private watch identity was rewritten';
  EXCEPTION WHEN check_violation THEN
   IF SQLERRM<>'Private activity identity is immutable' THEN RAISE; END IF;
  END;
  IF (SELECT to_jsonb(w) FROM watch_subscriptions w) IS DISTINCT FROM baseline THEN
   RAISE EXCEPTION 'Refused private watch rewrite survived'; END IF;
 END LOOP;
 SELECT to_jsonb(r) INTO baseline FROM card_reminders r;
 FOREACH mutation IN ARRAY ARRAY[
  'user_id=''06200000-0000-0000-0000-000000000043''',
  'card_id=''06200000-0000-0000-0000-000000000032''',
  'created_at=created_at-interval ''1 second'''] LOOP
  BEGIN
   EXECUTE 'UPDATE card_reminders SET '||mutation;
   RAISE EXCEPTION 'Private Reminder identity was rewritten';
  EXCEPTION WHEN check_violation THEN
   IF SQLERRM<>'Private activity identity is immutable' THEN RAISE; END IF;
  END;
  IF (SELECT to_jsonb(r) FROM card_reminders r) IS DISTINCT FROM baseline THEN
   RAISE EXCEPTION 'Refused private Reminder rewrite survived'; END IF;
 END LOOP;
 UPDATE watch_subscriptions SET watching=false,version=version+1,updated_at=clock_timestamp();
 UPDATE card_reminders SET enabled=false,status='CANCELLED',due_at=NULL,trigger_at=NULL,
  version=version+1,generation=generation+1,updated_at=clock_timestamp();
 IF NOT EXISTS(SELECT 1 FROM watch_subscriptions WHERE NOT watching AND version=2
  AND user_id='06200000-0000-0000-0000-000000000041' AND entity_type='BOARD'
  AND entity_id='06200000-0000-0000-0000-000000000011') OR
  NOT EXISTS(SELECT 1 FROM card_reminders WHERE status='CANCELLED' AND NOT enabled AND version=2 AND generation=2
   AND user_id='06200000-0000-0000-0000-000000000041' AND card_id='06200000-0000-0000-0000-000000000031') THEN
  RAISE EXCEPTION 'Normal private intent transitions or retained identity failed'; END IF;
 IF has_function_privilege(current_user,'enforce_private_activity_identity()','EXECUTE') THEN
  RAISE EXCEPTION 'Private activity identity trigger was exposed as a direct capability'; END IF;
END $$;
DO $$ BEGIN
 IF (SELECT activity_actor_label FROM work_events WHERE event_id='06200000-0000-0000-0000-000000000051')<>'Original name' OR
  (SELECT actor_id FROM work_events WHERE event_id='06200000-0000-0000-0000-000000000051')<>'06200000-0000-0000-0000-000000000041'::uuid THEN
  RAISE EXCEPTION 'Account rename/deactivation rewrote historical attribution'; END IF;
 IF to_regclass('public.ix_work_events_board_activity') IS NULL OR to_regclass('public.ix_work_events_card_activity') IS NULL THEN
  RAISE EXCEPTION 'Activity seek indexes are absent'; END IF;
END $$;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN IF EXISTS(SELECT 1 FROM work_events) THEN RAISE EXCEPTION 'Activity read without owning tenant'; END IF; END $$;
ROLLBACK;
\echo 'Activity journal: captured/immutable attribution, rename/deactivation history, foreign caption refusal, forced tenant isolation, immutable envelope, restricted Watch/Reminder generated-source readiness updates and seek indexes passed.'
