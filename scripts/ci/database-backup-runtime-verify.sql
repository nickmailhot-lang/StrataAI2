\set ON_ERROR_STOP on
-- Actual TCP sessions, each using its independently provisioned login.
DO $$ BEGIN
 IF current_user <> session_user OR current_user NOT IN ('strataai_api_runtime','strataai_worker_runtime')
  OR public.runtime_database_role_is_safe() IS NOT TRUE THEN
  RAISE EXCEPTION 'Restored runtime login is not a safe independent identity';
 END IF;
END $$;
SELECT current_user='strataai_api_runtime' AS is_api \gset
\if :is_api
BEGIN;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS(SELECT FROM cards) THEN RAISE EXCEPTION 'Restored API missing-context read leaked Cards'; END IF;
END $$;
SELECT set_config('app.tenant_id','bacc0000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM cards)<>1 OR EXISTS(SELECT FROM cards WHERE tenant_id<>'bacc0000-0000-0000-0000-000000000001') THEN
  RAISE EXCEPTION 'Restored API lost tenant read isolation'; END IF;
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
 VALUES(gen_random_uuid(),'bacc0000-0000-0000-0000-000000000001','bacc0000-0000-0000-0000-000000000011','Restored API write',repeat('7',30),now(),now());
 IF (SELECT count(*) FROM board_lists)<>2 THEN RAISE EXCEPTION 'Restored API own-tenant write failed'; END IF;
 BEGIN
  INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
  VALUES(gen_random_uuid(),'bacc0000-0000-0000-0000-000000000002','bacc0000-0000-0000-0000-000000000012','Forbidden foreign write',repeat('7',30),now(),now());
  RAISE EXCEPTION 'Restored API admitted a foreign write';
 EXCEPTION WHEN insufficient_privilege THEN
  IF SQLERRM<>'new row violates row-level security policy for table "board_lists"' THEN RAISE; END IF;
 END;
 BEGIN
  PERFORM public.recover_attachment_scan_page('bacc0000-0000-0000-0000-000000000001',1);
  RAISE EXCEPTION 'Restored API acquired Worker scan recovery';
 EXCEPTION WHEN insufficient_privilege THEN NULL;
 END;
END $$;
ROLLBACK;
-- Publications commit in a connection that ends before the Worker logs in.
BEGIN;
SELECT set_config('app.tenant_id','bacc0000-0000-0000-0000-000000000001',true);
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id)
VALUES('bacc0000-0000-0000-0000-000000000901','bacc0000-0000-0000-0000-000000000001','RECOVERY_FIXTURE','recovery-fixture','bacc0000-0000-0000-0000-000000000099','recovery-fixture','recovery-fixture');
SELECT set_config('app.tenant_id','bacc0000-0000-0000-0000-000000000002',true);
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id)
VALUES('bacc0000-0000-0000-0000-000000000902','bacc0000-0000-0000-0000-000000000002','RECOVERY_FIXTURE','recovery-fixture','bacc0000-0000-0000-0000-000000000099','recovery-fixture','recovery-fixture');
COMMIT;
\else
BEGIN;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS(SELECT FROM background_jobs) THEN RAISE EXCEPTION 'Restored Worker missing-context queue read leaked jobs'; END IF;
END $$;
SELECT set_config('app.tenant_id','bacc0000-0000-0000-0000-000000000001',true);
DO $$
DECLARE claimed public.background_jobs%ROWTYPE;
BEGIN
 IF (SELECT count(*) FROM background_jobs)<>1 THEN RAISE EXCEPTION 'Restored Worker crossed queue tenant scope'; END IF;
 BEGIN
  PERFORM id FROM cards;
  RAISE EXCEPTION 'Restored Worker obtained private Card reads';
 EXCEPTION WHEN insufficient_privilege THEN NULL;
 END;
 BEGIN
  PERFORM sha256 FROM attachments;
  RAISE EXCEPTION 'Restored Worker obtained private attachment metadata';
 EXCEPTION WHEN insufficient_privilege THEN NULL;
 END;
 SELECT * INTO claimed FROM public.claim_background_job('bacc0000-0000-0000-0000-000000000903');
 IF claimed.id IS DISTINCT FROM 'bacc0000-0000-0000-0000-000000000901'::uuid
  OR claimed.tenant_id IS DISTINCT FROM 'bacc0000-0000-0000-0000-000000000001'::uuid
  OR claimed.state<>'RUNNING' OR claimed.attempt_count<>1 OR claimed.lease_id IS NULL THEN
  RAISE EXCEPTION 'Restored Worker did not claim the committed API job'; END IF;
 IF public.complete_background_job(claimed.id,claimed.lease_id,'bacc0000-0000-0000-0000-000000000904') THEN
  RAISE EXCEPTION 'Restored Worker acknowledged another process lease'; END IF;
 IF public.complete_background_job(claimed.id,claimed.lease_id,claimed.worker_id) IS NOT TRUE THEN
  RAISE EXCEPTION 'Restored Worker could not acknowledge its original lease'; END IF;
 IF NOT EXISTS(SELECT FROM background_jobs WHERE id=claimed.id AND state='SUCCEEDED' AND lease_id IS NULL AND worker_id IS NULL) THEN
  RAISE EXCEPTION 'Restored Worker acknowledgment lost committed queue state'; END IF;
END $$;
COMMIT;
\endif
