DO $$
DECLARE relation text; privilege text; routine text;
BEGIN
 FOREACH relation IN ARRAY ARRAY['record_retention_policies','record_retention_policy_history','record_retention_policy_receipts'] LOOP
  IF NOT EXISTS(SELECT 1 FROM pg_class WHERE oid=('public.'||relation)::regclass AND relrowsecurity AND relforcerowsecurity)
   OR NOT has_table_privilege('strataai_api_runtime','public.'||relation,'SELECT')
   OR has_table_privilege('strataai_worker_runtime','public.'||relation,'SELECT') THEN
   RAISE EXCEPTION 'Retention policy read/RLS capabilities are invalid';
  END IF;
  FOREACH privilege IN ARRAY ARRAY['INSERT','UPDATE','DELETE','TRUNCATE','REFERENCES','TRIGGER'] LOOP
   IF has_table_privilege('strataai_api_runtime','public.'||relation,privilege)
     IS DISTINCT FROM (privilege='INSERT' OR privilege='UPDATE' AND relation='record_retention_policies')
    OR has_table_privilege('strataai_worker_runtime','public.'||relation,privilege) THEN
    RAISE EXCEPTION 'Retention policy restricted command capabilities are invalid';
   END IF;
  END LOOP;
 END LOOP;
 FOREACH routine IN ARRAY ARRAY['guard_retention_policy_current()','journal_retention_policy_history()',
  'guard_retention_policy_complete_source()','guard_retention_policy_receipt()'] LOOP
  IF has_function_privilege('strataai_api_runtime','public.'||routine,'EXECUTE')
   OR has_function_privilege('strataai_worker_runtime','public.'||routine,'EXECUTE') THEN
   RAISE EXCEPTION 'Retention policy internal routine capability escaped';
  END IF;
 END LOOP;
 IF EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid IN ('record_retention_policies'::regclass,
  'record_retention_policy_history'::regclass,'record_retention_policy_receipts'::regclass) AND NOT convalidated) THEN
  RAISE EXCEPTION 'Retention policy unvalidated constraint remains';
 END IF;
END $$;
