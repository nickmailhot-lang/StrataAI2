-- PRD-01 FOUND-FR-009: restricted lease-bound finish, payload edits and rollback.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('13100000-0000-0000-0000-000000000102','mail-clock@example.test','MAIL-CLOCK@EXAMPLE.TEST','Clock fixture','ACTIVE',true,'fixture',now(),now());
INSERT INTO organizations(id,name,created_at,updated_at)
 VALUES('13100000-0000-0000-0000-000000000101','Legacy mail clock',now(),now());
INSERT INTO organization_members(id,user_id,tenant_id,role,status)
 VALUES(gen_random_uuid(),'13100000-0000-0000-0000-000000000102','13100000-0000-0000-0000-000000000101','OWNER','ACTIVE');
INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 SELECT md5('mail-clock-invitation-'||n)::uuid,'13100000-0000-0000-0000-000000000101',
 'mail-clock-recipient-'||n||'@example.test',upper('mail-clock-recipient-'||n||'@example.test'),md5('mail-clock-token-'||n)||md5('mail-clock-token-'||n),
 'INTERNAL','MEMBER','13100000-0000-0000-0000-000000000102',now()-interval '2 days',now()+interval '1 day'
 FROM generate_series(1,3) n;
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,state,attempt_count,worker_id,lease_id,lease_expires_at)
 SELECT md5('mail-clock-job-'||n)::uuid,'13100000-0000-0000-0000-000000000101','INVITATION_EMAIL',
 'mail-clock-job-'||n,'13100000-0000-0000-0000-000000000102','invitation-email-delivery','mail-clock',
 jsonb_build_object('invitationId',md5('mail-clock-invitation-'||n)::uuid),'RUNNING',1,
 '13100000-0000-0000-0000-000000000103','13100000-0000-0000-0000-000000000104',now()+interval '5 minutes'
 FROM generate_series(1,3) n;
INSERT INTO invitation_mail_intents(job_id,tenant_id,invitation_id,issuer_id,recipient_email,target_surface,target_role,expires_at,key_id,sender_address,public_origin,provider_account,template_version,state,provider_receipt_id,finished_at,created_at,updated_at)
 SELECT md5('mail-clock-job-'||n)::uuid,i.tenant_id,i.id,i.created_by_user_id,i.invited_email,i.target_surface,i.target_role,i.expires_at,
 'clock-key','sender@example.test','https://example.test','clock-provider',1,
 CASE WHEN n=2 THEN 'SENT' ELSE 'PENDING' END,CASE WHEN n=2 THEN md5('mail-clock-receipt')::uuid END,
 CASE WHEN n=2 THEN now()-interval '1 day' END,i.created_at,'infinity'
 FROM generate_series(1,3) n JOIN invitations i ON i.id=md5('mail-clock-invitation-'||n)::uuid;
-- Historically admissible pending payload change without a recorded clock.
UPDATE invitation_mail_intents SET sender_address='later-sender@example.test' WHERE job_id=md5('mail-clock-job-3')::uuid;

DO $$ BEGIN
 IF (SELECT count(*) FROM invitation_mail_intents WHERE job_id IN (md5('mail-clock-job-1')::uuid,md5('mail-clock-job-2')::uuid)
  AND updated_at=created_at AND isfinite(updated_at))<>2 THEN
  RAISE EXCEPTION 'New mail clock trusted caller-supplied time';
 END IF;
 IF (SELECT count(*) FROM invitation_mail_intents WHERE tenant_id='13100000-0000-0000-0000-000000000101'
  AND isfinite(updated_at) AND updated_at>=created_at)<>3 THEN
  RAISE EXCEPTION 'Mail creation or admitted payload clock unavailable';
 END IF;
 IF has_table_privilege('strataai_worker_runtime','invitation_mail_intents','UPDATE') OR
  has_column_privilege('strataai_worker_runtime','invitation_mail_intents','updated_at','UPDATE') OR
  has_function_privilege('strataai_worker_runtime','capture_invitation_mail_update_clock()','EXECUTE') THEN
  RAISE EXCEPTION 'Mail clock widened Worker authority';
 END IF;
