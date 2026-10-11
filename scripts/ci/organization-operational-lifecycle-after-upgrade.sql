DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM operational_lifecycle_upgrade_fixture f LEFT JOIN organizations o ON o.id=f.id
  WHERE o.id IS NULL OR (to_jsonb(o)-'operational_lifecycle_version') IS DISTINCT FROM f.original
   OR o.operational_lifecycle_version IS NOT NULL) THEN
  RAISE EXCEPTION 'Lifecycle migration rewrote retained Organization state';
 END IF;
 IF EXISTS(SELECT 1 FROM organization_operational_lifecycle_history) OR EXISTS(SELECT 1 FROM organization_operational_lifecycle_receipts)
  OR EXISTS(SELECT 1 FROM organization_operational_lifecycle_events) THEN
  RAISE EXCEPTION 'Lifecycle migration invented historical sources';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_class WHERE relname IN ('organization_operational_lifecycle_history',
  'organization_operational_lifecycle_receipts','organization_operational_lifecycle_events') AND (NOT relrowsecurity OR NOT relforcerowsecurity)) THEN
  RAISE EXCEPTION 'Lifecycle tenant tables do not force RLS';
 END IF;
END $$;
DROP TABLE operational_lifecycle_upgrade_fixture;
