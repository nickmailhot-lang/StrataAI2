BEGIN;
SET LOCAL TIME ZONE 'UTC';
CREATE FUNCTION pg_temp.retention_revision(tenant uuid, policy uuid, actor uuid, membership uuid,
 revision bigint, key uuid, event uuid, created timestamptz, changed timestamptz, receipt boolean DEFAULT true) RETURNS void
LANGUAGE plpgsql AS $$
BEGIN
 INSERT INTO record_retention_policy_history(tenant_id,policy_id,version,record_class,category,jurisdiction,
  anchor,retention_days,action,source,notes,created_at,updated_at,actor_id,membership_id,membership_version,
  actor_label,acting_capacity,event_id,key_id,correlation_id)
 SELECT tenant,policy,revision,p.record_class,p.category,p.jurisdiction,'CREATED',30,'DELETE',
  'Private source must not enter audit metadata',NULL,created,changed,actor,membership,m.version,
  u.display_name,'ORGANIZATION_OWNER',event,key,'retention-schema-fixture'
 FROM record_retention_policies p JOIN users u ON u.id=actor
 JOIN organization_members m ON m.id=membership WHERE p.tenant_id=tenant AND p.id=policy;
 IF receipt THEN
  INSERT INTO record_retention_policy_receipts(tenant_id,actor_id,key_id,policy_id,version,fingerprint,created_at,expires_at)
  VALUES(tenant,actor,key,policy,revision,repeat('A',64),changed,changed+interval '24 hours');
 END IF;
END $$;
DO $$
DECLARE tenant uuid:=gen_random_uuid(); other uuid:=gen_random_uuid(); actor uuid:=gen_random_uuid();
 member uuid:=gen_random_uuid(); policy uuid:=gen_random_uuid(); key1 uuid:=gen_random_uuid();
 key2 uuid:=gen_random_uuid(); event1 uuid:=gen_random_uuid(); at timestamptz:=clock_timestamp();
 read_role text:='retention_ci_'||replace(gen_random_uuid()::text,'-',''); n bigint;
