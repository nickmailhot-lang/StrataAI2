BEGIN;

-- Existing tenants had no classification. Generic preserves that fact without
-- asserting a strata/legal status from their names or migration time.
ALTER TABLE organizations
 ADD COLUMN organization_type text NOT NULL DEFAULT 'GENERIC',
 ADD CONSTRAINT organizations_supported_type CHECK (organization_type IN
  ('STRATA','HOA','CONDOMINIUM','COOPERATIVE','PROPERTY_MANAGEMENT_COMPANY','GENERIC'));
ALTER TABLE organizations ALTER COLUMN organization_type SET DEFAULT 'STRATA';

-- This increment selects classification at creation. A later type change must
-- use the versioned/audited configuration command, not a general metadata write.
CREATE FUNCTION preserve_organization_creation_type()
RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF NEW.organization_type IS DISTINCT FROM OLD.organization_type THEN
  RAISE EXCEPTION 'Organization classification requires an audited configuration command';
 END IF;
 RETURN NEW;
END;
$$;
REVOKE ALL ON FUNCTION preserve_organization_creation_type() FROM PUBLIC;
CREATE TRIGGER organizations_creation_type_immutable BEFORE UPDATE OF organization_type
 ON organizations FOR EACH ROW EXECUTE FUNCTION preserve_organization_creation_type();

INSERT INTO schema_migrations(version) VALUES('136_organization_types');
COMMIT;
