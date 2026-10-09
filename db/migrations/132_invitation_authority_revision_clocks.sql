BEGIN;
LOCK TABLE invitation_recipient_authority_revisions IN ACCESS EXCLUSIVE MODE;
-- Legacy counters do not identify their first/last publication times. Source
-- event times and globally deduplicated effects cannot reconstruct those facts.
ALTER TABLE invitation_recipient_authority_revisions ADD COLUMN created_at timestamptz,
 ADD COLUMN updated_at timestamptz;
ALTER TABLE invitation_recipient_authority_revisions ADD CONSTRAINT invitation_authority_revision_clocks
 CHECK((created_at IS NULL OR isfinite(created_at)) AND (updated_at IS NULL OR isfinite(updated_at))
  AND (created_at IS NULL OR updated_at IS NULL OR updated_at>=created_at));
CREATE FUNCTION capture_invitation_authority_revision_clocks() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog AS $$
DECLARE admitted_at timestamptz;
BEGIN
 IF TG_OP='INSERT' THEN
  admitted_at:=clock_timestamp();
  NEW.created_at:=admitted_at; NEW.updated_at:=admitted_at;
 ELSE
  IF NEW.email_normalized IS DISTINCT FROM OLD.email_normalized THEN
   RAISE EXCEPTION 'Invitation authority revision identity is immutable' USING ERRCODE='23514';
  END IF;
  NEW.created_at:=OLD.created_at;
  IF NEW.revision IS NOT DISTINCT FROM OLD.revision THEN
   NEW.updated_at:=OLD.updated_at;
  ELSE
   NEW.updated_at:=GREATEST(clock_timestamp(),OLD.created_at,OLD.updated_at);
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER invitation_authority_revision_clocks BEFORE INSERT OR UPDATE ON invitation_recipient_authority_revisions
 FOR EACH ROW EXECUTE FUNCTION capture_invitation_authority_revision_clocks();
REVOKE ALL ON FUNCTION capture_invitation_authority_revision_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('132_invitation_authority_revision_clocks');
COMMIT;
