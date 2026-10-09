BEGIN;
LOCK TABLE invitations IN ACCESS EXCLUSIVE MODE;
LOCK TABLE invitation_routes IN ACCESS EXCLUSIVE MODE;
ALTER TABLE invitation_routes ADD COLUMN created_at timestamptz, ADD COLUMN updated_at timestamptz;
-- Organization names are retained publication snapshots, not rename writers.
-- Mutable invitation state follows the canonical invitation's recorded clocks.
UPDATE invitation_routes r SET created_at=i.created_at,updated_at=i.updated_at
 FROM invitations i WHERE i.id=r.invitation_id AND i.tenant_id=r.tenant_id AND i.token_hash=r.token_hash;
ALTER TABLE invitation_routes ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL,
 ADD CONSTRAINT invitation_route_clocks CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at);
CREATE FUNCTION maintain_invitation_route_clocks() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE source_created timestamptz; source_updated timestamptz;
BEGIN
 SELECT i.created_at,i.updated_at INTO source_created,source_updated FROM public.invitations i
  WHERE i.id=NEW.invitation_id AND i.tenant_id=NEW.tenant_id AND i.token_hash=NEW.token_hash;
 IF source_created IS NULL OR source_updated IS NULL THEN
  IF row_security_active(TG_RELID)
   AND NULLIF(current_setting('app.tenant_id',true),'') IS DISTINCT FROM NEW.tenant_id::text THEN
   RAISE EXCEPTION 'Invitation route write requires its owning tenant context' USING ERRCODE='42501';
  END IF;
  RAISE EXCEPTION 'Invitation route clock source is unavailable' USING ERRCODE='23514';
 END IF;
 IF TG_OP='UPDATE' THEN
  IF NEW.created_at IS DISTINCT FROM OLD.created_at
   OR NEW.updated_at IS DISTINCT FROM OLD.updated_at AND NEW.updated_at IS DISTINCT FROM source_updated THEN
   RAISE EXCEPTION 'Invitation route clocks belong to canonical history' USING ERRCODE='23514';
  END IF;
 ELSE
  IF NEW.created_at IS NOT NULL AND NEW.created_at IS DISTINCT FROM source_created
   OR NEW.updated_at IS NOT NULL AND NEW.updated_at IS DISTINCT FROM source_updated THEN
   RAISE EXCEPTION 'Invitation route clocks belong to canonical history' USING ERRCODE='23514';
  END IF;
 END IF;
 NEW.created_at:=source_created; NEW.updated_at:=source_updated;
 RETURN NEW;
END $$;
CREATE TRIGGER invitation_route_clock BEFORE INSERT OR UPDATE ON invitation_routes
 FOR EACH ROW EXECUTE FUNCTION maintain_invitation_route_clocks();
REVOKE ALL ON FUNCTION maintain_invitation_route_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('119_invitation_route_clocks');
COMMIT;