BEGIN
 IF EXISTS(SELECT 1 FROM record_retention_policies) OR EXISTS(SELECT 1 FROM record_retention_policy_history)
  OR EXISTS(SELECT 1 FROM record_retention_policy_receipts) THEN
  RAISE EXCEPTION 'Retention policy was fabricated during upgrade';
 END IF;
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Retention contract owner','ACTIVE','unusable-ci-hash',at,at);
 INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at,organization_type)
 VALUES(tenant,'Retention contract',actor,'ACTIVE',1,at,at,'STRATA'),
       (other,'Other retention contract',actor,'ACTIVE',1,at,at,'STRATA');
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
 VALUES(member,tenant,actor,'OWNER','ACTIVE',at,at);
 PERFORM set_config('app.tenant_id',tenant::text,true);
 PERFORM set_config('app.identity_subject',actor::text,true);
 INSERT INTO record_retention_policies(tenant_id,id,version,record_class,category,jurisdiction,created_at,updated_at)
 VALUES(tenant,policy,1,'OPERATIONAL_WORK','work.card',NULL,at,at);
 PERFORM pg_temp.retention_revision(tenant,policy,actor,member,1,key1,event1,at,at);
 UPDATE record_retention_policies SET version=2,updated_at=at+interval '1 second' WHERE tenant_id=tenant AND id=policy;
 PERFORM pg_temp.retention_revision(tenant,policy,actor,member,2,key2,gen_random_uuid(),at,at+interval '1 second');
 SET CONSTRAINTS ALL IMMEDIATE;
 IF (SELECT count(*) FROM record_retention_policy_history WHERE tenant_id=tenant AND policy_id=policy)<>2
  OR (SELECT count(*) FROM record_retention_policy_receipts WHERE tenant_id=tenant AND policy_id=policy)<>2
  OR NOT EXISTS(SELECT 1 FROM audit_events WHERE id=event1 AND tenant_id=tenant AND actor_id=actor
   AND entity_id=policy AND event_type='RECORD_RETENTION_POLICY_CHANGED'
   AND safe_metadata=jsonb_build_object('version',1,'recordClass','OPERATIONAL_WORK','action','DELETE','actingCapacity','ORGANIZATION_OWNER')) THEN
  RAISE EXCEPTION 'Retention policy graph or fixed audit metadata is incomplete';
 END IF;
 SET CONSTRAINTS ALL DEFERRED;
 BEGIN
  INSERT INTO record_retention_policies(tenant_id,id,version,record_class,category,created_at,updated_at)
  VALUES(tenant,gen_random_uuid(),1,'OPERATIONAL_WORK','WORK.CARD',at,at);
  RAISE EXCEPTION 'Duplicate normalized null jurisdiction namespace admitted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  INSERT INTO record_retention_policies(tenant_id,id,version,record_class,category,jurisdiction,created_at,updated_at)
  VALUES(tenant,gen_random_uuid(),1,'DOCUMENT','document.blank',U&'\00a0',at,at);
  RAISE EXCEPTION 'Whitespace jurisdiction became null policy scope';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO record_retention_policies(tenant_id,id,version,record_class,category,jurisdiction,created_at,updated_at)
  VALUES(tenant,gen_random_uuid(),1,'DOCUMENT','document.specific','ca-bc',at,at);
  INSERT INTO record_retention_policies(tenant_id,id,version,record_class,category,jurisdiction,created_at,updated_at)
  VALUES(tenant,gen_random_uuid(),1,'DOCUMENT','DOCUMENT.SPECIFIC','CA-BC',at,at);
  RAISE EXCEPTION 'Duplicate normalized specific jurisdiction admitted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_policies SET version=4 WHERE tenant_id=tenant AND id=policy;
  RAISE EXCEPTION 'Skipped current policy revision admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_policies SET version=3,category='other.card' WHERE tenant_id=tenant AND id=policy;
  RAISE EXCEPTION 'Policy namespace identity changed';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_policies SET version=3,updated_at=at+interval '2 seconds' WHERE tenant_id=tenant AND id=policy;
  PERFORM pg_temp.retention_revision(tenant,policy,actor,member,3,gen_random_uuid(),gen_random_uuid(),at,at+interval '2 seconds',false);
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Revision without receipt committed';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_policies SET version=3,updated_at=at+interval '2 seconds' WHERE tenant_id=tenant AND id=policy;
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Current revision without history committed';
 EXCEPTION WHEN foreign_key_violation OR check_violation THEN NULL; END;
 BEGIN
  PERFORM pg_temp.retention_revision(tenant,policy,actor,member,3,gen_random_uuid(),gen_random_uuid(),at,at+interval '2 seconds');
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Unpointed history revision committed';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_policies SET version=3,updated_at=at+interval '2 seconds' WHERE tenant_id=tenant AND id=policy;
  INSERT INTO record_retention_policy_history(tenant_id,policy_id,version,record_class,category,jurisdiction,
   anchor,retention_days,action,source,created_at,updated_at,actor_id,membership_id,membership_version,
   actor_label,acting_capacity,event_id,key_id,correlation_id)
  SELECT tenant,policy,3,p.record_class,p.category,p.jurisdiction,'CREATED',30,'DELETE','Private forged source',
   at,at+interval '2 seconds',actor,member,m.version,'Forged actor label','ORGANIZATION_OWNER',gen_random_uuid(),gen_random_uuid(),'retention-forged-fixture'
  FROM record_retention_policies p JOIN organization_members m ON m.id=member WHERE p.tenant_id=tenant AND p.id=policy;
  RAISE EXCEPTION 'Forged historical actor label admitted' USING ERRCODE='23514';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  UPDATE record_retention_policies SET version=3,updated_at=at+interval '2 seconds' WHERE tenant_id=tenant AND id=policy;
  PERFORM pg_temp.retention_revision(tenant,policy,actor,member,3,gen_random_uuid(),gen_random_uuid(),at,at+interval '2 seconds',false);
  INSERT INTO record_retention_policy_receipts(tenant_id,actor_id,key_id,policy_id,version,fingerprint,created_at,expires_at)
  SELECT tenant,actor,h.key_id,policy,3,repeat('A',64),at+interval '3 seconds',at+interval '24 hours 3 seconds'
   FROM record_retention_policy_history h WHERE tenant_id=tenant AND policy_id=policy AND version=3;
  RAISE EXCEPTION 'Receipt unrelated source clock admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_policy_history SET source='Rewritten' WHERE tenant_id=tenant AND policy_id=policy AND version=1;
  RAISE EXCEPTION 'Mutable retention history admitted' USING ERRCODE='23514';
 EXCEPTION WHEN raise_exception THEN NULL; END;
 BEGIN
  DELETE FROM record_retention_policy_receipts WHERE tenant_id=tenant AND actor_id=actor AND key_id=key1;
  RAISE EXCEPTION 'Deleted reserved retention key admitted' USING ERRCODE='23514';
 EXCEPTION WHEN raise_exception THEN NULL; END;
 BEGIN
  DELETE FROM record_retention_policies WHERE tenant_id=tenant AND id=policy;
  RAISE EXCEPTION 'Policy identity deletion admitted' USING ERRCODE='P0001';
 EXCEPTION WHEN check_violation THEN NULL; END;
 PERFORM set_config('app.tenant_id',other::text,true);
 BEGIN
  INSERT INTO record_retention_policies(tenant_id,id,version,record_class,category,created_at,updated_at)
  VALUES(tenant,gen_random_uuid(),1,'DOCUMENT','document.other',at,at);
  RAISE EXCEPTION 'Foreign policy context admitted' USING ERRCODE='23514';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 PERFORM set_config('app.tenant_id',tenant::text,true);
 EXECUTE format('CREATE ROLE %I NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT',read_role);
 EXECUTE format('GRANT USAGE ON SCHEMA public TO %I',read_role);
 EXECUTE format('GRANT SELECT ON record_retention_policies,record_retention_policy_history,record_retention_policy_receipts TO %I',read_role);
 EXECUTE format('SET LOCAL ROLE %I',read_role);
 SELECT count(*) INTO n FROM record_retention_policy_history; IF n<>2 THEN RAISE EXCEPTION 'Owning retained source read failed'; END IF;
 PERFORM set_config('app.tenant_id',other::text,true);
 SELECT count(*) INTO n FROM record_retention_policy_history; IF n<>0 THEN RAISE EXCEPTION 'Foreign history read leaked'; END IF;
 SELECT count(*) INTO n FROM record_retention_policy_receipts; IF n<>0 THEN RAISE EXCEPTION 'Foreign receipt read leaked'; END IF;
 PERFORM set_config('app.tenant_id','',true);
 SELECT count(*) INTO n FROM record_retention_policies; IF n<>0 THEN RAISE EXCEPTION 'Unscoped policy read leaked'; END IF;
 PERFORM set_config('app.tenant_id',tenant::text,true);
 BEGIN
  UPDATE record_retention_policies SET version=3 WHERE tenant_id=tenant AND id=policy;
  RAISE EXCEPTION 'Read-only policy principal mutated source' USING ERRCODE='23514';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 RESET ROLE;
 UPDATE users SET display_name='Changed current actor label',version=version+1,updated_at=clock_timestamp() WHERE id=actor;
 IF NOT EXISTS(SELECT 1 FROM record_retention_policy_history WHERE tenant_id=tenant AND policy_id=policy
  AND version=1 AND actor_label='Retention contract owner' AND membership_id=member AND acting_capacity='ORGANIZATION_OWNER') THEN
  RAISE EXCEPTION 'Historical attribution followed current profile';
 END IF;
 SET CONSTRAINTS ALL IMMEDIATE;
END $$;
ROLLBACK;
