-- PRD-01 FOUND-FR-009: real column-restricted Worker, history and rollback.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO organizations(id,name,created_at,updated_at)
 VALUES('13000000-0000-0000-0000-000000000101','Legacy activity clock',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('13000000-0000-0000-0000-000000000102','event-clock@example.test','EVENT-CLOCK@EXAMPLE.TEST','Clock fixture','ACTIVE','fixture',now(),now());
INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
 VALUES('13000000-0000-0000-0000-000000000103','13000000-0000-0000-0000-000000000101','Clock board',now(),now());
INSERT INTO work_event_streams(tenant_id,board_id)
 VALUES('13000000-0000-0000-0000-000000000101','13000000-0000-0000-0000-000000000103');
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at,ready_at)
 SELECT '13000000-0000-0000-0000-000000000101',md5('legacy-clock-'||n)::uuid,
 '13000000-0000-0000-0000-000000000103',n,'13000000-0000-0000-0000-000000000102',
 'BOARD_UPDATED','Board','13000000-0000-0000-0000-000000000103',1,'clock-upgrade',
 now()-interval '2 days',CASE WHEN n>1 THEN now()-interval '1 day' END FROM generate_series(1,3) n;
-- A reset source looks pending again; its lost history must not be invented.
UPDATE work_events SET ready_at=NULL WHERE correlation_id='clock-upgrade' AND sequence=3;

DO $$ BEGIN
 IF (SELECT count(*) FROM work_events WHERE correlation_id='clock-upgrade' AND updated_at=created_at AND isfinite(updated_at))<>2 THEN
  RAISE EXCEPTION 'New source creation clock missing';
 END IF;
 -- Third source was actually reset by the fixture and has its later clock.
 IF NOT EXISTS(SELECT 1 FROM work_events WHERE correlation_id='clock-upgrade' AND sequence=3
  AND ready_at IS NULL AND updated_at>=created_at AND isfinite(updated_at)) THEN
  RAISE EXCEPTION 'Reset source mutation clock missing';
 END IF;
 IF has_column_privilege('strataai_worker_runtime','work_events','updated_at','UPDATE') OR
  has_function_privilege('strataai_worker_runtime','capture_work_event_update_clock()','EXECUTE') THEN
  RAISE EXCEPTION 'Managed clock capability exposed';
 END IF;
END $$;
CREATE TEMP TABLE work_clock_baseline AS SELECT event_id,to_jsonb(e) AS body FROM work_events e WHERE correlation_id='clock-upgrade';
SELECT set_config('app.tenant_id','13000000-0000-0000-0000-000000000101',true);
SET LOCAL ROLE strataai_worker_runtime;
UPDATE work_events SET ready_at=clock_timestamp() WHERE tenant_id='13000000-0000-0000-0000-000000000101'
 AND event_id=md5('legacy-clock-1')::uuid;
RESET ROLE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM work_events e JOIN work_clock_baseline f USING(event_id)
  WHERE e.sequence=1 AND e.ready_at IS NOT NULL AND isfinite(e.updated_at) AND e.updated_at>e.created_at
  AND (to_jsonb(e)-'ready_at'-'updated_at')=(f.body-'ready_at'-'updated_at')) THEN
  RAISE EXCEPTION 'Column-restricted delivery clock or immutable source failed';
 END IF;
END $$;
TRUNCATE work_clock_baseline;
INSERT INTO work_clock_baseline SELECT event_id,to_jsonb(e) FROM work_events e WHERE correlation_id='clock-upgrade';
UPDATE work_events SET ready_at=ready_at,updated_at='infinity' WHERE correlation_id='clock-upgrade';
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM work_events e JOIN work_clock_baseline f USING(event_id) WHERE to_jsonb(e) IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'No-op/caller clock restamped event';
 END IF;
 BEGIN
  UPDATE work_events SET ready_at=NULL,entity_version=entity_version+1 WHERE correlation_id='clock-upgrade';
  RAISE EXCEPTION 'Readiness mutation bypassed immutable history';
 EXCEPTION WHEN check_violation THEN
  IF SQLERRM<>'Activity source is immutable' THEN RAISE; END IF;
 END;
 IF EXISTS(SELECT 1 FROM work_events e JOIN work_clock_baseline f USING(event_id) WHERE to_jsonb(e) IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'Rejected mutation retained clock or payload effects';
 END IF;
END $$;
UPDATE work_events SET ready_at=NULL,updated_at='-infinity' WHERE correlation_id='clock-upgrade' AND sequence=1;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM work_events e JOIN work_clock_baseline f USING(event_id)
  WHERE e.sequence=1 AND e.ready_at IS NULL AND isfinite(e.updated_at)
  AND e.updated_at>(f.body->>'updated_at')::timestamptz
  AND (to_jsonb(e)-'ready_at'-'updated_at')=(f.body-'ready_at'-'updated_at')) THEN
  RAISE EXCEPTION 'Reset clock lost history or trusted caller clock';
 END IF;
END $$;
ROLLBACK;
\echo 'Work event creation, restricted readiness delivery, no-op, reset, clock tamper and immutable rollback passed.'
