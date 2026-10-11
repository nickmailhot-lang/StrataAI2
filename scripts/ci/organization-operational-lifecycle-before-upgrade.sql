CREATE TABLE operational_lifecycle_upgrade_fixture AS SELECT id,to_jsonb(organizations) AS original FROM organizations;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM operational_lifecycle_upgrade_fixture) THEN
  RAISE EXCEPTION 'Lifecycle upgrade fixture requires retained Organizations';
 END IF;
END $$;
