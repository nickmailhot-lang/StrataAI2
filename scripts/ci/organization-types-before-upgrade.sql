-- Preserve the complete existing payload, membership, clocks and lifecycle.
CREATE TABLE organization_type_upgrade_fixture AS
 SELECT id,to_jsonb(o) AS original FROM organizations o;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM organization_type_upgrade_fixture) THEN
  RAISE EXCEPTION 'Organization type upgrade requires actual legacy Organizations';
 END IF;
END $$;
