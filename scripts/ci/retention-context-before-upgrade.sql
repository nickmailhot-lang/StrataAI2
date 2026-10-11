BEGIN;
CREATE TABLE retention_context_upgrade_fixture(tenant uuid,source uuid,hold uuid,destination uuid);
DO $$ DECLARE tenant uuid:=gen_random_uuid(); actor uuid:=gen_random_uuid(); member uuid:=gen_random_uuid();
 board uuid:=gen_random_uuid(); destination uuid:=gen_random_uuid(); list uuid:=gen_random_uuid(); dest_list uuid:=gen_random_uuid();
 card uuid:=gen_random_uuid(); hold uuid:=gen_random_uuid(); key uuid:=gen_random_uuid(); at timestamptz:=clock_timestamp()-interval '1 minute';
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Legacy context owner','ACTIVE','unusable-fixture-hash',at,at);
 INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at,organization_type) VALUES(tenant,'Legacy context fixture',actor,'ACTIVE',at,at,'STRATA');
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at) VALUES(member,tenant,actor,'OWNER','ACTIVE',at,at);
 INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(board,tenant,'Original context',at,at),(destination,tenant,'Destination context',at,at);
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES(list,tenant,board,'Original List','a',at,at),(dest_list,tenant,destination,'Destination List','a',at,at);
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES(card,tenant,board,list,'Private source','a',at,at);
 PERFORM set_config('app.tenant_id',tenant::text,true); PERFORM set_config('app.identity_subject',actor::text,true);
 INSERT INTO record_retention_sources(tenant_id,record_class,record_id,source_kind,category,registered_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,correlation_id)
 VALUES(tenant,'OPERATIONAL_WORK',card,'CARD','work.context',at,actor,member,1,'Legacy context owner','ORGANIZATION_OWNER',gen_random_uuid(),'context-upgrade');
 INSERT INTO record_retention_holds(tenant_id,id,version,record_class,record_id,status,reason,applied_by,applied_at)
 VALUES(tenant,hold,1,'OPERATIONAL_WORK',card,'ACTIVE','Private reason',actor,at);
 INSERT INTO record_retention_hold_history(tenant_id,hold_id,version,record_class,record_id,status,reason,applied_by,applied_at,actor_id,membership_id,membership_version,actor_label,acting_capacity,event_id,key_id,correlation_id,changed_at)
 VALUES(tenant,hold,1,'OPERATIONAL_WORK',card,'ACTIVE','Private reason',actor,at,actor,member,1,'Legacy context owner','ORGANIZATION_OWNER',gen_random_uuid(),key,'context-upgrade',at);
 INSERT INTO record_retention_hold_receipts(tenant_id,actor_id,key_id,hold_id,version,fingerprint,created_at,expires_at) VALUES(tenant,actor,key,hold,1,repeat('A',64),at,at+interval '24 hours');
 UPDATE cards SET board_id=destination,list_id=dest_list,version=version+1,updated_at=at+interval '1 second' WHERE id=card;
 INSERT INTO retention_context_upgrade_fixture VALUES(tenant,card,hold,destination);
END $$;
COMMIT;
