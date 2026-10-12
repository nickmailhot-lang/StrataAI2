BEGIN;
CREATE FUNCTION pg_temp.register_retention_source(tenant uuid, actor uuid, member uuid, source uuid,
 class text, kind text, parent uuid, at timestamptz, member_version bigint DEFAULT 1) RETURNS void
LANGUAGE plpgsql AS $$ BEGIN
 INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,category,jurisdiction,parent_card_id,
  registered_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id)
 VALUES(tenant,class,source,kind,'private.fixture',NULL,parent,at,actor,member,member_version,
  'Retention source owner','ORGANIZATION_OWNER',gen_random_uuid(),'retention-source-contract');
END $$;
DO $$
DECLARE tenant uuid:=gen_random_uuid(); other uuid:=gen_random_uuid(); actor uuid:=gen_random_uuid();
 member uuid:=gen_random_uuid(); board uuid:=gen_random_uuid(); list uuid:=gen_random_uuid(); card uuid:=gen_random_uuid();
 comment uuid:=gen_random_uuid(); attachment uuid:=gen_random_uuid(); event uuid:=gen_random_uuid();
 foreign_event uuid:=gen_random_uuid(); system_event uuid:=gen_random_uuid();
 at timestamptz:=clock_timestamp()-interval '10 seconds'; read_role text:='retention_source_ci_'||replace(gen_random_uuid()::text,'-',''); n bigint;
