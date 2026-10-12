-- Migration-only databases have no activated runtime retention capability.
DO $$
DECLARE relation text;
BEGIN
 FOREACH relation IN ARRAY ARRAY['record_retention_sources','record_retention_source_receipts',
  'record_retention_holds','record_retention_hold_history','record_retention_hold_receipts'] LOOP
  IF to_regclass('public.'||relation) IS NOT NULL AND EXISTS(
   SELECT 1 FROM pg_roles r WHERE r.rolname IN ('strataai_api_runtime','strataai_worker_runtime')
    AND (has_table_privilege(r.oid,'public.'||relation,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
     OR has_any_column_privilege(r.oid,'public.'||relation,'SELECT,INSERT,UPDATE,REFERENCES'))) THEN
   RAISE EXCEPTION 'Unprovisioned retention runtime capability escaped';
  END IF;
 END LOOP;
END $$;