END $$;
CREATE TEMP TABLE mail_clock_baseline AS SELECT job_id,to_jsonb(m) AS body FROM invitation_mail_intents m
 WHERE tenant_id='13100000-0000-0000-0000-000000000101';
SELECT set_config('app.tenant_id','13100000-0000-0000-0000-000000000101',true);
SET LOCAL ROLE strataai_worker_runtime;
DO $$ BEGIN
 IF finish_invitation_mail(md5('mail-clock-job-1')::uuid,'13100000-0000-0000-0000-000000000101',
  '13100000-0000-0000-0000-000000000102','13100000-0000-0000-0000-000000000103',gen_random_uuid(),
  'SENT',NULL,md5('new-mail-clock-receipt')::uuid) THEN
  RAISE EXCEPTION 'Foreign lease completed mail';
 END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_mail_intents m JOIN mail_clock_baseline f USING(job_id) WHERE to_jsonb(m) IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'Refused lease changed mail payload or clock';
 END IF;
END $$;
SET LOCAL ROLE strataai_worker_runtime;
DO $$ BEGIN
 IF NOT finish_invitation_mail(md5('mail-clock-job-1')::uuid,'13100000-0000-0000-0000-000000000101',
  '13100000-0000-0000-0000-000000000102','13100000-0000-0000-0000-000000000103','13100000-0000-0000-0000-000000000104',
  'SENT',NULL,md5('new-mail-clock-receipt')::uuid) THEN
  RAISE EXCEPTION 'Actual live lease could not finish mail';
 END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_mail_intents m JOIN mail_clock_baseline f USING(job_id)
  WHERE m.job_id=md5('mail-clock-job-1')::uuid AND m.state='SENT' AND m.version=2
  AND m.updated_at>=m.finished_at AND m.updated_at>m.created_at AND isfinite(m.updated_at)
  AND (to_jsonb(m)-'state'-'version'-'provider_receipt_id'-'finished_at'-'updated_at')=
      (f.body-'state'-'version'-'provider_receipt_id'-'finished_at'-'updated_at')) THEN
  RAISE EXCEPTION 'Mail terminal update clock or original identity failed';
 END IF;
END $$;
TRUNCATE mail_clock_baseline;
INSERT INTO mail_clock_baseline SELECT job_id,to_jsonb(m) FROM invitation_mail_intents m
 WHERE tenant_id='13100000-0000-0000-0000-000000000101';
SET LOCAL ROLE strataai_worker_runtime;
DO $$ BEGIN
 IF finish_invitation_mail(md5('mail-clock-job-1')::uuid,'13100000-0000-0000-0000-000000000101',
  '13100000-0000-0000-0000-000000000102','13100000-0000-0000-0000-000000000103','13100000-0000-0000-0000-000000000104',
  'SENT',NULL,md5('new-mail-clock-receipt')::uuid) THEN RAISE EXCEPTION 'Duplicate finish was admitted'; END IF;
END $$;
RESET ROLE;
UPDATE invitation_mail_intents SET updated_at='infinity',state=state
 WHERE tenant_id='13100000-0000-0000-0000-000000000101';
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_mail_intents m JOIN mail_clock_baseline f USING(job_id) WHERE to_jsonb(m) IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'Duplicate/no-op restamped mail clock';
 END IF;
 BEGIN
  UPDATE invitation_mail_intents SET created_at=created_at+interval '1 second' WHERE job_id=md5('mail-clock-job-1')::uuid;
  RAISE EXCEPTION 'Mail creation history rewritten';
 EXCEPTION WHEN check_violation THEN
  IF SQLERRM<>'Invitation mail creation clock is immutable' THEN RAISE; END IF;
 END;
 BEGIN
  UPDATE invitation_mail_intents SET target_role='INVALID',updated_at='-infinity'
   WHERE job_id=md5('mail-clock-job-3')::uuid;
  RAISE EXCEPTION 'Invalid mail payload accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF EXISTS(SELECT 1 FROM invitation_mail_intents m JOIN mail_clock_baseline f USING(job_id) WHERE to_jsonb(m) IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'Rejected payload/creation mutation retained effects';
 END IF;
END $$;
ROLLBACK;
\echo 'Invitation mail creation/payload clocks, live lease finish, duplicate/no-op, tamper refusal and rollback passed.'
