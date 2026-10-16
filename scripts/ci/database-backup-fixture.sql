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
-- Model an enabled mail deployment's database ACLs without activating or
-- rotating any source-cluster identity. Global roles are not in logical dumps.
GRANT USAGE ON SCHEMA public TO strataai_identity_mail_runtime;
GRANT SELECT ON schema_migrations TO strataai_identity_mail_runtime;
GRANT SELECT,UPDATE ON identity_delivery_jobs TO strataai_identity_mail_runtime;
GRANT SELECT(id,email,status,email_verified) ON users TO strataai_identity_mail_runtime;
GRANT SELECT(id,user_id,token_hash,used_at,revoked_at,expires_at)
 ON password_reset_tokens,email_verification_tokens TO strataai_identity_mail_runtime;
GRANT EXECUTE ON FUNCTION public.runtime_database_role_is_safe(),public.claim_identity_delivery(uuid),
 public.finish_identity_delivery(uuid,uuid,uuid,text,text,uuid) TO strataai_identity_mail_runtime;
INSERT INTO password_reset_tokens(id,user_id,token_hash,created_at,expires_at)
VALUES('bacc0000-0000-0000-0000-000000000905','bacc0000-0000-0000-0000-000000000099',repeat('a',64),now(),now()+interval '30 minutes');
INSERT INTO identity_delivery_jobs(id,user_id,purpose,password_reset_token_id,key_id,correlation_id,recipient_email,sender_address,public_origin,provider_account,created_at,expires_at)
VALUES('bacc0000-0000-0000-0000-000000000905','bacc0000-0000-0000-0000-000000000099','RESET_PASSWORD',
 'bacc0000-0000-0000-0000-000000000905','recovery-fixture','recovery-fixture','backup@example.test',
 'sender@example.test','https://app.example.test','fixture',now(),now()+interval '30 minutes');
COMMIT;
