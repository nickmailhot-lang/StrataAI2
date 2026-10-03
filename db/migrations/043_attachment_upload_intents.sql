BEGIN;
-- Durable metadata before object I/O; expected claims become trusted only after
-- complete server measurement. Retained terminal rows keep retry identities used.
CREATE TABLE attachment_upload_intents (
 id uuid PRIMARY KEY CHECK(id <> '00000000-0000-0000-0000-000000000000'),
 tenant_id uuid NOT NULL, card_id uuid NOT NULL, uploader_id uuid NOT NULL,
 retry_key uuid NOT NULL CHECK(retry_key <> '00000000-0000-0000-0000-000000000000'),
 original_card_version bigint NOT NULL CHECK(original_card_version>0),
 display_name text NOT NULL CHECK(length(display_name) BETWEEN 1 AND 255 AND display_name !~ '^[[:space:]]*$' AND display_name !~ '[[:cntrl:]]'),
 expected_size_bytes bigint NOT NULL CHECK(expected_size_bytes BETWEEN 1 AND 1073741824),
 expected_sha256 text NOT NULL CHECK(length(expected_sha256)=64 AND expected_sha256 ~ '^[0-9a-f]{64}$'),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 updated_at timestamptz NOT NULL CHECK(isfinite(updated_at) AND updated_at>=created_at),
 expires_at timestamptz NOT NULL CHECK(isfinite(expires_at) AND expires_at>created_at AND expires_at<=created_at+interval '24 hours'),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 status text NOT NULL DEFAULT 'PREPARED' CHECK(status IN ('PREPARED','WRITING','RECONCILE','STORED','PUBLISHED','ABANDONED')),
 write_lease_id uuid, write_lease_until timestamptz,
 verified_mime_type text, stored_at timestamptz, published_at timestamptz, abandoned_at timestamptz,
 published_attachment_id uuid,
 UNIQUE(tenant_id,uploader_id,retry_key),
 FOREIGN KEY(card_id,tenant_id) REFERENCES cards(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,uploader_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT,
 FOREIGN KEY(published_attachment_id,tenant_id,card_id) REFERENCES attachments(id,tenant_id,card_id) ON DELETE RESTRICT,
 CHECK((verified_mime_type IS NULL AND stored_at IS NULL) OR
   (verified_mime_type IS NOT NULL AND verified_mime_type ~ '^[a-z0-9!#$&^_.+\-]+/[a-z0-9!#$&^_.+\-]+$'
     AND length(verified_mime_type)<=127 AND stored_at IS NOT NULL AND stored_at>=created_at AND stored_at<=updated_at AND stored_at<expires_at)),
 CHECK((status IN ('PREPARED','RECONCILE') AND write_lease_id IS NULL AND write_lease_until IS NULL
     AND verified_mime_type IS NULL AND stored_at IS NULL AND published_at IS NULL AND abandoned_at IS NULL AND published_attachment_id IS NULL)
   OR (status='WRITING' AND write_lease_id IS NOT NULL AND write_lease_id<>'00000000-0000-0000-0000-000000000000'
     AND write_lease_until IS NOT NULL AND write_lease_until>updated_at AND write_lease_until<=expires_at AND write_lease_until<=updated_at+interval '10 minutes'
     AND verified_mime_type IS NULL AND stored_at IS NULL AND published_at IS NULL AND abandoned_at IS NULL AND published_attachment_id IS NULL)
   OR (status='STORED' AND write_lease_id IS NULL AND write_lease_until IS NULL AND stored_at IS NOT NULL
     AND published_at IS NULL AND abandoned_at IS NULL AND published_attachment_id IS NULL)
   OR (status='PUBLISHED' AND write_lease_id IS NULL AND write_lease_until IS NULL AND stored_at IS NOT NULL
     AND published_at IS NOT NULL AND published_at>=stored_at AND published_at<=updated_at AND published_at<expires_at
     AND abandoned_at IS NULL AND published_attachment_id IS NOT NULL AND published_attachment_id=id)
   OR (status='ABANDONED' AND write_lease_id IS NULL AND write_lease_until IS NULL
     AND published_at IS NULL AND published_attachment_id IS NULL AND abandoned_at IS NOT NULL AND abandoned_at>=created_at AND abandoned_at<=updated_at))
);
CREATE INDEX ix_attachment_upload_intents_reconcile ON attachment_upload_intents(tenant_id,status,expires_at,id)
 WHERE status NOT IN ('PUBLISHED','ABANDONED');
ALTER TABLE attachment_upload_intents ENABLE ROW LEVEL SECURITY;
ALTER TABLE attachment_upload_intents FORCE ROW LEVEL SECURITY;
CREATE POLICY attachment_upload_intents_tenant_isolation ON attachment_upload_intents
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION enforce_attachment_upload_intent() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  IF NEW.status<>'PREPARED' OR NEW.version<>1 OR NEW.updated_at<>NEW.created_at THEN
   RAISE EXCEPTION 'Upload intent must start prepared' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
 END IF;
 IF NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
 IF ROW(NEW.id,NEW.tenant_id,NEW.card_id,NEW.uploader_id,NEW.retry_key,NEW.original_card_version,NEW.display_name,
        NEW.expected_size_bytes,NEW.expected_sha256,NEW.expires_at,NEW.created_at)
    IS DISTINCT FROM ROW(OLD.id,OLD.tenant_id,OLD.card_id,OLD.uploader_id,OLD.retry_key,OLD.original_card_version,OLD.display_name,
        OLD.expected_size_bytes,OLD.expected_sha256,OLD.expires_at,OLD.created_at)
    OR NEW.version<>OLD.version+1 OR NEW.updated_at<OLD.updated_at THEN
  RAISE EXCEPTION 'Upload intent identity and revision are protected' USING ERRCODE='23514';
 END IF;
 IF OLD.stored_at IS NOT NULL AND ROW(NEW.stored_at,NEW.verified_mime_type) IS DISTINCT FROM ROW(OLD.stored_at,OLD.verified_mime_type) THEN
  RAISE EXCEPTION 'Verified upload measurement is immutable' USING ERRCODE='23514';
 END IF;
 IF NOT ((OLD.status='PREPARED' AND NEW.status IN ('WRITING','ABANDONED'))
   OR (OLD.status='WRITING' AND NEW.status IN ('RECONCILE','STORED'))
   OR (OLD.status='RECONCILE' AND NEW.status IN ('PREPARED','STORED','ABANDONED'))
   OR (OLD.status='STORED' AND NEW.status IN ('PUBLISHED','ABANDONED'))
   OR (OLD.status='WRITING' AND NEW.status='WRITING' AND NEW.write_lease_id=OLD.write_lease_id
     AND NEW.write_lease_until>OLD.write_lease_until AND NEW.updated_at<OLD.write_lease_until)) THEN
  RAISE EXCEPTION 'Upload intent transition is unavailable' USING ERRCODE='23514';
 END IF;
 IF (OLD.status='WRITING' AND NEW.status='STORED' AND NEW.updated_at>=OLD.write_lease_until)
   OR (OLD.status='RECONCILE' AND NEW.status IN ('PREPARED','STORED') AND NEW.updated_at>=OLD.expires_at) THEN
  RAISE EXCEPTION 'Upload intent lease or expiry elapsed' USING ERRCODE='23514';
 END IF;
 IF NEW.status='PUBLISHED' AND NOT EXISTS (
  SELECT 1 FROM attachments a WHERE a.id=NEW.id AND a.tenant_id=NEW.tenant_id AND a.card_id=NEW.card_id
   AND a.uploader_id=NEW.uploader_id AND a.kind='FILE' AND a.display_name=NEW.display_name
   AND a.size_bytes=NEW.expected_size_bytes AND a.sha256=NEW.expected_sha256 AND a.mime_type=NEW.verified_mime_type
   AND a.storage_key='attachments/'||replace(NEW.tenant_id::text,'-','')||'/'||replace(NEW.id::text,'-','')
   AND a.scan_status='PENDING' AND a.deleted_at IS NULL) THEN
  RAISE EXCEPTION 'Upload publication requires matching quarantined metadata' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER attachment_upload_intents_guard BEFORE INSERT OR UPDATE ON attachment_upload_intents
 FOR EACH ROW EXECUTE FUNCTION enforce_attachment_upload_intent();
INSERT INTO schema_migrations(version) VALUES('043_attachment_upload_intents');
COMMIT;
