\set ON_ERROR_STOP on
-- PRD-02 / ARCH-08: actual restored mail login, never administrator SET ROLE.
DO $$ BEGIN
 IF current_user<>session_user OR current_user<>'strataai_identity_mail_runtime'
  OR public.runtime_database_role_is_safe() IS NOT TRUE THEN
  RAISE EXCEPTION 'Restored mail login is not a safe independent identity';
 END IF;
END $$;
BEGIN;
SELECT set_config('app.service_scope','',true);
DO $$ BEGIN
 IF EXISTS(SELECT FROM identity_delivery_jobs) THEN
  RAISE EXCEPTION 'Restored mail missing-context read leaked delivery jobs'; END IF;
 BEGIN
  PERFORM password_hash FROM users;
  RAISE EXCEPTION 'Restored mail acquired credential-column reads';
 EXCEPTION WHEN insufficient_privilege THEN NULL;
 END;
 BEGIN
  PERFORM id FROM cards;
  RAISE EXCEPTION 'Restored mail acquired private Card reads';
 EXCEPTION WHEN insufficient_privilege THEN NULL;
 END;
 BEGIN
  PERFORM id FROM background_jobs;
  RAISE EXCEPTION 'Restored mail acquired the general Worker queue';
 EXCEPTION WHEN insufficient_privilege THEN NULL;
 END;
 BEGIN
  PERFORM public.recover_attachment_scan_page('bacc0000-0000-0000-0000-000000000001',1);
  RAISE EXCEPTION 'Restored mail acquired general Worker recovery';
 EXCEPTION WHEN insufficient_privilege THEN NULL;
 END;
 IF has_table_privilege(current_user,'attachments','MAINTAIN')
  OR has_function_privilege(current_user,'public.runtime_database_role_is_safe()','EXECUTE WITH GRANT OPTION') THEN
  RAISE EXCEPTION 'Restored mail acquired maintenance or delegation'; END IF;
END $$;
SELECT set_config('app.service_scope','GLOBAL_IDENTITY_MAIL',true);
DO $$ DECLARE job public.identity_delivery_jobs%ROWTYPE;
BEGIN
 PERFORM id,email,status,email_verified FROM users;
 PERFORM id,user_id,token_hash,used_at,revoked_at,expires_at FROM password_reset_tokens;
 PERFORM id,user_id,token_hash,used_at,revoked_at,expires_at FROM email_verification_tokens;
 SELECT * INTO STRICT job FROM public.claim_identity_delivery('bacc0000-0000-0000-0000-000000000906');
 IF job.id IS DISTINCT FROM 'bacc0000-0000-0000-0000-000000000905'::uuid
  OR job.state<>'RUNNING' OR job.attempt_count<>1 OR job.lease_id IS NULL THEN
  RAISE EXCEPTION 'Restored mail did not claim its original delivery'; END IF;
 IF public.finish_identity_delivery(job.id,job.lease_id,'bacc0000-0000-0000-0000-000000000907','SENT',NULL,'bacc0000-0000-0000-0000-000000000908') THEN
  RAISE EXCEPTION 'Restored mail acknowledged another process lease'; END IF;
 IF public.finish_identity_delivery(job.id,gen_random_uuid(),job.worker_id,'SENT',NULL,'bacc0000-0000-0000-0000-000000000908') THEN
  RAISE EXCEPTION 'Restored mail acknowledged an incorrect lease'; END IF;
 IF public.finish_identity_delivery(job.id,job.lease_id,job.worker_id,'SENT',NULL,'bacc0000-0000-0000-0000-000000000908') IS NOT TRUE THEN
  RAISE EXCEPTION 'Restored mail could not acknowledge its original lease'; END IF;
 IF public.finish_identity_delivery(job.id,job.lease_id,job.worker_id,'SENT',NULL,'bacc0000-0000-0000-0000-000000000908') THEN
  RAISE EXCEPTION 'Restored mail replayed a completed lease'; END IF;
 IF NOT EXISTS(SELECT FROM identity_delivery_jobs WHERE id=job.id AND state='SENT' AND attempt_count=1
  AND lease_id IS NULL AND worker_id IS NULL AND provider_message_id='bacc0000-0000-0000-0000-000000000908') THEN
  RAISE EXCEPTION 'Restored mail completion lost its receipt'; END IF;
 IF EXISTS(SELECT FROM password_reset_tokens WHERE id=job.id AND used_at IS NOT NULL) THEN
  RAISE EXCEPTION 'Restored mail redeemed the recovery token'; END IF;
END $$;
COMMIT;
