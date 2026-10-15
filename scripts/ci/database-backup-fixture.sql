\set ON_ERROR_STOP on
BEGIN;
INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
VALUES('bacc0000-0000-0000-0000-000000000099','backup@example.test','BACKUP@EXAMPLE.TEST','Recovery fixture','unusable-fixture','ACTIVE',true,now(),now());
DO $$
DECLARE tenant uuid; board uuid; list uuid;
BEGIN
 FOR n IN 1..2 LOOP
  tenant := ('bacc0000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid;
  board := ('bacc0000-0000-0000-0000-'||lpad((10+n)::text,12,'0'))::uuid;
  list := ('bacc0000-0000-0000-0000-'||lpad((20+n)::text,12,'0'))::uuid;
  PERFORM set_config('app.tenant_id',tenant::text,true);
  INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
  VALUES(tenant,'Recovery Organization '||n,'bacc0000-0000-0000-0000-000000000099','ACTIVE','STRATA',now(),now());
  INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
  VALUES(gen_random_uuid(),tenant,'bacc0000-0000-0000-0000-000000000099','OWNER','ACTIVE',now(),now());
  INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(board,tenant,'Recovery Board',now(),now());
  INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
  VALUES(list,tenant,board,'Recovery List',repeat('5',30),now(),now());
  INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,created_at,updated_at)
  VALUES(gen_random_uuid(),tenant,board,list,'Recovery Card','Préservé 家 🔒',repeat('5',30),now(),now());
  INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
  VALUES(gen_random_uuid(),tenant,'bacc0000-0000-0000-0000-000000000099','RECOVERY_FIXTURE','BOARD',board,'recovery-fixture');
 END LOOP;
END $$;
COMMIT;
