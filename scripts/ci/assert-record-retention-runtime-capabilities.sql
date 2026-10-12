-- Canonical production provisioning: immutable human classification and minimal
-- current hold scope only. Never grant the Worker private retention capabilities.
DO $$
DECLARE relation text;
BEGIN
 FOREACH relation IN ARRAY ARRAY['record_retention_sources','record_retention_source_receipts'] LOOP
  IF NOT has_table_privilege('strataai_api_runtime',relation,'SELECT')
   OR NOT has_table_privilege('strataai_api_runtime',relation,'INSERT')
   OR has_table_privilege('strataai_api_runtime',relation,'UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
   OR has_any_column_privilege('strataai_api_runtime',relation,'UPDATE,REFERENCES')
   OR has_table_privilege('strataai_worker_runtime',relation,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
   OR has_any_column_privilege('strataai_worker_runtime',relation,'SELECT,INSERT,UPDATE,REFERENCES') THEN
   RAISE EXCEPTION 'Classification runtime capability must be API SELECT/INSERT only';
  END IF;
 END LOOP;
 IF has_table_privilege('strataai_api_runtime','record_retention_holds','SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
  OR has_any_column_privilege('strataai_api_runtime','record_retention_holds','INSERT,UPDATE,REFERENCES')
  OR NOT (SELECT bool_and(has_column_privilege('strataai_api_runtime','record_retention_holds',a.attname,'SELECT')
    = (a.attname IN ('tenant_id','record_class','category_key','record_id','status')))
   FROM pg_attribute a WHERE a.attrelid='record_retention_holds'::regclass AND a.attnum>0 AND NOT a.attisdropped)
  OR has_table_privilege('strataai_worker_runtime','record_retention_holds','SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
  OR has_any_column_privilege('strataai_worker_runtime','record_retention_holds','SELECT,INSERT,UPDATE,REFERENCES') THEN
  RAISE EXCEPTION 'Disposition capability must read exactly five current hold scope columns';
 END IF;
 FOREACH relation IN ARRAY ARRAY['record_retention_hold_history','record_retention_hold_receipts'] LOOP
  IF EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname IN ('strataai_api_runtime','strataai_worker_runtime')
   AND (has_table_privilege(r.oid,relation,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
    OR has_any_column_privilege(r.oid,relation,'SELECT,INSERT,UPDATE,REFERENCES'))) THEN
   RAISE EXCEPTION 'Private hold history or receipt capability escaped';
  END IF;
 END LOOP;
END $$;
