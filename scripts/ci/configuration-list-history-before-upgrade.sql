BEGIN;
DO $$
DECLARE tenant uuid:='14800000-0000-0000-0000-000000000001';
 actor uuid:='14800000-0000-0000-0000-000000000099'; source jsonb;
 at timestamptz:='2026-10-01T12:34:56.123456Z'; key uuid; event uuid;
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,email_verified,created_at,updated_at)
 VALUES(actor,'historical-list@example.test','HISTORICAL-LIST@EXAMPLE.TEST','History fixture','ACTIVE','unusable-fixture-hash',true,at,at);
 INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
 VALUES(tenant,'History fixture',actor,'ACTIVE','STRATA',at,at);
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),tenant,actor,'OWNER','ACTIVE',at,at);
 INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('14800000-0000-0000-0000-000000000011',tenant,'Source',at,at),
 ('14800000-0000-0000-0000-000000000012',tenant,'Destination',at,at);
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('14800000-0000-0000-0000-000000000021',tenant,'14800000-0000-0000-0000-000000000011','Former intake',repeat('5',30),at,at),
 ('14800000-0000-0000-0000-000000000022',tenant,'14800000-0000-0000-0000-000000000011','Current intake',repeat('6',30),at,at);
 PERFORM set_config('app.tenant_id',tenant::text,true);
 event:=gen_random_uuid(); key:=gen_random_uuid();
 source:=jsonb_build_object('OrganizationId',tenant,'Version',1,
  'Configuration',jsonb_build_object('LegalName','History fixture','Jurisdiction','CA-BC','Timezone','UTC',
   'IntakeBoardId','14800000-0000-0000-0000-000000000011','IntakeListId','14800000-0000-0000-0000-000000000021'),
  'OrganizationName','History fixture','OrganizationType','STRATA','OrganizationVersion',1,
  'ActorId',actor,'EventId',event,'CorrelationId','history-list-upgrade','CreatedAt',at,'UpdatedAt',at);
 INSERT INTO organization_configurations(tenant_id,version,record_json) VALUES(tenant,1,source);
 INSERT INTO organization_configuration_history(tenant_id,version,record_json) VALUES(tenant,1,source);
 INSERT INTO organization_configuration_receipts(tenant_id,actor_id,key_id,version,receipt_json)
 VALUES(tenant,actor,key,1,jsonb_build_object('ActorId',actor,'Key',key,'Fingerprint',repeat('A',64),'Result',source,'ExpiresAt',at+interval '24 hours'));
 source:=source||jsonb_build_object('Version',2,'EventId',gen_random_uuid(),'UpdatedAt',at+interval '1 second',
  'Configuration',(source->'Configuration')||jsonb_build_object('IntakeListId','14800000-0000-0000-0000-000000000022'));
 key:=gen_random_uuid();
 UPDATE organization_configurations SET version=2,record_json=source WHERE tenant_id=tenant;
 INSERT INTO organization_configuration_history(tenant_id,version,record_json) VALUES(tenant,2,source);
 INSERT INTO organization_configuration_receipts(tenant_id,actor_id,key_id,version,receipt_json)
 VALUES(tenant,actor,key,2,jsonb_build_object('ActorId',actor,'Key',key,'Fingerprint',repeat('B',64),'Result',source,'ExpiresAt',at+interval '24 hours'));
 -- Before the upgrade even the former intake List is permanently pinned.
 BEGIN
  UPDATE board_lists SET board_id='14800000-0000-0000-0000-000000000012'
   WHERE id='14800000-0000-0000-0000-000000000021';
  RAISE EXCEPTION 'Historical composite parent did not refuse the move';
 EXCEPTION WHEN foreign_key_violation THEN NULL;
 END;
END $$;
COMMIT;
