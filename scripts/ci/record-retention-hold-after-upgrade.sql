BEGIN;
CREATE FUNCTION pg_temp.hold_revision(tenant uuid, hold uuid, actor uuid, member uuid, key uuid,
 receipt boolean DEFAULT true) RETURNS void LANGUAGE plpgsql AS $$
DECLARE current_row record; at timestamptz;
BEGIN
 SELECT * INTO current_row FROM record_retention_holds WHERE tenant_id=tenant AND id=hold;
 at:=COALESCE(current_row.released_at,current_row.applied_at);
 INSERT INTO record_retention_hold_history(tenant_id,hold_id,version,record_class,category,record_id,status,reason,
  applied_by,applied_at,released_by,released_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,key_id,correlation_id,changed_at)
 VALUES(tenant,hold,current_row.version,current_row.record_class,current_row.category,current_row.record_id,current_row.status,current_row.reason,
  current_row.applied_by,current_row.applied_at,current_row.released_by,current_row.released_at,actor,member,1,'Hold contract owner',
  'ORGANIZATION_OWNER',gen_random_uuid(),key,'hold-schema-contract',at);
 IF receipt THEN
  INSERT INTO record_retention_hold_receipts(tenant_id,actor_id,key_id,hold_id,version,fingerprint,created_at,expires_at)
  VALUES(tenant,actor,key,hold,current_row.version,repeat('A',64),at,at+interval '24 hours');
 END IF;
END $$;
DO $$
DECLARE tenant uuid:=gen_random_uuid(); other uuid:=gen_random_uuid(); actor uuid:=gen_random_uuid(); member uuid:=gen_random_uuid();
 record uuid:=gen_random_uuid(); holds uuid[]:=ARRAY[gen_random_uuid(),gen_random_uuid(),gen_random_uuid(),gen_random_uuid()];
 apply_key uuid:=gen_random_uuid(); release_key uuid:=gen_random_uuid(); broken uuid:=gen_random_uuid();
 at timestamptz:=clock_timestamp()-interval '1 minute'; shape integer; n bigint; relation text; routine text;
 read_role text:='hold_ci_'||replace(gen_random_uuid()::text,'-','');
