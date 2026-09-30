BEGIN;
CREATE TABLE work_event_streams (
    tenant_id uuid NOT NULL,
    board_id uuid NOT NULL,
    last_sequence bigint NOT NULL DEFAULT 0 CHECK (last_sequence >= 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (tenant_id,board_id),
    FOREIGN KEY (board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT
);
CREATE TABLE work_events (
    tenant_id uuid NOT NULL,
    event_id uuid NOT NULL,
    board_id uuid NOT NULL,
    sequence bigint NOT NULL CHECK (sequence > 0),
    actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    event_type text NOT NULL CHECK (event_type ~ '^[A-Z][A-Z0-9_]{0,79}$'),
    entity_type text NOT NULL CHECK (entity_type IN ('Board','List','Card')),
    entity_id uuid NOT NULL,
    entity_version bigint NOT NULL CHECK (entity_version > 0),
    correlation_id text NOT NULL CHECK (correlation_id ~ '^[A-Za-z0-9._-]{1,64}$'),
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (metadata = '{}'::jsonb),
    created_at timestamptz NOT NULL,
    ready_at timestamptz,
    PRIMARY KEY (tenant_id,event_id),
    UNIQUE (tenant_id,board_id,sequence),
    FOREIGN KEY (tenant_id,board_id) REFERENCES work_event_streams(tenant_id,board_id) ON DELETE RESTRICT
);
CREATE INDEX ix_work_events_pending ON work_events(tenant_id,board_id,sequence) WHERE ready_at IS NULL;
ALTER TABLE work_event_streams ENABLE ROW LEVEL SECURITY;
ALTER TABLE work_event_streams FORCE ROW LEVEL SECURITY;
CREATE POLICY work_event_streams_tenant_isolation ON work_event_streams
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
ALTER TABLE work_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE work_events FORCE ROW LEVEL SECURITY;
CREATE POLICY work_events_tenant_isolation ON work_events
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
INSERT INTO schema_migrations(version) VALUES ('011_work_events') ON CONFLICT(version) DO NOTHING;
COMMIT;
