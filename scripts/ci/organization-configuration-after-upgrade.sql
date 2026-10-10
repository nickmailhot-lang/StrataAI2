BEGIN;
SET LOCAL TIME ZONE 'UTC';
DO $$
DECLARE tenant uuid:=gen_random_uuid(); other uuid:=gen_random_uuid(); actor uuid:=gen_random_uuid();
 event uuid:=gen_random_uuid(); key uuid:=gen_random_uuid(); source_at timestamptz:=clock_timestamp();
 source jsonb; changed jsonb; receipt jsonb; runtime_role text:='orgcfg_ci_'||replace(gen_random_uuid()::text,'-','');
BEGIN
 IF EXISTS(SELECT 1 FROM organization_configurations) OR EXISTS(SELECT 1 FROM organization_configuration_history)
  OR EXISTS(SELECT 1 FROM organization_configuration_receipts) OR EXISTS(SELECT 1 FROM organization_configuration_events) THEN
  RAISE EXCEPTION 'Configuration was fabricated during upgrade';
 END IF;
 IF organization_configuration_namespace('gov')<>'GOV'
  OR organization_configuration_namespace(U&'\00a0 reg-example \3000')<>'REG-EXAMPLE' THEN
  RAISE EXCEPTION 'Configuration namespace normalization is inconsistent';
 END IF;
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Configuration contract owner','ACTIVE','unusable-ci-hash',source_at,source_at);
 INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at,organization_type)
 VALUES(tenant,'Configuration contract',actor,'ACTIVE',1,source_at,source_at,'STRATA'),
       (other,'Other configuration contract',actor,'ACTIVE',1,source_at,source_at,'STRATA');
 INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),tenant,actor,'OWNER','ACTIVE',source_at,source_at),
       (gen_random_uuid(),other,actor,'OWNER','ACTIVE',source_at,source_at);
 EXECUTE format('CREATE ROLE %I NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT',runtime_role);
 EXECUTE format('GRANT USAGE ON SCHEMA public TO %I',runtime_role);
 EXECUTE format('GRANT SELECT,UPDATE ON organizations,organization_members TO %I',runtime_role);
 EXECUTE format('GRANT SELECT ON users,boards,board_lists TO %I',runtime_role);
 EXECUTE format('GRANT SELECT,INSERT,UPDATE ON organization_configurations TO %I',runtime_role);
 EXECUTE format('GRANT SELECT,INSERT ON organization_configuration_history,organization_configuration_receipts TO %I',runtime_role);
 EXECUTE format('GRANT SELECT ON organization_configuration_events TO %I',runtime_role);
 EXECUTE format('GRANT EXECUTE ON FUNCTION organization_configuration_namespace(text) TO %I',runtime_role);
 PERFORM set_config('app.tenant_id',tenant::text,true);
 EXECUTE format('SET LOCAL ROLE %I',runtime_role);
 source:=jsonb_build_object('OrganizationId',tenant,'Version',1,
  'Configuration',jsonb_build_object('LegalName','Reviewed legal name','Jurisdiction','CA-BC','Timezone','UTC','CorporationIdentifier','REG-GOV'),
  'OrganizationName','Configuration contract','OrganizationType','STRATA','OrganizationVersion',1,
  'ActorId',actor,'EventId',event,'CorrelationId','configuration-contract','CreatedAt',source_at,'UpdatedAt',source_at);
 receipt:=jsonb_build_object('ActorId',actor,'Key',key,'Fingerprint',repeat('A',64),'Result',source,'ExpiresAt',source_at+interval '24 hours');
 INSERT INTO organization_configurations(tenant_id,version,record_json) VALUES(tenant,1,source);
 INSERT INTO organization_configuration_history(tenant_id,version,record_json) VALUES(tenant,1,source);
 INSERT INTO organization_configuration_receipts(tenant_id,actor_id,key_id,version,receipt_json) VALUES(tenant,actor,key,1,receipt);
 SET CONSTRAINTS organization_configuration_current_history,organization_configuration_history_receipt,
  organization_configuration_history_audit IMMEDIATE;
 SET CONSTRAINTS organization_configuration_current_history,organization_configuration_history_receipt,
  organization_configuration_history_audit DEFERRED;
 IF (SELECT count(*) FROM organization_configuration_history)<>1
  OR (SELECT count(*) FROM organization_configuration_receipts)<>1
  OR (SELECT count(*) FROM organization_configuration_events)<>1
  OR (SELECT event_json->>'EventType' FROM organization_configuration_events WHERE tenant_id=tenant)<>'ORGANIZATION_CONFIGURATION_CHANGED'
  OR (SELECT event_json->>'CreatedAt' FROM organization_configuration_events WHERE tenant_id=tenant)<>source->>'UpdatedAt'
  OR EXISTS(SELECT 1 FROM organization_configuration_events WHERE event_json ?| ARRAY['Configuration','LegalName','CivicAddress','CorporationIdentifier','EmergencyContacts']) THEN
  RAISE EXCEPTION 'Configuration revision effects or safe source are invalid';
 END IF;
 BEGIN
  UPDATE organization_configuration_history SET record_json=record_json WHERE tenant_id=tenant;
  RAISE EXCEPTION 'Configuration history accepted a rewrite';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  UPDATE organization_configuration_receipts SET receipt_json=receipt_json WHERE tenant_id=tenant;
  RAISE EXCEPTION 'Configuration receipt accepted a rewrite';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  INSERT INTO organization_configuration_events SELECT * FROM organization_configuration_events;
  RAISE EXCEPTION 'Runtime forged configuration publication';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 PERFORM set_config('app.tenant_id',other::text,true);
 IF EXISTS(SELECT 1 FROM organization_configurations) OR EXISTS(SELECT 1 FROM organization_configuration_history)
  OR EXISTS(SELECT 1 FROM organization_configuration_receipts) OR EXISTS(SELECT 1 FROM organization_configuration_events) THEN
  RAISE EXCEPTION 'Configuration isolation disclosed another tenant';
 END IF;
 BEGIN
  INSERT INTO organization_configurations(tenant_id,version,record_json) VALUES(tenant,1,source);
  RAISE EXCEPTION 'Configuration scope substitution succeeded';
 EXCEPTION WHEN insufficient_privilege OR check_violation THEN NULL; END;
 changed:=source||jsonb_build_object('OrganizationId',other,'OrganizationName','Other configuration contract','EventId',gen_random_uuid());
 BEGIN
  INSERT INTO organization_configurations(tenant_id,version,record_json) VALUES(other,1,changed);
  RAISE EXCEPTION 'Configuration registration conflict was missed';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 PERFORM set_config('app.tenant_id',tenant::text,true);
 BEGIN
  changed:=source||jsonb_build_object('Version',2,'EventId',gen_random_uuid(),'UpdatedAt',clock_timestamp());
  UPDATE organization_configurations SET version=2,record_json=changed WHERE tenant_id=tenant;
  INSERT INTO organization_configuration_history(tenant_id,version,record_json) VALUES(tenant,2,changed);
  SET CONSTRAINTS organization_configuration_history_receipt IMMEDIATE;
  RAISE EXCEPTION 'Configuration revision committed without its receipt';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 IF (SELECT version FROM organization_configurations WHERE tenant_id=tenant)<>1
  OR (SELECT count(*) FROM organization_configuration_history WHERE tenant_id=tenant)<>1
  OR (SELECT count(*) FROM organization_configuration_events WHERE tenant_id=tenant)<>1 THEN
  RAISE EXCEPTION 'Failed configuration command retained partial effects';
 END IF;
 EXECUTE 'RESET ROLE';
 IF NOT EXISTS(SELECT 1 FROM audit_events WHERE id=event AND tenant_id=tenant
  AND event_type='ORGANIZATION_CONFIGURATION_CHANGED' AND actor_id=actor
  AND entity_type='OrganizationConfiguration' AND entity_id=tenant AND safe_metadata=jsonb_build_object('version',1)
  AND created_at=source_at) OR EXISTS(SELECT 1 FROM audit_events WHERE tenant_id=tenant AND id<>event) THEN
  RAISE EXCEPTION 'Configuration audit did not preserve exact source or atomic rollback';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_class WHERE relname IN ('organization_configurations','organization_configuration_history',
  'organization_configuration_receipts','organization_configuration_events') AND (NOT relrowsecurity OR NOT relforcerowsecurity)) THEN
  RAISE EXCEPTION 'Configuration tables lost forced RLS';
 END IF;
END;
$$;
ROLLBACK;
