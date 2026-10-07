BEGIN;
LOCK TABLE invitation_issuer_authority_jobs IN SHARE ROW EXCLUSIVE MODE;
ALTER TABLE invitation_issuer_authority_jobs
 DROP CONSTRAINT invitation_issuer_authority_jobs_state_check,
 ADD CONSTRAINT invitation_issuer_authority_jobs_state_check CHECK(state IN ('PENDING','RUNNING','SUCCEEDED','FAILED')),
 ADD COLUMN failed_at timestamptz,
 ADD COLUMN failure_code text,
 ADD CONSTRAINT issuer_authority_failure_pair CHECK((failed_at IS NULL)=(failure_code IS NULL)),
 ADD CONSTRAINT issuer_authority_failure_state CHECK((state='FAILED')=(failed_at IS NOT NULL AND failure_code IS NOT NULL)),
 ADD CONSTRAINT issuer_authority_failure_reason CHECK(failure_code IS NULL OR failure_code='LEASE_EXHAUSTED'),
 ADD CONSTRAINT issuer_authority_failure_time CHECK(failed_at IS NULL OR isfinite(failed_at)),
 ADD CONSTRAINT issuer_authority_failure_attempt CHECK(state<>'FAILED' OR attempt_count=5);

CREATE OR REPLACE FUNCTION claim_invitation_issuer_authority(p_worker uuid)
 RETURNS TABLE(job_id uuid,event_id uuid,actor_id uuid,worker_id uuid,lease_id uuid,correlation_id text)
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF p_worker IS NULL OR p_worker='00000000-0000-0000-0000-000000000000' THEN RETURN; END IF;
 -- One row per call: exhausted crash recovery is bounded just like claiming.
 -- No recipient/tenant scan, source replacement, attempt reset or history rewrite.
 RETURN QUERY WITH selected AS (
  SELECT j.id FROM public.invitation_issuer_authority_jobs j
  WHERE (j.state='PENDING' AND j.attempt_count<5)
    OR (j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp())
  ORDER BY j.created_at,j.id LIMIT 1 FOR UPDATE SKIP LOCKED
 ), recovered AS (
  UPDATE public.invitation_issuer_authority_jobs j SET
   state=CASE WHEN j.attempt_count=5 THEN 'FAILED' ELSE 'RUNNING' END,
   attempt_count=CASE WHEN j.attempt_count=5 THEN 5 ELSE j.attempt_count+1 END,
   worker_id=CASE WHEN j.attempt_count=5 THEN NULL ELSE p_worker END,
   lease_id=CASE WHEN j.attempt_count=5 THEN NULL ELSE gen_random_uuid() END,
   lease_expires_at=CASE WHEN j.attempt_count=5 THEN NULL ELSE clock_timestamp()+interval '2 minutes' END,
   failed_at=CASE WHEN j.attempt_count=5 THEN clock_timestamp() ELSE NULL END,
   failure_code=CASE WHEN j.attempt_count=5 THEN 'LEASE_EXHAUSTED' ELSE NULL END
  FROM selected x WHERE j.id=x.id RETURNING j.*
 ) SELECT r.id,r.event_id,r.actor_id,r.worker_id,r.lease_id,s.correlation_id
 FROM recovered r JOIN public.invitation_issuer_authority_sources s ON s.event_id=r.event_id AND s.actor_id=r.actor_id
 WHERE r.state='RUNNING';
END $$;

CREATE OR REPLACE FUNCTION protect_invitation_issuer_job_identity() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$ BEGIN
 IF TG_OP='DELETE' OR NEW.id IS DISTINCT FROM OLD.id OR NEW.event_id IS DISTINCT FROM OLD.event_id
  OR NEW.actor_id IS DISTINCT FROM OLD.actor_id OR NEW.after_tenant IS DISTINCT FROM OLD.after_tenant
  OR NEW.created_at IS DISTINCT FROM OLD.created_at OR OLD.state IN ('SUCCEEDED','FAILED') THEN
  RAISE EXCEPTION 'Invitation issuer job history is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
INSERT INTO schema_migrations(version) VALUES('110_invitation_issuer_authority_exhaustion');
COMMIT;