BEGIN
 IF EXISTS(SELECT 1 FROM record_retention_holds) OR EXISTS(SELECT 1 FROM record_retention_hold_history)
  OR EXISTS(SELECT 1 FROM record_retention_hold_receipts) THEN RAISE EXCEPTION 'Upgrade fabricated holds'; END IF;
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Hold contract owner','ACTIVE','unusable-fixture-hash',at,at);
 INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at,organization_type)
 VALUES(tenant,'Hold fixture',actor,'ACTIVE',1,at,at,'STRATA'),(other,'Other hold fixture',actor,'ACTIVE',1,at,at,'STRATA');
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at) VALUES(member,tenant,actor,'OWNER','ACTIVE',at,at);
 PERFORM set_config('app.tenant_id',tenant::text,true); PERFORM set_config('app.identity_subject',actor::text,true);
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,correlation_id,created_at)
 VALUES(record,tenant,actor,'HOLD_SOURCE_FIXTURE','AUDIT','hold-schema-contract',at);
 INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,category,registered_at,actor_id,
  membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id)
 VALUES(tenant,'AUDIT',record,'AUDIT_EVENT','audit.fixture',at+interval '1 second',actor,member,1,'Hold contract owner',
  'ORGANIZATION_OWNER',gen_random_uuid(),'hold-schema-contract');
 FOR shape IN 1..4 LOOP
  INSERT INTO record_retention_holds(tenant_id,id,version,record_class,category,record_id,status,reason,applied_by,applied_at)
  VALUES(tenant,holds[shape],1,CASE WHEN shape>1 THEN 'AUDIT' END,CASE WHEN shape=3 THEN 'audit.fixture' END,
   CASE WHEN shape=4 THEN record END,'ACTIVE','Private hold reason',actor,at+interval '2 seconds');
  PERFORM pg_temp.hold_revision(tenant,holds[shape],actor,member,CASE WHEN shape=4 THEN apply_key ELSE gen_random_uuid() END);
 END LOOP;
 SET CONSTRAINTS ALL IMMEDIATE;
 SET CONSTRAINTS ALL DEFERRED;
 UPDATE record_retention_holds SET version=2,status='RELEASED',released_by=actor,released_at=at+interval '3 seconds'
  WHERE tenant_id=tenant AND id=holds[4] AND version=1;
 PERFORM pg_temp.hold_revision(tenant,holds[4],actor,member,release_key);
 SET CONSTRAINTS ALL IMMEDIATE;
 IF (SELECT count(*) FROM record_retention_hold_history WHERE tenant_id=tenant)<>5
  OR (SELECT count(*) FROM record_retention_hold_receipts WHERE tenant_id=tenant)<>5
  OR NOT EXISTS(SELECT 1 FROM record_retention_hold_receipts r JOIN record_retention_hold_history h
    ON(h.tenant_id,h.hold_id,h.version)=(r.tenant_id,r.hold_id,r.version)
    WHERE r.tenant_id=tenant AND r.actor_id=actor AND r.key_id=apply_key AND h.status='ACTIVE' AND h.version=1)
  OR NOT EXISTS(SELECT 1 FROM record_retention_holds WHERE tenant_id=tenant AND id=holds[4] AND version=2 AND status='RELEASED') THEN
  RAISE EXCEPTION 'Exact original apply receipt or released head missing'; END IF;
 IF (SELECT count(*) FROM audit_events WHERE tenant_id=tenant AND entity_type='RETENTION_HOLD')<>5
  OR EXISTS(SELECT 1 FROM audit_events WHERE tenant_id=tenant AND entity_type='RETENTION_HOLD'
   AND (safe_metadata-ARRAY['version','status','scopeKind','actingCapacity']<>'{}'::jsonb
    OR NOT safe_metadata ?& ARRAY['version','status','scopeKind','actingCapacity'])) THEN
  RAISE EXCEPTION 'Hold audit privacy or atomic graph invalid'; END IF;
 BEGIN
  UPDATE record_retention_holds SET version=2,status='RELEASED',released_by=actor,released_at=at+interval '3 seconds',reason='Rewritten'
   WHERE tenant_id=tenant AND id=holds[1];
  RAISE EXCEPTION 'Release rewrote original reason'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_holds SET version=1,status='ACTIVE',released_by=NULL,released_at=NULL WHERE tenant_id=tenant AND id=holds[4];
  RAISE EXCEPTION 'Released hold reactivated'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO record_retention_holds(tenant_id,id,version,record_class,category,record_id,status,reason,applied_by,applied_at)
  VALUES(tenant,gen_random_uuid(),1,'AUDIT','wrong.category',record,'ACTIVE','Private',actor,at+interval '2 seconds');
  RAISE EXCEPTION 'Record category substitution admitted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO record_retention_holds(tenant_id,id,version,record_class,record_id,status,reason,applied_by,applied_at)
  VALUES(tenant,gen_random_uuid(),1,'AUDIT',gen_random_uuid(),'ACTIVE','Private',actor,at+interval '2 seconds');
  RAISE EXCEPTION 'Unregistered record admitted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  SET CONSTRAINTS ALL DEFERRED;
  INSERT INTO record_retention_holds(tenant_id,id,version,status,reason,applied_by,applied_at)
  VALUES(tenant,broken,1,'ACTIVE','Private',actor,at+interval '2 seconds');
  PERFORM pg_temp.hold_revision(tenant,broken,actor,member,gen_random_uuid(),false);
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Missing receipt graph committed'; EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  SET CONSTRAINTS ALL DEFERRED;
  INSERT INTO record_retention_holds(tenant_id,id,version,status,reason,applied_by,applied_at)
  VALUES(tenant,broken,1,'ACTIVE','Private',actor,at+interval '2 seconds');
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Bare current hold committed'; EXCEPTION WHEN foreign_key_violation OR check_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_hold_history SET reason='Rewritten' WHERE tenant_id=tenant AND hold_id=holds[1];
  RAISE EXCEPTION 'Hold history rewritten'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  DELETE FROM record_retention_hold_receipts WHERE tenant_id=tenant AND key_id=apply_key;
  RAISE EXCEPTION 'Original receipt deleted'; EXCEPTION WHEN check_violation THEN NULL; END;
 FOREACH relation IN ARRAY ARRAY['record_retention_holds','record_retention_hold_history','record_retention_hold_receipts'] LOOP
  IF NOT EXISTS(SELECT 1 FROM pg_class WHERE oid=('public.'||relation)::regclass AND relrowsecurity AND relforcerowsecurity)
   OR EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid=('public.'||relation)::regclass AND NOT convalidated)
   OR EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname IN ('strataai_api_runtime','strataai_worker_runtime')
    AND has_table_privilege(r.oid,'public.'||relation,'SELECT,INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')) THEN
   RAISE EXCEPTION 'Hold RLS or unactivated capability boundary invalid'; END IF;
 END LOOP;
 FOREACH routine IN ARRAY ARRAY['guard_retention_hold_current()','journal_retention_hold_history()','guard_retention_hold_receipt()','guard_retention_hold_complete()'] LOOP
  IF EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname IN ('strataai_api_runtime','strataai_worker_runtime')
   AND has_function_privilege(r.oid,'public.'||routine,'EXECUTE')) THEN RAISE EXCEPTION 'Hold routine capability escaped'; END IF;
 END LOOP;
 EXECUTE format('CREATE ROLE %I NOLOGIN',read_role);
 EXECUTE format('GRANT SELECT ON record_retention_holds,record_retention_hold_history,record_retention_hold_receipts TO %I',read_role);
 EXECUTE format('SET LOCAL ROLE %I',read_role);
 SELECT count(*) INTO n FROM record_retention_holds;
 IF n<>4 THEN RAISE EXCEPTION 'Owning hold scope unavailable'; END IF;
 PERFORM set_config('app.tenant_id',other::text,true);
 SELECT (SELECT count(*) FROM record_retention_holds)+(SELECT count(*) FROM record_retention_hold_history)+(SELECT count(*) FROM record_retention_hold_receipts) INTO n;
 IF n<>0 THEN RAISE EXCEPTION 'Foreign hold graph disclosed'; END IF;
 PERFORM set_config('app.tenant_id','',true);
 SELECT (SELECT count(*) FROM record_retention_holds)+(SELECT count(*) FROM record_retention_hold_history)+(SELECT count(*) FROM record_retention_hold_receipts) INTO n;
 IF n<>0 THEN RAISE EXCEPTION 'Unscoped hold graph disclosed'; END IF;
 RESET ROLE;
 IF EXISTS(SELECT 1 FROM record_retention_holds WHERE tenant_id=tenant AND id=broken)
  OR (SELECT count(*) FROM audit_events WHERE tenant_id=tenant AND entity_type='RETENTION_HOLD')<>5 THEN
  RAISE EXCEPTION 'Failed hold graph left effects'; END IF;
END $$;
ROLLBACK;
