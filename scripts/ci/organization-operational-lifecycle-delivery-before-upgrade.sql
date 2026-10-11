BEGIN;
DO $$ DECLARE tenant uuid:=gen_random_uuid(); actor uuid:=gen_random_uuid(); member uuid:=gen_random_uuid();
 event uuid:=gen_random_uuid(); key uuid:=gen_random_uuid(); at timestamptz:=clock_timestamp(); source jsonb;
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,email_verified,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Lifecycle upgrade owner','ACTIVE','unused-fixture',true,at,at);
 INSERT INTO organizations(id,name,owner_user_id,status,version,organization_type,created_at,updated_at)
 VALUES(tenant,'Lifecycle delivery upgrade',actor,'ACTIVE',1,'STRATA',at,at);
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,version,created_at,updated_at)
 VALUES(member,tenant,actor,'OWNER','ACTIVE',1,at,at);
 PERFORM set_config('app.tenant_id',tenant::text,true);
 UPDATE organizations SET status='SUSPENDED',version=2,operational_lifecycle_version=2,updated_at=at WHERE id=tenant;
 source:=jsonb_build_object('OrganizationId',tenant,'EventId',event,'ActorId',actor,'ActorLabel','Lifecycle upgrade owner',
  'ActingCapacity','ORGANIZATION_OWNER','MembershipId',member,'MembershipVersion',1,'PreviousStatus',0,'Status',4,
  'Version',2,'CorrelationId',repeat('u',256),'CreatedAt',at,'EventType','ORGANIZATION_SUSPENDED',
  'EntityType','Organization','EntityId',tenant);
 INSERT INTO organization_operational_lifecycle_history(tenant_id,version,source_json,source_at) VALUES(tenant,2,source,at);
 INSERT INTO organization_operational_lifecycle_receipts(tenant_id,actor_id,key_id,version,receipt_json)
 VALUES(tenant,actor,key,2,jsonb_build_object('ActorId',actor,'Key',key,'Result',source,'ExpiresAt',at+interval '24 hours',
  'Fingerprint',upper(encode(sha256(convert_to(format('{"organization":"%s","status":4,"expectedVersion":1}',tenant),'UTF8')),'hex'))));
END $$;
COMMIT;
CREATE TABLE operational_lifecycle_delivery_upgrade_fixture AS
 SELECT e.tenant_id,e.version,to_jsonb(e) AS original_event,h.source_json,r.receipt_json
 FROM organization_operational_lifecycle_events e
 JOIN organization_operational_lifecycle_history h USING(tenant_id,version)
 JOIN organization_operational_lifecycle_receipts r USING(tenant_id,version);
