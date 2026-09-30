BEGIN;
-- A pending claim and its result commit together with domain state and audit.
-- Keys remain reserved after expiry: an old retry must never create another item.
CREATE TABLE work_command_replays (
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    key_id uuid NOT NULL CHECK (key_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    fingerprint text NOT NULL CHECK (fingerprint ~ '^[A-F0-9]{64}$'),
    result_json jsonb,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL DEFAULT clock_timestamp() + interval '24 hours',
    PRIMARY KEY (tenant_id, actor_id, key_id),
    CHECK (expires_at > created_at)
);
CREATE INDEX ix_work_command_replays_expiry ON work_command_replays(tenant_id, expires_at);
ALTER TABLE work_command_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE work_command_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY work_command_replays_tenant_isolation ON work_command_replays
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
INSERT INTO schema_migrations(version) VALUES ('010_work_command_replays') ON CONFLICT(version) DO NOTHING;
COMMIT;
