BEGIN;
-- Metadata only: binary bytes stay in adopted object storage. Card identity,
-- rather than a duplicated Board ID, remains valid when the Card moves.
CREATE TABLE attachments (
 id uuid PRIMARY KEY CHECK(id <> '00000000-0000-0000-0000-000000000000'),
 tenant_id uuid NOT NULL, card_id uuid NOT NULL, uploader_id uuid NOT NULL,
 kind text NOT NULL CHECK(kind IN ('FILE','URL')),
 display_name text NOT NULL CHECK(length(display_name) BETWEEN 1 AND 255 AND display_name !~ '^[[:space:]]*$' AND display_name !~ '[[:cntrl:]]'),
 mime_type text, size_bytes bigint, storage_key text, url text,
 scan_status text NOT NULL CHECK(scan_status IN ('NOT_APPLICABLE','PENDING','CLEAN','REJECTED','FAILED')),
 scanned_at timestamptz,
 created_at timestamptz NOT NULL, updated_at timestamptz NOT NULL CHECK(updated_at>=created_at),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0), deleted_at timestamptz,
 UNIQUE(id,tenant_id,card_id),
 FOREIGN KEY(card_id,tenant_id) REFERENCES cards(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,uploader_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT,
 CHECK(deleted_at IS NULL OR deleted_at BETWEEN created_at AND updated_at),
 CHECK((kind='URL' AND mime_type IS NULL AND size_bytes IS NULL AND storage_key IS NULL
        AND url IS NOT NULL AND length(url) BETWEEN 1 AND 2048 AND url ~ '^https?://[^/?#[:space:]@]+([/?#]|$)'
        AND url !~ '[[:cntrl:]]' AND scan_status='NOT_APPLICABLE' AND scanned_at IS NULL)
    OR (kind='FILE' AND url IS NULL AND mime_type IS NOT NULL AND length(mime_type)<=127
        AND mime_type ~ '^[a-z0-9!#$&^_.+\-]+/[a-z0-9!#$&^_.+\-]+$'
        AND size_bytes IS NOT NULL AND size_bytes>0 AND storage_key IS NOT NULL
        AND length(storage_key) BETWEEN 1 AND 512 AND storage_key !~ '[[:cntrl:]]'
        AND storage_key !~ E'\\\\' AND storage_key !~ '(^|/)(\.{1,2})?(/|$)'
        AND ((scan_status='PENDING' AND scanned_at IS NULL)
          OR (scan_status IN ('CLEAN','REJECTED','FAILED') AND scanned_at IS NOT NULL
            AND scanned_at BETWEEN created_at AND updated_at))))
);
CREATE UNIQUE INDEX ix_attachments_storage_key ON attachments(storage_key) WHERE storage_key IS NOT NULL;
CREATE INDEX ix_attachments_card_cursor ON attachments(tenant_id,card_id,created_at DESC,id DESC) WHERE deleted_at IS NULL;
ALTER TABLE attachments ENABLE ROW LEVEL SECURITY;
ALTER TABLE attachments FORCE ROW LEVEL SECURITY;
CREATE POLICY attachments_tenant_isolation ON attachments
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
INSERT INTO schema_migrations(version) VALUES('041_attachments');
COMMIT;
