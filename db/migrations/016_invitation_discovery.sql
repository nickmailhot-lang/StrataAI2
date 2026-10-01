BEGIN;
-- New acceptance acknowledgments prove their actor; legacy accepted rows remain unclaimed.
ALTER TABLE invitations ADD COLUMN accepted_by_user_id uuid REFERENCES users(id) ON DELETE RESTRICT,
    ADD CONSTRAINT invitation_acceptance_actor CHECK(accepted_by_user_id IS NULL OR accepted_at IS NOT NULL);
ALTER TABLE invitation_routes ADD COLUMN accepted_by_user_id uuid REFERENCES users(id) ON DELETE RESTRICT;
-- The invitation's organization label is recipient-visible context, not membership or board access.
ALTER TABLE invitation_routes ADD COLUMN organization_name text;
UPDATE invitation_routes r SET organization_name=o.name FROM organizations o WHERE o.id=r.tenant_id;
ALTER TABLE invitation_routes ALTER COLUMN organization_name SET NOT NULL;
CREATE INDEX invitation_routes_email_cursor ON invitation_routes(email_normalized,invitation_id)
    WHERE accepted_at IS NULL AND revoked_at IS NULL;
CREATE FUNCTION protect_invitation_acceptance() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.accepted_by_user_id IS NOT NULL AND
       (NEW.accepted_by_user_id,NEW.accepted_at,NEW.tenant_id,NEW.email_normalized,NEW.target_surface,NEW.target_role,NEW.created_by_user_id)
       IS DISTINCT FROM
       (OLD.accepted_by_user_id,OLD.accepted_at,OLD.tenant_id,OLD.email_normalized,OLD.target_surface,OLD.target_role,OLD.created_by_user_id) THEN
        RAISE EXCEPTION 'Completed invitation acceptance is immutable' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END;
$$;
CREATE TRIGGER invitations_protect_acceptance BEFORE UPDATE ON invitations
    FOR EACH ROW EXECUTE FUNCTION protect_invitation_acceptance();
CREATE OR REPLACE FUNCTION sync_invitation_route()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='DELETE' THEN
        DELETE FROM invitation_routes WHERE invitation_id=OLD.id;
        RETURN OLD;
    END IF;
    INSERT INTO invitation_routes(token_hash,invitation_id,tenant_id,email_normalized,target_surface,target_role,
        expires_at,accepted_at,revoked_at,accepted_by_user_id,organization_name)
    VALUES(NEW.token_hash,NEW.id,NEW.tenant_id,NEW.email_normalized,NEW.target_surface,NEW.target_role,
        NEW.expires_at,NEW.accepted_at,NEW.revoked_at,NEW.accepted_by_user_id,
        (SELECT name FROM organizations WHERE id=NEW.tenant_id))
    ON CONFLICT(invitation_id) DO UPDATE SET token_hash=EXCLUDED.token_hash,email_normalized=EXCLUDED.email_normalized,
        target_surface=EXCLUDED.target_surface,target_role=EXCLUDED.target_role,expires_at=EXCLUDED.expires_at,
        accepted_at=EXCLUDED.accepted_at,revoked_at=EXCLUDED.revoked_at,accepted_by_user_id=EXCLUDED.accepted_by_user_id;
    RETURN NEW;
END;
$$;
INSERT INTO schema_migrations(version) VALUES('016_invitation_discovery') ON CONFLICT(version) DO NOTHING;
COMMIT;
