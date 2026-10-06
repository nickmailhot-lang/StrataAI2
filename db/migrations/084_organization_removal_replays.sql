BEGIN;
CREATE TABLE organization_removal_replays (
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
    fingerprint text NOT NULL CHECK(fingerprint ~ '^[0-9A-F]{64}$'),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    PRIMARY KEY(tenant_id,actor_id,key_id),
    CHECK(expires_at>created_at)
);
ALTER TABLE organization_removal_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_removal_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_removal_replays_tenant ON organization_removal_replays
    USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
    WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
INSERT INTO schema_migrations(version) VALUES('084_organization_removal_replays') ON CONFLICT(version) DO NOTHING;
COMMIT;
