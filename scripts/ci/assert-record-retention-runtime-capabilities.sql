-- Canonical production provisioning: immutable human classification and minimal
-- current hold transitions. Never grant the Worker private retention capabilities.
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
 IF has_table_privilege('strataai_api_runtime','record_retention_holds','UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
  OR NOT has_table_privilege('strataai_api_runtime','record_retention_holds','SELECT')
  OR NOT has_table_privilege('strataai_api_runtime','record_retention_holds','INSERT')
  OR has_any_column_privilege('strataai_api_runtime','record_retention_holds','REFERENCES')
  OR NOT (SELECT bool_and(has_column_privilege('strataai_api_runtime','record_retention_holds',a.attname,'UPDATE')
    = (a.attname IN ('version','status','released_by','released_at')))
   FROM pg_attribute a WHERE a.attrelid='record_retention_holds'::regclass AND a.attnum>0 AND NOT a.attisdropped)
  OR has_table_privilege('strataai_worker_runtime','record_retention_holds','SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
  OR has_any_column_privilege('strataai_worker_runtime','record_retention_holds','SELECT,INSERT,UPDATE,REFERENCES') THEN
  RAISE EXCEPTION 'Human hold capability must read/insert current heads and update exactly four transition columns';
 END IF;
 FOREACH relation IN ARRAY ARRAY['record_retention_hold_history','record_retention_hold_receipts'] LOOP
  IF NOT has_table_privilege('strataai_api_runtime',relation,'SELECT')
   OR NOT has_table_privilege('strataai_api_runtime',relation,'INSERT')
   OR has_table_privilege('strataai_api_runtime',relation,'UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
   OR has_any_column_privilege('strataai_api_runtime',relation,'UPDATE,REFERENCES')
   OR has_table_privilege('strataai_worker_runtime',relation,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
   OR has_any_column_privilege('strataai_worker_runtime',relation,'SELECT,INSERT,UPDATE,REFERENCES') THEN
   RAISE EXCEPTION 'Hold history and receipt capability must be immutable and human API only';
  END IF;
 END LOOP;
END $$;
