BEGIN READ ONLY;
DO $$
DECLARE expected record; relation regclass; actual text[]; capability text;
BEGIN
 FOR expected IN SELECT * FROM (VALUES
  ('organization_configurations',ARRAY['tenant_id']),
  ('organization_configuration_history',ARRAY['tenant_id','version']),
  ('organization_configuration_receipts',ARRAY['tenant_id','actor_id','key_id']),
  ('organization_configuration_events',ARRAY['tenant_id','version'])) AS required(name,columns)
 LOOP
  relation:=to_regclass('public.'||expected.name);
  IF relation IS NULL OR NOT EXISTS(SELECT 1 FROM pg_class WHERE oid=relation AND relrowsecurity AND relforcerowsecurity) THEN
   RAISE EXCEPTION 'Configuration tenant isolation is unavailable';
  END IF;
  SELECT array_agg(a.attname::text ORDER BY k.ordinality) INTO actual
   FROM pg_constraint c CROSS JOIN LATERAL unnest(c.conkey) WITH ORDINALITY k(attnum,ordinality)
   JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum
   WHERE c.conrelid=relation AND c.contype='p';
  IF actual IS DISTINCT FROM expected.columns THEN RAISE EXCEPTION 'Configuration primary identity is invalid'; END IF;
  IF NOT has_table_privilege('strataai_api_runtime',relation,'SELECT')
   OR has_table_privilege('strataai_api_runtime',relation,'DELETE,TRUNCATE,TRIGGER') THEN
   RAISE EXCEPTION 'Configuration API capabilities are invalid';
  END IF;
  FOREACH capability IN ARRAY ARRAY['INSERT','UPDATE','DELETE','TRUNCATE','TRIGGER'] LOOP
   IF has_table_privilege('strataai_worker_runtime',relation,capability) THEN
    RAISE EXCEPTION 'Configuration Worker has direct mutation capability';
   END IF;
  END LOOP;
 END LOOP;
 IF NOT has_table_privilege('strataai_api_runtime','organization_configurations','INSERT')
  OR NOT has_table_privilege('strataai_api_runtime','organization_configurations','UPDATE')
  OR NOT has_table_privilege('strataai_api_runtime','organization_configuration_history','INSERT')
  OR NOT has_table_privilege('strataai_api_runtime','organization_configuration_receipts','INSERT')
  OR has_table_privilege('strataai_api_runtime','organization_configuration_history','UPDATE')
  OR has_table_privilege('strataai_api_runtime','organization_configuration_receipts','UPDATE')
  OR has_table_privilege('strataai_api_runtime','organization_configuration_events','INSERT,UPDATE') THEN
  RAISE EXCEPTION 'Configuration append-only capabilities are invalid';
 END IF;
 FOR expected IN SELECT * FROM (VALUES
  ('organization_configurations',ARRAY['tenant_id'],'organizations',ARRAY['id'],'r',false,false),
  ('organization_configuration_history',ARRAY['tenant_id'],'organizations',ARRAY['id'],'r',false,false),
  ('organization_configuration_receipts',ARRAY['tenant_id'],'organizations',ARRAY['id'],'r',false,false),
  ('organization_configuration_events',ARRAY['tenant_id'],'organizations',ARRAY['id'],'r',false,false),
  ('organization_configurations',ARRAY['intake_board_id','tenant_id'],'boards',ARRAY['id','tenant_id'],'r',false,false),
  ('organization_configuration_history',ARRAY['intake_board_id','tenant_id'],'boards',ARRAY['id','tenant_id'],'r',false,false),
  ('organization_configurations',ARRAY['intake_list_id','intake_board_id','tenant_id'],'board_lists',ARRAY['id','board_id','tenant_id'],'r',false,false),
  ('organization_configuration_history',ARRAY['intake_list_id','tenant_id'],'board_lists',ARRAY['id','tenant_id'],'r',false,false),
  ('organization_configuration_history',ARRAY['actor_id'],'users',ARRAY['id'],'r',false,false),
  ('organization_configuration_receipts',ARRAY['actor_id'],'users',ARRAY['id'],'r',false,false),
  ('organization_configuration_receipts',ARRAY['tenant_id','version'],'organization_configuration_history',ARRAY['tenant_id','version'],'r',false,false),
  ('organization_configuration_events',ARRAY['tenant_id','version'],'organization_configuration_history',ARRAY['tenant_id','version'],'r',false,false),
  ('organization_configurations',ARRAY['tenant_id','version'],'organization_configuration_history',ARRAY['tenant_id','version'],'a',true,true),
  ('organization_configuration_history',ARRAY['tenant_id','version'],'organization_configuration_receipts',ARRAY['tenant_id','version'],'a',true,true),
  ('organization_configuration_history',ARRAY['event_id'],'audit_events',ARRAY['id'],'a',true,true),
  ('organization_configuration_events',ARRAY['event_id'],'audit_events',ARRAY['id'],'r',false,false),
  ('organization_configuration_events',ARRAY['tenant_id','source_membership_id','actor_id'],'organization_members',ARRAY['tenant_id','id','user_id'],'r',false,false)
 ) AS required(source,columns,target,target_columns,deletion,deferred_constraint,initially_deferred)
 LOOP
  IF NOT EXISTS(SELECT 1 FROM pg_constraint c WHERE c.contype='f' AND c.convalidated
   AND c.conrelid=to_regclass('public.'||expected.source) AND c.confrelid=to_regclass('public.'||expected.target)
   AND c.confdeltype::text=expected.deletion AND c.condeferrable=expected.deferred_constraint AND c.condeferred=expected.initially_deferred
   AND ARRAY(SELECT a.attname::text FROM unnest(c.conkey) WITH ORDINALITY k(attnum,n)
    JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum ORDER BY k.n)=expected.columns
   AND ARRAY(SELECT a.attname::text FROM unnest(c.confkey) WITH ORDINALITY k(attnum,n)
    JOIN pg_attribute a ON a.attrelid=c.confrelid AND a.attnum=k.attnum ORDER BY k.n)=expected.target_columns) THEN
   RAISE EXCEPTION 'Configuration relationship integrity is unavailable';
  END IF;
 END LOOP;
 FOR expected IN SELECT * FROM (VALUES
  ('organization_configurations',ARRAY['registration_jurisdiction','registration_identifier']),
  ('organization_configuration_history',ARRAY['event_id']),
  ('organization_configuration_receipts',ARRAY['tenant_id','version']),
  ('organization_configuration_events',ARRAY['event_id'])) AS required(name,columns)
 LOOP
  IF NOT EXISTS(SELECT 1 FROM pg_constraint c WHERE c.conrelid=to_regclass('public.'||expected.name) AND c.contype='u'
   AND ARRAY(SELECT a.attname::text FROM unnest(c.conkey) WITH ORDINALITY k(attnum,n)
    JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum ORDER BY k.n)=expected.columns) THEN
   RAISE EXCEPTION 'Configuration uniqueness is unavailable';
  END IF;
 END LOOP;
 FOREACH capability IN ARRAY ARRAY[
  'public.deliver_organization_configuration_event(uuid,uuid,uuid,uuid,uuid,uuid)',
  'public.discover_organization_configuration_scopes(uuid,integer)',
  'public.claim_organization_configuration_job(uuid)'] LOOP
  IF NOT has_function_privilege('strataai_worker_runtime',capability,'EXECUTE')
   OR has_function_privilege('strataai_api_runtime',capability,'EXECUTE') THEN
   RAISE EXCEPTION 'Configuration delivery capabilities are invalid';
  END IF;
 END LOOP;
END $$;
COMMIT;