BEGIN
 IF EXISTS(SELECT 1 FROM record_retention_sources) THEN RAISE EXCEPTION 'Upgrade fabricated retention classification'; END IF;
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Retention source owner','ACTIVE','unusable-fixture-hash',at,at);
 INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at,organization_type)
 VALUES(tenant,'Retention source fixture',actor,'ACTIVE',1,at,at,'STRATA'),(other,'Other source fixture',actor,'ACTIVE',1,at,at,'STRATA');
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
 VALUES(member,tenant,actor,'OWNER','ACTIVE',at,at);
 PERFORM set_config('app.tenant_id',tenant::text,true); PERFORM set_config('app.identity_subject',actor::text,true);
 INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(board,tenant,'Source Board',at,at);
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES(list,tenant,board,'Source List','a',at,at);
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES(card,tenant,board,list,'Private source','a',at,at);
 INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at) VALUES(comment,tenant,card,actor,'Private comment',at,at);
 INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
 VALUES(attachment,tenant,card,actor,'URL','Private attachment','https://example.test/file','NOT_APPLICABLE',at,at);
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
 VALUES(event,tenant,actor,'SOURCE_FIXTURE','CARD',card,'retention-source-contract',at);
 INSERT INTO audit_events(id,tenant_id,event_type,entity_type,correlation_id,created_at)
 VALUES(foreign_event,other,'SOURCE_FIXTURE','AUDIT','retention-source-contract',at),
       (system_event,NULL,'SOURCE_FIXTURE','AUDIT','retention-source-contract',at);
 PERFORM pg_temp.register_retention_source(tenant,actor,member,card,'OPERATIONAL_WORK','CARD',NULL,at+interval '1 second');
 PERFORM pg_temp.register_retention_source(tenant,actor,member,comment,'OPERATIONAL_WORK','CARD_COMMENT',card,at+interval '1 second');
 PERFORM pg_temp.register_retention_source(tenant,actor,member,attachment,'DOCUMENT','ATTACHMENT',card,at+interval '1 second');
 PERFORM pg_temp.register_retention_source(tenant,actor,member,event,'AUDIT','AUDIT_EVENT',NULL,at+interval '1 second');
 SET CONSTRAINTS ALL IMMEDIATE;
 IF (SELECT count(*) FROM record_retention_sources WHERE tenant_id=tenant)<>4
  OR (SELECT count(*) FROM audit_events WHERE tenant_id=tenant AND event_type='RECORD_RETENTION_SOURCE_REGISTERED')<>4
  OR EXISTS(SELECT 1 FROM audit_events WHERE tenant_id=tenant AND event_type='RECORD_RETENTION_SOURCE_REGISTERED'
   AND (safe_metadata-ARRAY['recordClass','sourceKind','actingCapacity']<>'{}'::jsonb
    OR NOT safe_metadata ?& ARRAY['recordClass','sourceKind','actingCapacity'])) THEN
  RAISE EXCEPTION 'Typed source and private audit graph invalid';
 END IF;
 BEGIN
  PERFORM pg_temp.register_retention_source(tenant,actor,member,gen_random_uuid(),'OPERATIONAL_WORK','CARD',NULL,at+interval '1 second');
  RAISE EXCEPTION 'Unknown producer admitted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  PERFORM pg_temp.register_retention_source(tenant,actor,member,card,'GOVERNANCE','CARD',NULL,at+interval '1 second');
  RAISE EXCEPTION 'Generic Card became Governance record'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  PERFORM pg_temp.register_retention_source(tenant,actor,member,comment,'OPERATIONAL_WORK','CARD_COMMENT',gen_random_uuid(),at+interval '1 second');
  RAISE EXCEPTION 'Substituted child relationship admitted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  PERFORM pg_temp.register_retention_source(tenant,actor,member,card,'OPERATIONAL_WORK','CARD',NULL,at+interval '1 second',2);
  RAISE EXCEPTION 'Forged membership provenance admitted'; EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  PERFORM pg_temp.register_retention_source(tenant,actor,member,card,'OPERATIONAL_WORK','CARD',NULL,clock_timestamp()+interval '1 hour');
  RAISE EXCEPTION 'Future registration clock admitted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  PERFORM pg_temp.register_retention_source(tenant,actor,member,foreign_event,'AUDIT','AUDIT_EVENT',NULL,at+interval '1 second');
  RAISE EXCEPTION 'Cross-tenant producer substituted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  PERFORM pg_temp.register_retention_source(tenant,actor,member,system_event,'AUDIT','AUDIT_EVENT',NULL,at+interval '1 second');
  RAISE EXCEPTION 'System audit misclassified as tenant source'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_sources SET category='substitute' WHERE tenant_id=tenant AND record_id=card;
  RAISE EXCEPTION 'Immutable classification rewritten'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  DELETE FROM record_retention_sources WHERE tenant_id=tenant AND record_id=card;
  RAISE EXCEPTION 'Retained reference deleted'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  DELETE FROM attachments WHERE tenant_id=tenant AND id=attachment;
  RAISE EXCEPTION 'Referenced physical source deleted'; EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 UPDATE organization_members SET version=version+1,updated_at=at+interval '2 seconds' WHERE id=member;
 BEGIN
  PERFORM pg_temp.register_retention_source(tenant,actor,member,card,'OPERATIONAL_WORK','CARD',NULL,at+interval '1 second',2);
  RAISE EXCEPTION 'Registration predates current authority'; EXCEPTION WHEN check_violation THEN NULL; END;
 EXECUTE format('CREATE ROLE %I NOLOGIN',read_role);
 EXECUTE format('GRANT SELECT ON record_retention_sources TO %I',read_role);
 EXECUTE format('SET LOCAL ROLE %I',read_role);
 SELECT count(*) INTO n FROM record_retention_sources;
 IF n<>4 THEN RAISE EXCEPTION 'Owning tenant source read failed'; END IF;
 PERFORM set_config('app.tenant_id',other::text,true);
 SELECT count(*) INTO n FROM record_retention_sources;
 IF n<>0 THEN RAISE EXCEPTION 'Foreign tenant source escaped'; END IF;
 PERFORM set_config('app.tenant_id','',true);
 SELECT count(*) INTO n FROM record_retention_sources;
 IF n<>0 THEN RAISE EXCEPTION 'Unscoped source escaped'; END IF;
 RESET ROLE;
 IF (SELECT count(*) FROM record_retention_sources WHERE tenant_id=tenant)<>4
  OR (SELECT count(*) FROM audit_events WHERE tenant_id=tenant AND event_type='RECORD_RETENTION_SOURCE_REGISTERED')<>4 THEN
  RAISE EXCEPTION 'Refused writes left audit or reference effects'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_class WHERE oid='record_retention_sources'::regclass AND relrowsecurity AND relforcerowsecurity)
  OR EXISTS(SELECT 1 FROM pg_constraint WHERE conrelid='record_retention_sources'::regclass AND NOT convalidated) THEN
  RAISE EXCEPTION 'Retention source RLS or constraint boundary missing'; END IF;
 IF EXISTS(SELECT 1 FROM pg_roles r WHERE r.rolname IN ('strataai_api_runtime','strataai_worker_runtime')
  AND (has_function_privilege(r.oid,'journal_retention_source()','EXECUTE')
   OR has_function_privilege(r.oid,'guard_retention_source_immutable()','EXECUTE'))) THEN
  RAISE EXCEPTION 'Retention source internal routine capability escaped'; END IF;
END $$;
ROLLBACK;
