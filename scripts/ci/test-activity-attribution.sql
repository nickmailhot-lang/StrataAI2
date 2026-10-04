-- PRD-15 FR-008/009/010: actual restricted journal storage, not HTTP admission.
\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_activity_storage_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_activity_storage_ci;
GRANT SELECT,INSERT,UPDATE ON work_events TO strataai_activity_storage_ci;
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
 SELECT to_jsonb(e)-'ready_at' INTO baseline FROM work_events e;
 FOREACH mutation IN ARRAY ARRAY[
  'event_id=gen_random_uuid()','tenant_id=gen_random_uuid()','board_id=gen_random_uuid()',
  'sequence=sequence+1','actor_id=gen_random_uuid()','event_type=''CARD_UPDATED''',
  'entity_type=''Card''','entity_id=gen_random_uuid()','entity_version=entity_version+1',
  'correlation_id=''rewritten''','metadata=''{}''::jsonb||jsonb_build_object(''body'',''Private comment'')',
  'created_at=created_at+interval ''1 second''','activity_actor_label=''Rewritten name'''] LOOP
  BEGIN
   EXECUTE 'UPDATE work_events SET '||mutation;
   RAISE EXCEPTION 'Historical activity mutated: %',mutation;
  EXCEPTION WHEN check_violation THEN NULL; END;
  IF (SELECT to_jsonb(e)-'ready_at' FROM work_events e) IS DISTINCT FROM baseline THEN
   RAISE EXCEPTION 'Rejected activity mutation changed historical state'; END IF;
 END LOOP;
 UPDATE work_events SET ready_at=clock_timestamp();
 IF (SELECT ready_at IS NOT NULL FROM work_events) IS NOT TRUE OR
  (SELECT to_jsonb(e)-'ready_at' FROM work_events e) IS DISTINCT FROM baseline THEN
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
UPDATE users SET display_name='Later name',status='DEACTIVATED' WHERE id='06200000-0000-0000-0000-000000000041';
SET LOCAL ROLE strataai_activity_storage_ci;
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
\echo 'Activity journal: captured/immutable attribution, rename/deactivation history, foreign caption refusal, forced tenant isolation, immutable envelope, readiness-only updates and seek indexes passed.'
