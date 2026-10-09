BEGIN;
SET LOCAL ROLE strataai_worker_runtime;
SELECT * FROM public.claim_invitation_issuer_authority(gen_random_uuid());
RESET ROLE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs WHERE state='RUNNING') THEN
  RAISE EXCEPTION 'Legacy issuer lease was not drained through exhaustion';
 END IF;
END $$;
UPDATE issuer_clock_upgrade_fixture f SET original=to_jsonb(j) FROM invitation_issuer_authority_jobs j WHERE j.id=f.id;
COMMIT;
