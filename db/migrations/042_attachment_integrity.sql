BEGIN;
-- No digest is invented for legacy FILE rows. They retain NULL and cannot be
-- admitted to the integrity-bound scanner or controlled delivery. A future
-- explicit private-object verification/backfill is required before publication.
ALTER TABLE attachments ADD COLUMN sha256 text;
ALTER TABLE attachments ADD CONSTRAINT attachments_digest_shape CHECK (
 sha256 IS NULL OR (kind='FILE' AND size_bytes BETWEEN 1 AND 1073741824 AND length(sha256)=64 AND sha256 ~ '^[0-9a-f]{64}$')
);
CREATE FUNCTION enforce_attachment_digest() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.kind='FILE' AND NEW.sha256 IS NULL AND (TG_OP='INSERT' OR (NEW.scan_status='CLEAN' AND NEW.deleted_at IS NULL)) THEN
  RAISE EXCEPTION 'Verified attachment digest is required' USING ERRCODE='23514';
 END IF;
 IF TG_OP='UPDATE' AND OLD.sha256 IS NOT NULL AND NEW.sha256 IS DISTINCT FROM OLD.sha256 THEN
  RAISE EXCEPTION 'Verified attachment digest is immutable' USING ERRCODE='23514';
 END IF;
 IF TG_OP='UPDATE' AND OLD.kind='FILE' AND (NEW.kind IS DISTINCT FROM OLD.kind
   OR NEW.storage_key IS DISTINCT FROM OLD.storage_key OR NEW.size_bytes IS DISTINCT FROM OLD.size_bytes
   OR NEW.mime_type IS DISTINCT FROM OLD.mime_type) THEN
  RAISE EXCEPTION 'Attachment object identity is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER attachments_digest_guard BEFORE INSERT OR UPDATE ON attachments
 FOR EACH ROW EXECUTE FUNCTION enforce_attachment_digest();
INSERT INTO schema_migrations(version) VALUES('042_attachment_integrity');
COMMIT;
