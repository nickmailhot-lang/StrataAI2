BEGIN;
DO $$ BEGIN
 IF (SELECT count(*) FROM issuer_clock_upgrade_fixture)<>4 OR EXISTS(
  SELECT 1 FROM issuer_clock_upgrade_fixture f LEFT JOIN invitation_issuer_authority_jobs j ON j.id=f.id
  WHERE j.id IS NULL OR to_jsonb(j)-'updated_at' IS DISTINCT FROM f.original
   OR j.updated_at IS DISTINCT FROM CASE j.state WHEN 'SUCCEEDED' THEN j.completed_at WHEN 'FAILED' THEN j.failed_at ELSE j.created_at END
 ) THEN RAISE EXCEPTION 'Issuer upgrade changed historical state or invented a clock'; END IF;
 IF has_table_privilege('strataai_worker_runtime','invitation_issuer_authority_jobs','UPDATE')
  OR has_table_privilege('strataai_api_runtime','invitation_issuer_authority_jobs','UPDATE') THEN
  RAISE EXCEPTION 'Issuer job direct runtime update grant was introduced';
 END IF;
END $$;
CREATE TEMP TABLE issuer_clock_pending AS SELECT * FROM invitation_issuer_authority_jobs WHERE state='PENDING';
-- A rolled-back restricted claim must restore its entire original row.
SAVEPOINT before_claim;
SET LOCAL ROLE strataai_worker_runtime;
SELECT * FROM public.claim_invitation_issuer_authority(gen_random_uuid());
RESET ROLE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs j JOIN issuer_clock_pending p USING(id)
  WHERE j.state='RUNNING' AND j.attempt_count=1 AND j.updated_at>p.updated_at AND j.created_at=p.created_at) THEN
  RAISE EXCEPTION 'Accepted issuer claim did not record its update clock';
 END IF;
END $$;
ROLLBACK TO SAVEPOINT before_claim;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs j JOIN issuer_clock_pending p USING(id) WHERE to_jsonb(j)<>to_jsonb(p)) THEN
  RAISE EXCEPTION 'Rolled-back issuer claim retained state or clock';
 END IF;
 UPDATE invitation_issuer_authority_jobs SET state=state WHERE state='PENDING';
 IF EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs j JOIN issuer_clock_pending p USING(id) WHERE to_jsonb(j)<>to_jsonb(p)) THEN
  RAISE EXCEPTION 'No-op issuer update changed its clock';
 END IF;
 BEGIN
  UPDATE invitation_issuer_authority_jobs SET updated_at=clock_timestamp() WHERE state='PENDING';
  RAISE EXCEPTION 'Direct clock replacement was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
SET LOCAL ROLE strataai_worker_runtime;
SELECT * FROM public.claim_invitation_issuer_authority(gen_random_uuid());
RESET ROLE;
CREATE TEMP TABLE issuer_clock_capability AS SELECT id,event_id,actor_id,worker_id,lease_id FROM invitation_issuer_authority_jobs WHERE state='RUNNING';
GRANT SELECT ON issuer_clock_capability TO strataai_worker_runtime;
CREATE TEMP TABLE issuer_clock_running AS SELECT j.* FROM invitation_issuer_authority_jobs j JOIN issuer_clock_pending p USING(id);
SET LOCAL ROLE strataai_worker_runtime;
DO $$ DECLARE c record; BEGIN
 SELECT * INTO STRICT c FROM issuer_clock_capability;
 IF public.deliver_invitation_issuer_authority(c.id,c.event_id,c.actor_id,gen_random_uuid(),c.lease_id,100) THEN
  RAISE EXCEPTION 'Substituted issuer Worker capability was accepted';
 END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs j JOIN issuer_clock_running p USING(id) WHERE to_jsonb(j)<>to_jsonb(p)) THEN
  RAISE EXCEPTION 'Refused issuer capability changed state or clock';
 END IF;
END $$;
-- Expiry is privileged fault injection; reclaim itself uses the real capability.
UPDATE invitation_issuer_authority_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE state='RUNNING';
SET LOCAL ROLE strataai_worker_runtime;
SELECT * FROM public.claim_invitation_issuer_authority(gen_random_uuid());
RESET ROLE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs j JOIN issuer_clock_running p USING(id)
  WHERE j.attempt_count=2 AND j.updated_at>p.updated_at AND j.created_at=p.created_at AND j.lease_id<>p.lease_id) THEN
  RAISE EXCEPTION 'Reclaimed issuer job did not preserve creation and advance its clock';
 END IF;
END $$;
TRUNCATE issuer_clock_capability;
INSERT INTO issuer_clock_capability SELECT id,event_id,actor_id,worker_id,lease_id FROM invitation_issuer_authority_jobs WHERE state='RUNNING';
SAVEPOINT before_success;
SET LOCAL ROLE strataai_worker_runtime;
DO $$ DECLARE c record; BEGIN
 SELECT * INTO STRICT c FROM issuer_clock_capability;
 IF NOT public.deliver_invitation_issuer_authority(c.id,c.event_id,c.actor_id,c.worker_id,c.lease_id,100) THEN
  RAISE EXCEPTION 'Valid issuer capability did not complete';
 END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs j JOIN issuer_clock_running p USING(id)
  WHERE j.state='SUCCEEDED' AND j.updated_at>p.updated_at AND j.updated_at>=j.completed_at AND j.created_at=p.created_at) THEN
  RAISE EXCEPTION 'Succeeded issuer job did not retain its terminal update clock';
 END IF;
END $$;
CREATE TEMP TABLE issuer_clock_success AS SELECT j.* FROM invitation_issuer_authority_jobs j JOIN issuer_clock_pending p USING(id);
SET LOCAL ROLE strataai_worker_runtime;
DO $$ DECLARE c record; BEGIN
 SELECT * INTO STRICT c FROM issuer_clock_capability;
 IF public.deliver_invitation_issuer_authority(c.id,c.event_id,c.actor_id,c.worker_id,c.lease_id,100) THEN
  RAISE EXCEPTION 'Terminal issuer capability replay was accepted';
 END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs j JOIN issuer_clock_success p USING(id) WHERE to_jsonb(j)<>to_jsonb(p)) THEN
  RAISE EXCEPTION 'Terminal issuer replay changed state or clock';
 END IF;
END $$;
ROLLBACK TO SAVEPOINT before_success;
UPDATE invitation_issuer_authority_jobs SET attempt_count=5,lease_expires_at=clock_timestamp()-interval '1 second' WHERE state='RUNNING';
SET LOCAL ROLE strataai_worker_runtime;
SELECT * FROM public.claim_invitation_issuer_authority(gen_random_uuid());
RESET ROLE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs j JOIN issuer_clock_running p USING(id)
  WHERE j.state='FAILED' AND j.attempt_count=5 AND j.updated_at>p.updated_at AND j.updated_at>=j.failed_at AND j.created_at=p.created_at) THEN
  RAISE EXCEPTION 'Exhausted issuer job did not retain its terminal update clock';
 END IF;
 BEGIN
  UPDATE invitation_issuer_authority_jobs SET state=state WHERE state='FAILED';
  RAISE EXCEPTION 'Terminal issuer history update was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
ROLLBACK;
