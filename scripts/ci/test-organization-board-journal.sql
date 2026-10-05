-- PRD-04: canonical ordered source projection, not HTTP/audience admission.
\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_directory_journal_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_directory_journal_ci;
GRANT SELECT,INSERT ON work_events TO strataai_directory_journal_ci;
GRANT SELECT ON organization_board_events,organization_board_event_streams TO strataai_directory_journal_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('07300000-0000-0000-0000-000000000001','Journal A',now(),now()),
 ('07300000-0000-0000-0000-000000000002','Journal B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('07300000-0000-0000-0000-000000000041','directory-journal@example.test','DIRECTORY-JOURNAL@EXAMPLE.TEST','Journal actor','ACTIVE',true,'fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),id,'07300000-0000-0000-0000-000000000041','OWNER','ACTIVE'
 FROM organizations WHERE id IN ('07300000-0000-0000-0000-000000000001','07300000-0000-0000-0000-000000000002');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('07300000-0000-0000-0000-000000000011','07300000-0000-0000-0000-000000000001','Board A1',now(),now()),
 ('07300000-0000-0000-0000-000000000012','07300000-0000-0000-0000-000000000001','Board A2',now(),now()),
 ('07300000-0000-0000-0000-000000000013','07300000-0000-0000-0000-000000000002','Board B',now(),now());
INSERT INTO work_event_streams(tenant_id,board_id,last_sequence)
 SELECT tenant_id,id,1 FROM boards WHERE id IN
 ('07300000-0000-0000-0000-000000000011','07300000-0000-0000-0000-000000000012','07300000-0000-0000-0000-000000000013');
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('07300000-0000-0000-0000-000000000002','07300000-0000-0000-0000-000000000053',
 '07300000-0000-0000-0000-000000000013',1,'07300000-0000-0000-0000-000000000041',
 'BOARD_CREATED','Board','07300000-0000-0000-0000-000000000013',1,'journal-foreign',now());
SET LOCAL ROLE strataai_directory_journal_ci;
SET LOCAL app.tenant_id='07300000-0000-0000-0000-000000000001';
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('07300000-0000-0000-0000-000000000001','07300000-0000-0000-0000-000000000051',
 '07300000-0000-0000-0000-000000000011',1,'07300000-0000-0000-0000-000000000041',
 'BOARD_CREATED','Board','07300000-0000-0000-0000-000000000011',1,'journal-first',now()),
 ('07300000-0000-0000-0000-000000000001','07300000-0000-0000-0000-000000000052',
 '07300000-0000-0000-0000-000000000012',1,'07300000-0000-0000-0000-000000000041',
 'BOARD_ARCHIVED','Board','07300000-0000-0000-0000-000000000012',2,'journal-second',now());
DO $$ DECLARE counter_before jsonb; BEGIN
 SELECT to_jsonb(s) INTO counter_before FROM organization_board_event_streams s;
 IF (SELECT count(*) FROM organization_board_events)<>2 OR
  (SELECT last_sequence FROM organization_board_event_streams)<>2 OR
  (SELECT event_id FROM organization_board_events WHERE sequence=2)<>'07300000-0000-0000-0000-000000000052'::uuid THEN
  RAISE EXCEPTION 'Board-independent Organization sequence or tenant isolation failed'; END IF;
 IF EXISTS(SELECT 1 FROM organization_board_events j JOIN work_events e USING(tenant_id,event_id) WHERE e.ready_at IS NOT NULL) THEN
  RAISE EXCEPTION 'Source projection manufactured Worker readiness'; END IF;
 BEGIN
  INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
   VALUES('07300000-0000-0000-0000-000000000001',gen_random_uuid(),'07300000-0000-0000-0000-000000000011',2,
   '07300000-0000-0000-0000-000000000041','BOARD_UPDATED','Board','07300000-0000-0000-0000-000000000011',2,'journal-rollback',now());
  IF (SELECT last_sequence FROM organization_board_event_streams)<>3 THEN RAISE EXCEPTION 'Rollback fixture did not reach projection'; END IF;
  RAISE EXCEPTION 'Declared late command refusal' USING ERRCODE='23514';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF (SELECT count(*) FROM organization_board_events)<>2 OR (SELECT last_sequence FROM organization_board_event_streams)<>2
  OR (SELECT count(*) FROM work_events)<>2 OR (SELECT to_jsonb(s) FROM organization_board_event_streams s) IS DISTINCT FROM counter_before
  THEN RAISE EXCEPTION 'Owning rollback retained journal/counter/source effects'; END IF;
 INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
  SELECT tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at FROM work_events
  ON CONFLICT(tenant_id,event_id) DO NOTHING;
 IF (SELECT count(*) FROM organization_board_events)<>2 OR (SELECT last_sequence FROM organization_board_event_streams)<>2 THEN
  RAISE EXCEPTION 'Duplicate source allocated another journal event'; END IF;
 BEGIN
  INSERT INTO organization_board_events VALUES('07300000-0000-0000-0000-000000000001',3,gen_random_uuid());
  RAISE EXCEPTION 'Runtime manufactured a journal event';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  UPDATE organization_board_event_streams SET last_sequence=99;
  RAISE EXCEPTION 'Runtime changed journal ordering';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
   VALUES('07300000-0000-0000-0000-000000000002',gen_random_uuid(),'07300000-0000-0000-0000-000000000013',2,
   '07300000-0000-0000-0000-000000000041','BOARD_UPDATED','Board','07300000-0000-0000-0000-000000000013',2,'journal-foreign-refusal',now());
  RAISE EXCEPTION 'Foreign source widened the journal trigger capability';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
-- Legacy shared-star type must not enter the Organization source projection.
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('07300000-0000-0000-0000-000000000001',gen_random_uuid(),'07300000-0000-0000-0000-000000000011',2,
 '07300000-0000-0000-0000-000000000041','BOARD_STARRED','Board','07300000-0000-0000-0000-000000000011',2,'journal-private-exclusion',now());
DO $$ BEGIN
 IF (SELECT count(*) FROM organization_board_events)<>2 OR (SELECT last_sequence FROM organization_board_event_streams)<>2 THEN
  RAISE EXCEPTION 'Personal activity entered shared Board ordering'; END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 BEGIN
  UPDATE organization_board_events SET sequence=99 WHERE tenant_id='07300000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Historical journal changed';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF (SELECT last_sequence FROM organization_board_event_streams WHERE tenant_id='07300000-0000-0000-0000-000000000002')<>1 THEN
  RAISE EXCEPTION 'Rejected foreign write changed another stream'; END IF;
END $$;
UPDATE work_events SET ready_at=clock_timestamp() WHERE event_id='07300000-0000-0000-0000-000000000052';
SET LOCAL ROLE strataai_directory_journal_ci;
DO $$ BEGIN
 IF (SELECT count(*) FROM organization_board_events j JOIN work_events e USING(tenant_id,event_id) WHERE e.ready_at IS NOT NULL)<>1 THEN
  RAISE EXCEPTION 'Journal source lost canonical readiness'; END IF;
END $$;
RESET ROLE;
ROLLBACK;
\echo 'Organization Board journal: restricted canonical projection, cross-Board order, tenant isolation, original IDs, pending/readiness, rollback, duplicate and private-source exclusion passed.'
