-- PRD-01 FOUND-FR-009: real receipt completion, no-op/replay and rollback clocks.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO organizations(id,name,created_at,updated_at)
 VALUES('12800000-0000-0000-0000-000000000201','Receipt clocks',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('12800000-0000-0000-0000-000000000202','work-clock-current@example.test','WORK-CLOCK-CURRENT@EXAMPLE.TEST','Receipt clocks','ACTIVE','fixture',now(),now());
SET LOCAL ROLE strataai_api_runtime;
SELECT set_config('app.tenant_id','12800000-0000-0000-0000-000000000201',true);
INSERT INTO work_command_replays(tenant_id,actor_id,key_id,fingerprint,updated_at)
 VALUES('12800000-0000-0000-0000-000000000201','12800000-0000-0000-0000-000000000202',
  '12800000-0000-0000-0000-000000000203',repeat('A',64),'infinity');
DO $$ DECLARE first work_command_replays%ROWTYPE; completed work_command_replays%ROWTYPE; baseline jsonb; BEGIN
 SELECT * INTO first FROM work_command_replays WHERE key_id='12800000-0000-0000-0000-000000000203';
 IF first.updated_at IS DISTINCT FROM first.created_at OR NOT isfinite(first.updated_at) THEN
  RAISE EXCEPTION 'New receipt did not use its original creation clock'; END IF;
 UPDATE work_command_replays SET result_json='{"Succeeded":true}'::jsonb,updated_at='-infinity'
  WHERE key_id=first.key_id;
 SELECT * INTO completed FROM work_command_replays WHERE key_id=first.key_id;
 IF completed.updated_at IS NULL OR completed.updated_at<first.updated_at OR NOT isfinite(completed.updated_at)
  OR (to_jsonb(completed)-'updated_at'-'result_json') IS DISTINCT FROM (to_jsonb(first)-'updated_at'-'result_json') THEN
  RAISE EXCEPTION 'Completion clock or original receipt identity changed'; END IF;
 baseline:=to_jsonb(completed);
 UPDATE work_command_replays SET result_json=result_json,updated_at='infinity' WHERE key_id=first.key_id;
 IF (SELECT to_jsonb(r) FROM work_command_replays r WHERE key_id=first.key_id) IS DISTINCT FROM baseline THEN
  RAISE EXCEPTION 'Exact no-op/caller clock restamped receipt'; END IF;
 BEGIN
  UPDATE work_command_replays SET fingerprint=repeat('B',64) WHERE key_id=first.key_id;
  RAISE EXCEPTION 'fixture rollback' USING ERRCODE='P0002';
 EXCEPTION WHEN no_data_found THEN NULL;
 END;
 IF (SELECT to_jsonb(r) FROM work_command_replays r WHERE key_id=first.key_id) IS DISTINCT FROM baseline THEN
  RAISE EXCEPTION 'Rolled-back receipt update retained effects or clock'; END IF;
 BEGIN
  INSERT INTO work_command_replays(tenant_id,actor_id,key_id,fingerprint,created_at)
   VALUES(first.tenant_id,first.actor_id,gen_random_uuid(),repeat('A',64),'infinity');
  RAISE EXCEPTION 'Nonfinite receipt creation accepted';
 EXCEPTION WHEN check_violation THEN NULL;
 END;
END $$;
ROLLBACK;
\echo 'Work receipt clocks: creation, actual completion, caller-clock denial, no-op preservation, rollback and finite-time admission passed.'
