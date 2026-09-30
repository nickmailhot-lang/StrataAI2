BEGIN;

CREATE TABLE invitation_routes (
    token_hash char(64) PRIMARY KEY,
    invitation_id uuid NOT NULL UNIQUE,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    email_normalized text NOT NULL,
    target_surface text NOT NULL CHECK (target_surface IN ('INTERNAL', 'PORTAL')),
    target_role text NOT NULL,
    expires_at timestamptz NOT NULL,
    accepted_at timestamptz,
    revoked_at timestamptz
);

CREATE INDEX ix_invitation_routes_email_pending
    ON invitation_routes(email_normalized, expires_at DESC)
    WHERE accepted_at IS NULL AND revoked_at IS NULL;

CREATE OR REPLACE FUNCTION sync_invitation_route()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM invitation_routes
        WHERE invitation_id = OLD.id;
        RETURN OLD;
    END IF;

    INSERT INTO invitation_routes(
        token_hash,
        invitation_id,
        tenant_id,
        email_normalized,
        target_surface,
        target_role,
        expires_at,
        accepted_at,
        revoked_at)
    VALUES (
        NEW.token_hash,
        NEW.id,
        NEW.tenant_id,
        NEW.email_normalized,
        NEW.target_surface,
        NEW.target_role,
        NEW.expires_at,
        NEW.accepted_at,
        NEW.revoked_at)
    ON CONFLICT (invitation_id)
    DO UPDATE SET
        token_hash = EXCLUDED.token_hash,
        email_normalized = EXCLUDED.email_normalized,
        target_surface = EXCLUDED.target_surface,
        target_role = EXCLUDED.target_role,
        expires_at = EXCLUDED.expires_at,
        accepted_at = EXCLUDED.accepted_at,
        revoked_at = EXCLUDED.revoked_at;

    RETURN NEW;
END;
$$;

CREATE TRIGGER invitations_sync_route
AFTER INSERT OR UPDATE OR DELETE ON invitations
FOR EACH ROW EXECUTE FUNCTION sync_invitation_route();

INSERT INTO invitation_routes(
    token_hash,
    invitation_id,
    tenant_id,
    email_normalized,
    target_surface,
    target_role,
    expires_at,
    accepted_at,
    revoked_at)
SELECT
    token_hash,
    id,
    tenant_id,
    email_normalized,
    target_surface,
    target_role,
    expires_at,
    accepted_at,
    revoked_at
FROM invitations
ON CONFLICT (invitation_id)
DO UPDATE SET
    token_hash = EXCLUDED.token_hash,
    email_normalized = EXCLUDED.email_normalized,
    target_surface = EXCLUDED.target_surface,
    target_role = EXCLUDED.target_role,
    expires_at = EXCLUDED.expires_at,
    accepted_at = EXCLUDED.accepted_at,
    revoked_at = EXCLUDED.revoked_at;

INSERT INTO schema_migrations(version)
VALUES ('005_invitation_routing')
ON CONFLICT (version) DO NOTHING;

COMMIT;
