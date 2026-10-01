BEGIN;
ALTER TABLE invitations ADD CONSTRAINT invitations_creation_scope_unique UNIQUE(id,tenant_id,created_by_user_id);
-- A token-free pointer commits with invitation creation and its audit. Expired
-- keys remain reserved; replay never issues another invitation or bearer token.
CREATE TABLE invitation_creation_replays (
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
    fingerprint text NOT NULL CHECK(fingerprint ~ '^[A-F0-9]{64}$'),
    invitation_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL DEFAULT clock_timestamp()+interval '24 hours',
    PRIMARY KEY(tenant_id,actor_id,key_id),
    CONSTRAINT invitation_creation_replay_invitation_scope FOREIGN KEY(invitation_id,tenant_id,actor_id)
        REFERENCES invitations(id,tenant_id,created_by_user_id) ON DELETE RESTRICT,
    CHECK(expires_at>created_at)
);
ALTER TABLE invitation_creation_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_creation_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_creation_replays_tenant ON invitation_creation_replays
    USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
    WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
INSERT INTO schema_migrations(version) VALUES('022_invitation_creation_replays') ON CONFLICT(version) DO NOTHING;
COMMIT;
