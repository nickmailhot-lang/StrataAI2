BEGIN;
DO $$ DECLARE legacy record;
BEGIN
 IF to_regclass('retention_context_upgrade_fixture') IS NOT NULL THEN
  EXECUTE 'SELECT * FROM retention_context_upgrade_fixture' INTO legacy;
  IF NOT EXISTS(SELECT 1 FROM cards WHERE tenant_id=legacy.tenant AND id=legacy.source AND board_id=legacy.destination)
   OR NOT EXISTS(SELECT 1 FROM record_retention_sources WHERE tenant_id=legacy.tenant AND record_id=legacy.source
    AND authorization_context_version IS NULL AND original_board_id IS NULL AND source_version IS NULL)
   OR NOT EXISTS(SELECT 1 FROM record_retention_holds WHERE tenant_id=legacy.tenant AND id=legacy.hold
    AND authorization_context_version IS NULL AND classification_board_id IS NULL AND application_board_id IS NULL AND application_source_version IS NULL)
   OR EXISTS(SELECT 1 FROM record_retention_hold_history WHERE tenant_id=legacy.tenant AND hold_id=legacy.hold AND authorization_context_version IS NOT NULL) THEN
   RAISE EXCEPTION 'Upgrade invented historical Board authority'; END IF;
 END IF;
