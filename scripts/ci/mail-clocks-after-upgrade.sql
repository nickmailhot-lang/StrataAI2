DO $$ BEGIN
 IF (SELECT count(*) FROM mail_clock_upgrade_fixture)<>3 OR EXISTS(
  SELECT 1 FROM mail_clock_upgrade_fixture f LEFT JOIN invitation_mail_intents m USING(job_id)
  WHERE m.job_id IS NULL OR m.updated_at IS NOT NULL OR (to_jsonb(m)-'updated_at') IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'Legacy mail payload or unknown update clock changed';
 END IF;
END $$;
BEGIN;
UPDATE invitation_mail_intents SET updated_at='infinity' WHERE tenant_id='13100000-0000-0000-0000-000000000101';
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_mail_intents WHERE tenant_id='13100000-0000-0000-0000-000000000101' AND updated_at IS NOT NULL) THEN
  RAISE EXCEPTION 'No-op manufactured legacy mail update provenance';
 END IF;
END $$;
UPDATE invitation_mail_intents SET sender_address='newly-recorded@example.test' WHERE job_id=md5('mail-clock-job-3')::uuid;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_mail_intents WHERE job_id=md5('mail-clock-job-3')::uuid
  AND updated_at>=created_at AND isfinite(updated_at)) THEN
  RAISE EXCEPTION 'Actual legacy mail payload update clock missing';
 END IF;
END $$;
ROLLBACK;
