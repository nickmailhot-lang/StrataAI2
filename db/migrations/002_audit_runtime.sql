BEGIN;

CREATE TABLE audit_events (
    id uuid PRIMARY KEY,
    tenant_id uuid REFERENCES organizations(id) ON DELETE RESTRICT,
    actor_id uuid,
    event_type text NOT NULL CHECK (length(btrim(event_type)) > 0),
    entity_type text NOT NULL CHECK (length(btrim(entity_type)) > 0),
    entity_id uuid,
    correlation_id text NOT NULL CHECK (length(btrim(correlation_id)) > 0),
    safe_metadata jsonb NOT NULL DEFAULT '{}'::jsonb,
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_audit_events_tenant_created
    ON audit_events(tenant_id, created_at DESC);

CREATE INDEX ix_audit_events_entity
    ON audit_events(tenant_id, entity_type, entity_id, created_at DESC);

ALTER TABLE audit_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_events FORCE ROW LEVEL SECURITY;

CREATE POLICY audit_events_tenant_isolation ON audit_events
    USING (
        tenant_id IS NULL
        OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id IS NULL
        OR tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

CREATE OR REPLACE FUNCTION prevent_audit_event_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'audit_events are append-only';
END;
$$;

CREATE TRIGGER audit_events_no_update
BEFORE UPDATE ON audit_events
FOR EACH ROW EXECUTE FUNCTION prevent_audit_event_mutation();

CREATE TRIGGER audit_events_no_delete
BEFORE DELETE ON audit_events
FOR EACH ROW EXECUTE FUNCTION prevent_audit_event_mutation();

INSERT INTO schema_migrations(version)
VALUES ('002_audit_runtime')
ON CONFLICT (version) DO NOTHING;

COMMIT;