END $$;
DO $$ DECLARE tenant uuid:=gen_random_uuid(); actor uuid:=gen_random_uuid(); member uuid:=gen_random_uuid();
 boards uuid[]:=ARRAY[gen_random_uuid(),gen_random_uuid(),gen_random_uuid()]; lists uuid[]:=ARRAY[gen_random_uuid(),gen_random_uuid(),gen_random_uuid()];
 card uuid:=gen_random_uuid(); hold uuid:=gen_random_uuid(); key uuid; i integer; revision integer; at timestamptz:=clock_timestamp()-interval '1 minute';
 children uuid[]:=ARRAY[gen_random_uuid(),gen_random_uuid()]; child_holds uuid[]:=ARRAY[gen_random_uuid(),gen_random_uuid()];
 classes text[]:=ARRAY['OPERATIONAL_WORK','DOCUMENT']; kinds text[]:=ARRAY['CARD_COMMENT','ATTACHMENT'];
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Context owner','ACTIVE','unusable-fixture-hash',at,at);
 INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at,organization_type) VALUES(tenant,'Context fixture',actor,'ACTIVE',at,at,'STRATA');
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at) VALUES(member,tenant,actor,'OWNER','ACTIVE',at,at);
 FOR i IN 1..3 LOOP
  INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(boards[i],tenant,'Context Board',at,at);
  INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES(lists[i],tenant,boards[i],'Context List','a',at,at);
 END LOOP;
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES(card,tenant,boards[1],lists[1],'Private source','a',at,at);
 PERFORM set_config('app.tenant_id',tenant::text,true); PERFORM set_config('app.identity_subject',actor::text,true);
 BEGIN
  INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,category,registered_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id,original_board_id)
  VALUES(tenant,'OPERATIONAL_WORK',card,'CARD','work.context',at,actor,member,1,'Context owner','ORGANIZATION_OWNER',gen_random_uuid(),'context-fixture',boards[2]);
  RAISE EXCEPTION 'Caller supplied classification authority'; EXCEPTION WHEN check_violation THEN NULL; END;
 INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,category,registered_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id)
 VALUES(tenant,'OPERATIONAL_WORK',card,'CARD','work.context',at,actor,member,1,'Context owner','ORGANIZATION_OWNER',gen_random_uuid(),'context-fixture');
 INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
 VALUES(children[1],tenant,card,actor,'Private comment',at,at);
 INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
 VALUES(children[2],tenant,card,actor,'URL','Private attachment','https://example.test/file','NOT_APPLICABLE',at,at);
 FOR i IN 1..2 LOOP
  INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,parent_card_id,category,registered_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id)
  VALUES(tenant,classes[i],children[i],kinds[i],card,'work.context',at,actor,member,1,'Context owner','ORGANIZATION_OWNER',gen_random_uuid(),'context-fixture');
 END LOOP;
 UPDATE cards SET board_id=boards[2],list_id=lists[2],version=2,updated_at=at+interval '1 second' WHERE id=card;
 FOR i IN 1..2 LOOP
  key:=gen_random_uuid();
  INSERT INTO record_retention_holds(tenant_id,id,version,record_class,record_id,status,reason,applied_by,applied_at)
  VALUES(tenant,child_holds[i],1,classes[i],children[i],'ACTIVE','Private reason',actor,at+interval '2 seconds');
  INSERT INTO record_retention_hold_history(tenant_id,hold_id,version,record_class,record_id,status,reason,applied_by,applied_at,
   actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,key_id,correlation_id,changed_at)
  VALUES(tenant,child_holds[i],1,classes[i],children[i],'ACTIVE','Private reason',actor,at+interval '2 seconds',
   actor,member,1,'Context owner','ORGANIZATION_OWNER',gen_random_uuid(),key,'context-fixture',at+interval '2 seconds');
  INSERT INTO record_retention_hold_receipts(tenant_id,actor_id,key_id,hold_id,version,fingerprint,created_at,expires_at)
  VALUES(tenant,actor,key,child_holds[i],1,repeat('A',64),at+interval '2 seconds',at+interval '24 hours 2 seconds');
 END LOOP;
 INSERT INTO record_retention_holds(tenant_id,id,version,record_class,record_id,status,reason,applied_by,applied_at)
 VALUES(tenant,hold,1,'OPERATIONAL_WORK',card,'ACTIVE','Private reason',actor,at+interval '2 seconds');
 FOR revision IN 1..2 LOOP
  IF revision=2 THEN
   UPDATE cards SET board_id=boards[3],list_id=lists[3],version=3,updated_at=at+interval '3 seconds' WHERE id=card;
   UPDATE record_retention_holds SET version=2,status='RELEASED',released_by=actor,released_at=at+interval '4 seconds' WHERE tenant_id=tenant AND id=hold;
  END IF;
  key:=gen_random_uuid();
  INSERT INTO record_retention_hold_history(tenant_id,hold_id,version,record_class,record_id,status,reason,applied_by,applied_at,released_by,released_at,
   actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,key_id,correlation_id,changed_at)
  SELECT tenant,hold,version,record_class,record_id,status,reason,applied_by,applied_at,released_by,released_at,
   actor,member,1,'Context owner','ORGANIZATION_OWNER',gen_random_uuid(),key,'context-fixture',COALESCE(released_at,applied_at)
   FROM record_retention_holds WHERE tenant_id=tenant AND id=hold;
  INSERT INTO record_retention_hold_receipts(tenant_id,actor_id,key_id,hold_id,version,fingerprint,created_at,expires_at)
  SELECT tenant,actor,key,hold,revision,repeat('A',64),COALESCE(released_at,applied_at),COALESCE(released_at,applied_at)+interval '24 hours'
   FROM record_retention_holds WHERE tenant_id=tenant AND id=hold;
 END LOOP;
 SET CONSTRAINTS ALL IMMEDIATE;
 IF NOT EXISTS(SELECT 1 FROM record_retention_sources WHERE tenant_id=tenant AND record_id=card AND authorization_context_version=1 AND original_board_id=boards[1] AND source_version=1)
  OR NOT EXISTS(SELECT 1 FROM record_retention_holds WHERE tenant_id=tenant AND id=hold AND authorization_context_version=1 AND classification_board_id=boards[1] AND application_board_id=boards[2] AND application_source_version=2)
  OR (SELECT count(*) FROM record_retention_hold_history WHERE tenant_id=tenant AND hold_id=hold AND authorization_context_version=1 AND classification_board_id=boards[1] AND application_board_id=boards[2] AND application_source_version=2)<>2 THEN
  RAISE EXCEPTION 'Original classification/application context changed after moves'; END IF;
 FOR i IN 1..2 LOOP
  IF NOT EXISTS(SELECT 1 FROM record_retention_sources WHERE tenant_id=tenant AND record_id=children[i]
   AND authorization_context_version=1 AND original_board_id=boards[1] AND source_version=1)
   OR NOT EXISTS(SELECT 1 FROM record_retention_holds WHERE tenant_id=tenant AND id=child_holds[i]
    AND authorization_context_version=1 AND classification_board_id=boards[1] AND application_board_id=boards[2] AND application_source_version=1)
   OR NOT EXISTS(SELECT 1 FROM record_retention_hold_history WHERE tenant_id=tenant AND hold_id=child_holds[i]
    AND authorization_context_version=1 AND classification_board_id=boards[1] AND application_board_id=boards[2] AND application_source_version=1) THEN
   RAISE EXCEPTION 'Child context did not retain exact original and application parent Boards'; END IF;
 END LOOP;
 BEGIN
  UPDATE record_retention_sources SET original_board_id=boards[3] WHERE tenant_id=tenant AND record_id=card;
  RAISE EXCEPTION 'Historical classification gate rewritten'; EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE record_retention_holds SET application_board_id=boards[3] WHERE tenant_id=tenant AND id=hold;
  RAISE EXCEPTION 'Historical hold gate rewritten'; EXCEPTION WHEN check_violation THEN NULL; END;
 IF EXISTS(SELECT 1 FROM audit_events WHERE tenant_id=tenant AND (safe_metadata ? 'originalBoardId' OR safe_metadata ? 'applicationBoardId' OR safe_metadata ? 'classificationBoardId')) THEN
  RAISE EXCEPTION 'Private context published in audit metadata'; END IF;
END $$;
ROLLBACK;
