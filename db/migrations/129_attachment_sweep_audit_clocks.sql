BEGIN;
LOCK TABLE attachment_preview_sweeps,attachment_scan_sweeps IN ACCESS EXCLUSIVE MODE;
-- Old cursor times describe candidate ordering, never checkpoint audit time.
-- Leave both legacy audit clocks unknown; retain every original cursor.
ALTER TABLE attachment_preview_sweeps ADD COLUMN created_at timestamptz,ADD COLUMN updated_at timestamptz;
ALTER TABLE attachment_scan_sweeps ADD COLUMN created_at timestamptz,ADD COLUMN updated_at timestamptz;
ALTER TABLE attachment_preview_sweeps ADD CONSTRAINT preview_sweep_audit_clocks CHECK(
 (created_at IS NULL OR isfinite(created_at)) AND
 (updated_at IS NULL OR (isfinite(updated_at) AND (created_at IS NULL OR updated_at>=created_at))));
ALTER TABLE attachment_scan_sweeps ADD CONSTRAINT scan_sweep_audit_clocks CHECK(
 (created_at IS NULL OR isfinite(created_at)) AND
 (updated_at IS NULL OR (isfinite(updated_at) AND (created_at IS NULL OR updated_at>=created_at))));
CREATE FUNCTION capture_attachment_sweep_audit_clocks() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog AS $$
DECLARE recorded_at timestamptz;
BEGIN
 IF TG_OP='INSERT' THEN
  recorded_at:=clock_timestamp();
  NEW.created_at:=recorded_at;NEW.updated_at:=recorded_at;
 ELSE
  IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id THEN
   RAISE EXCEPTION 'Attachment sweep tenant is immutable' USING ERRCODE='23514';
  END IF;
  NEW.created_at:=OLD.created_at;
  IF (NEW.cursor_created_at,NEW.cursor_id) IS NOT DISTINCT FROM (OLD.cursor_created_at,OLD.cursor_id) THEN
   NEW.updated_at:=OLD.updated_at;
  ELSE
   NEW.updated_at:=GREATEST(clock_timestamp(),OLD.created_at,OLD.updated_at);
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER preview_sweep_audit_clocks BEFORE INSERT OR UPDATE ON attachment_preview_sweeps
 FOR EACH ROW EXECUTE FUNCTION capture_attachment_sweep_audit_clocks();
CREATE TRIGGER scan_sweep_audit_clocks BEFORE INSERT OR UPDATE ON attachment_scan_sweeps
 FOR EACH ROW EXECUTE FUNCTION capture_attachment_sweep_audit_clocks();
REVOKE ALL ON FUNCTION capture_attachment_sweep_audit_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('129_attachment_sweep_audit_clocks');
COMMIT;
