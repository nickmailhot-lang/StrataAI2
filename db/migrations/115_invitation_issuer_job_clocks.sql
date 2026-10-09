BEGIN;
LOCK TABLE invitation_issuer_authority_jobs IN ACCESS EXCLUSIVE MODE;
-- A legacy lease deadline cannot prove its latest mutation timestamp. Drain
-- old RUNNING jobs with the existing Worker before retrying this atomic upgrade.
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs WHERE state='RUNNING') THEN
  RAISE EXCEPTION 'Drain running invitation issuer authority jobs before clock upgrade' USING ERRCODE='23514';
 END IF;
END $$;
ALTER TABLE invitation_issuer_authority_jobs ADD COLUMN updated_at timestamptz;
-- Terminal rows are immutable during normal operation. The exclusive lock and
-- transaction bound this historical-only backfill; restore protection before commit.
DROP TRIGGER issuer_job_identity ON invitation_issuer_authority_jobs;
UPDATE invitation_issuer_authority_jobs SET updated_at=CASE state
 WHEN 'SUCCEEDED' THEN completed_at WHEN 'FAILED' THEN failed_at ELSE created_at END;
CREATE TRIGGER issuer_job_identity BEFORE UPDATE OR DELETE ON invitation_issuer_authority_jobs
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_issuer_job_identity();
ALTER TABLE invitation_issuer_authority_jobs
 ALTER COLUMN updated_at SET NOT NULL,
 ADD CONSTRAINT issuer_job_update_clock CHECK(isfinite(updated_at) AND updated_at>=created_at
  AND (completed_at IS NULL OR updated_at>=completed_at) AND (failed_at IS NULL OR updated_at>=failed_at));
CREATE FUNCTION maintain_invitation_issuer_job_clock() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$ BEGIN
 IF TG_OP='INSERT' THEN
  NEW.updated_at:=NEW.created_at;
 ELSE
  IF NEW.updated_at IS DISTINCT FROM OLD.updated_at THEN
   RAISE EXCEPTION 'Invitation issuer update clock is managed by its transition' USING ERRCODE='23514';
  END IF;
  IF (to_jsonb(NEW)-'updated_at') IS DISTINCT FROM (to_jsonb(OLD)-'updated_at') THEN
   NEW.updated_at:=greatest(OLD.updated_at,clock_timestamp(),NEW.completed_at,NEW.failed_at);
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER issuer_job_clock BEFORE INSERT OR UPDATE ON invitation_issuer_authority_jobs
 FOR EACH ROW EXECUTE FUNCTION maintain_invitation_issuer_job_clock();
REVOKE ALL ON FUNCTION maintain_invitation_issuer_job_clock() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('115_invitation_issuer_job_clocks');
COMMIT;
