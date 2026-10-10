BEGIN;
CREATE TABLE configuration_delivery_upgrade_fixture(tenant_id uuid,event_id uuid,event_json jsonb);
DO $$
DECLARE tenant uuid:=gen_random_uuid(); actor uuid:=gen_random_uuid(); event uuid:=gen_random_uuid();
 key uuid:=gen_random_uuid(); at timestamptz:='2026-05-01T12:34:56.123456Z'; source jsonb;
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,email_verified,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Delivery upgrade owner','ACTIVE','unusable-ci-hash',true,at,at);
 INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
 VALUES(tenant,'Delivery upgrade Organization',actor,'ACTIVE','STRATA',at,at);
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),tenant,actor,'OWNER','ACTIVE',at,at);
 PERFORM set_config('app.tenant_id',tenant::text,true);
 source:=jsonb_build_object('OrganizationId',tenant,'Version',1,
  'Configuration',jsonb_build_object('LegalName','Private upgrade legal name','Jurisdiction','CA-BC','Timezone','UTC'),
  'OrganizationName','Delivery upgrade Organization','OrganizationType','STRATA','OrganizationVersion',1,
  'ActorId',actor,'EventId',event,'CorrelationId',repeat('r',256),'CreatedAt',at,'UpdatedAt',at);
 INSERT INTO organization_configurations(tenant_id,version,record_json) VALUES(tenant,1,source);
 INSERT INTO organization_configuration_history(tenant_id,version,record_json) VALUES(tenant,1,source);
 INSERT INTO organization_configuration_receipts(tenant_id,actor_id,key_id,version,receipt_json)
 VALUES(tenant,actor,key,1,jsonb_build_object('ActorId',actor,'Key',key,
  'Fingerprint',repeat('A',64),'Result',source,'ExpiresAt',at+interval '24 hours'));
END $$;
INSERT INTO configuration_delivery_upgrade_fixture SELECT tenant_id,event_id,event_json FROM organization_configuration_events;
COMMIT;
