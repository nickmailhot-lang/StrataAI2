BEGIN;
LOCK TABLE invitation_mail_intents IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_mail_intents WHERE NOT isfinite(created_at)) THEN
  RAISE EXCEPTION 'Invitation mail creation clock is not finite' USING ERRCODE='23514';
 END IF;
END $$;
-- Terminal finished_at is not evidence of every earlier admitted payload edit.
-- Preserve original records and explicitly unknown legacy update provenance.
ALTER TABLE invitation_mail_intents ADD COLUMN updated_at timestamptz;
ALTER TABLE invitation_mail_intents ADD CONSTRAINT invitation_mail_update_clock
 CHECK(updated_at IS NULL OR (isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at));
CREATE FUNCTION capture_invitation_mail_update_clock() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 IF NOT isfinite(NEW.created_at) THEN
  RAISE EXCEPTION 'Invitation mail creation clock is not finite' USING ERRCODE='23514';
 END IF;
 IF TG_OP='INSERT' THEN
  NEW.updated_at:=NEW.created_at;
 ELSE
  IF NEW.created_at IS DISTINCT FROM OLD.created_at THEN
   RAISE EXCEPTION 'Invitation mail creation clock is immutable' USING ERRCODE='23514';
  END IF;
  IF (to_jsonb(NEW)-'updated_at') IS NOT DISTINCT FROM (to_jsonb(OLD)-'updated_at') THEN
   NEW.updated_at:=OLD.updated_at;
  ELSE
   NEW.updated_at:=GREATEST(clock_timestamp(),NEW.created_at,OLD.updated_at);
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER invitation_mail_update_clock BEFORE INSERT OR UPDATE ON invitation_mail_intents
 FOR EACH ROW EXECUTE FUNCTION capture_invitation_mail_update_clock();
REVOKE ALL ON FUNCTION capture_invitation_mail_update_clock() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('131_invitation_mail_update_clocks');
COMMIT;
