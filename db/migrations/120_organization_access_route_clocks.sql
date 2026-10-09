BEGIN;
LOCK TABLE organization_members IN ACCESS EXCLUSIVE MODE;
LOCK TABLE user_organization_access IN ACCESS EXCLUSIVE MODE;
ALTER TABLE user_organization_access ADD COLUMN created_at timestamptz;
UPDATE user_organization_access r SET created_at=m.created_at,updated_at=m.updated_at
 FROM organization_members m WHERE m.user_id=r.user_id AND m.tenant_id=r.tenant_id AND m.role=r.role AND m.status=r.status;
ALTER TABLE user_organization_access ALTER COLUMN created_at SET NOT NULL,
 ADD CONSTRAINT organization_access_route_clocks CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at);
CREATE FUNCTION maintain_organization_access_route_clocks() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE source_created timestamptz; source_updated timestamptz;
BEGIN
 SELECT m.created_at,m.updated_at INTO source_created,source_updated FROM public.organization_members m
  WHERE m.user_id=NEW.user_id AND m.tenant_id=NEW.tenant_id AND m.role=NEW.role AND m.status=NEW.status;
 IF source_created IS NULL OR source_updated IS NULL THEN
  IF row_security_active(TG_RELID)
   AND NULLIF(current_setting('app.tenant_id',true),'') IS DISTINCT FROM NEW.tenant_id::text THEN
   RAISE EXCEPTION 'Organization access route write requires its owning tenant context' USING ERRCODE='42501';
  END IF;
  RAISE EXCEPTION 'Organization access route clock source is unavailable' USING ERRCODE='23514';
 END IF;
 IF TG_OP='UPDATE' THEN
  IF NEW.created_at IS DISTINCT FROM OLD.created_at
   OR NEW.updated_at IS DISTINCT FROM OLD.updated_at AND NEW.updated_at IS DISTINCT FROM source_updated THEN
   RAISE EXCEPTION 'Organization access route clocks belong to membership history' USING ERRCODE='23514';
  END IF;
 ELSE
  IF NEW.created_at IS NOT NULL AND NEW.created_at IS DISTINCT FROM source_created
   OR NEW.updated_at IS NOT NULL AND NEW.updated_at IS DISTINCT FROM source_updated THEN
   RAISE EXCEPTION 'Organization access route clocks belong to membership history' USING ERRCODE='23514';
  END IF;
 END IF;
 NEW.created_at:=source_created; NEW.updated_at:=source_updated;
 RETURN NEW;
END $$;
CREATE TRIGGER organization_access_route_clock BEFORE INSERT OR UPDATE ON user_organization_access
 FOR EACH ROW EXECUTE FUNCTION maintain_organization_access_route_clocks();
REVOKE ALL ON FUNCTION maintain_organization_access_route_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('120_organization_access_route_clocks');
COMMIT;
