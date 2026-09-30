BEGIN;

CREATE TABLE user_organization_access (
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    role text NOT NULL CHECK (role IN ('OWNER', 'ADMIN', 'MEMBER')),
    status text NOT NULL CHECK (status IN ('ACTIVE', 'SUSPENDED', 'REMOVED')),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, tenant_id)
);

CREATE INDEX ix_user_organization_access_active
    ON user_organization_access(user_id, status, tenant_id);

CREATE OR REPLACE FUNCTION sync_user_organization_access()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM user_organization_access
        WHERE user_id = OLD.user_id
          AND tenant_id = OLD.tenant_id;
        RETURN OLD;
    END IF;

    INSERT INTO user_organization_access(
        user_id, tenant_id, role, status, updated_at)
    VALUES (
        NEW.user_id, NEW.tenant_id, NEW.role, NEW.status, NEW.updated_at)
    ON CONFLICT (user_id, tenant_id)
    DO UPDATE SET
        role = EXCLUDED.role,
        status = EXCLUDED.status,
        updated_at = EXCLUDED.updated_at;

    RETURN NEW;
END;
$$;

CREATE TRIGGER organization_members_sync_access
AFTER INSERT OR UPDATE OR DELETE ON organization_members
FOR EACH ROW EXECUTE FUNCTION sync_user_organization_access();

INSERT INTO user_organization_access(
    user_id, tenant_id, role, status, updated_at)
SELECT
    user_id, tenant_id, role, status, updated_at
FROM organization_members
ON CONFLICT (user_id, tenant_id)
DO UPDATE SET
    role = EXCLUDED.role,
    status = EXCLUDED.status,
    updated_at = EXCLUDED.updated_at;

INSERT INTO schema_migrations(version)
VALUES ('004_organization_access_routing')
ON CONFLICT (version) DO NOTHING;

COMMIT;
