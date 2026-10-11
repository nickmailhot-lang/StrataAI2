BEGIN;
ALTER TABLE organizations DROP CONSTRAINT organizations_status_check;
ALTER TABLE organizations ADD CONSTRAINT organizations_status_check
 CHECK(status IN ('ACTIVE','ARCHIVED','DELETING','DELETED','SUSPENDED','DEACTIVATED'));
ALTER TABLE organization_members ADD CONSTRAINT organization_members_lifecycle_identity UNIQUE(id,tenant_id,user_id);

CREATE TABLE organization_operational_lifecycle_history (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 version bigint NOT NULL CHECK(version>1),
 source_json jsonb NOT NULL CHECK(jsonb_typeof(source_json)='object' AND octet_length(source_json::text)<=8192),
 event_id uuid GENERATED ALWAYS AS ((source_json->>'EventId')::uuid) STORED NOT NULL UNIQUE,
 actor_id uuid GENERATED ALWAYS AS ((source_json->>'ActorId')::uuid) STORED NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 membership_id uuid GENERATED ALWAYS AS ((source_json->>'MembershipId')::uuid) STORED NOT NULL,
 source_at timestamptz NOT NULL CHECK(isfinite(source_at)),
 PRIMARY KEY(tenant_id,version),
 FOREIGN KEY(membership_id,tenant_id,actor_id) REFERENCES organization_members(id,tenant_id,user_id) ON DELETE RESTRICT,
 CHECK(source_json->>'OrganizationId' IS NOT NULL AND source_json->>'Version' IS NOT NULL
  AND source_json->>'OrganizationId'=tenant_id::text AND (source_json->>'Version')::bigint=version),
 CHECK(event_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 CHECK((source_json->>'Status')::integer IN (0,4,5) AND (source_json->>'PreviousStatus')::integer IN (0,4,5)
  AND source_json->>'Status' IS DISTINCT FROM source_json->>'PreviousStatus'),
 CHECK((source_json->>'MembershipVersion')::bigint>0),
 CHECK(length(btrim(source_json->>'ActorLabel')) BETWEEN 1 AND 160 AND source_json->>'ActorLabel' !~ '[[:cntrl:]]'),
 CHECK(source_json->>'ActingCapacity' IN ('ORGANIZATION_OWNER','ORGANIZATION_ADMIN')),
 CHECK(length(btrim(source_json->>'CorrelationId')) BETWEEN 1 AND 256 AND source_json->>'CorrelationId' !~ '[[:cntrl:]]'),
 CHECK(source_json->>'CreatedAt' IS NOT NULL AND (source_json->>'CreatedAt')::timestamptz=source_at)
);
CREATE TABLE organization_operational_lifecycle_receipts (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 version bigint NOT NULL,
 receipt_json jsonb NOT NULL CHECK(jsonb_typeof(receipt_json)='object' AND octet_length(receipt_json::text)<=10240),
 PRIMARY KEY(tenant_id,actor_id,key_id), UNIQUE(tenant_id,version),
 FOREIGN KEY(tenant_id,version) REFERENCES organization_operational_lifecycle_history(tenant_id,version) ON DELETE RESTRICT
);
ALTER TABLE organization_operational_lifecycle_history ADD CONSTRAINT operational_lifecycle_history_receipt
 FOREIGN KEY(tenant_id,version) REFERENCES organization_operational_lifecycle_receipts(tenant_id,version) DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE organization_operational_lifecycle_history ADD CONSTRAINT operational_lifecycle_history_audit
 FOREIGN KEY(event_id) REFERENCES audit_events(id) DEFERRABLE INITIALLY DEFERRED;
CREATE TABLE organization_operational_lifecycle_events (
 tenant_id uuid NOT NULL, version bigint NOT NULL,
 event_id uuid NOT NULL UNIQUE REFERENCES audit_events(id) ON DELETE RESTRICT,
 event_json jsonb NOT NULL CHECK(jsonb_typeof(event_json)='object'),
 PRIMARY KEY(tenant_id,version),
 FOREIGN KEY(tenant_id,version) REFERENCES organization_operational_lifecycle_history(tenant_id,version) ON DELETE RESTRICT
);
ALTER TABLE organizations ADD COLUMN operational_lifecycle_version bigint;
ALTER TABLE organizations ADD CONSTRAINT organization_operational_lifecycle_source
 FOREIGN KEY(id,operational_lifecycle_version) REFERENCES organization_operational_lifecycle_history(tenant_id,version)
 DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE organizations ADD CONSTRAINT organization_operational_lifecycle_shape
 CHECK((status NOT IN ('SUSPENDED','DEACTIVATED') OR operational_lifecycle_version IS NOT NULL)
  AND (operational_lifecycle_version IS NULL OR operational_lifecycle_version<=version));

DO $$ DECLARE relation text; BEGIN
 FOREACH relation IN ARRAY ARRAY['organization_operational_lifecycle_history','organization_operational_lifecycle_receipts',
  'organization_operational_lifecycle_events'] LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',relation);
  EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',relation);
  EXECUTE format('CREATE POLICY %I ON %I USING (tenant_id=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)
   WITH CHECK (tenant_id=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)',relation||'_tenant',relation);
  EXECUTE format('CREATE TRIGGER %I BEFORE UPDATE OR DELETE ON %I FOR EACH ROW EXECUTE FUNCTION prevent_audit_event_mutation()',relation||'_immutable',relation);
 END LOOP;
END $$;

CREATE FUNCTION guard_organization_operational_lifecycle_parent() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE source jsonb;
BEGIN
 IF TG_OP='INSERT' THEN
  IF NEW.status IN ('SUSPENDED','DEACTIVATED') OR NEW.operational_lifecycle_version IS NOT NULL THEN
   RAISE EXCEPTION 'Organization lifecycle requires a retained prior state' USING ERRCODE='23514';
  END IF;
  RETURN NULL;
 END IF;
 IF NEW.status IS DISTINCT FROM OLD.status AND (NEW.status IN ('SUSPENDED','DEACTIVATED') OR OLD.status IN ('SUSPENDED','DEACTIVATED')) THEN
  SELECT source_json INTO source FROM public.organization_operational_lifecycle_history
   WHERE tenant_id=NEW.id AND version=NEW.version;
  IF NEW.status NOT IN ('ACTIVE','SUSPENDED','DEACTIVATED') OR OLD.status NOT IN ('ACTIVE','SUSPENDED','DEACTIVATED')
   OR NEW.version<>OLD.version+1 OR NEW.operational_lifecycle_version IS DISTINCT FROM NEW.version
   OR NEW.updated_at<OLD.updated_at OR source IS NULL
   OR (source->>'CreatedAt')::timestamptz IS DISTINCT FROM NEW.updated_at
   OR (source->>'Status')::integer IS DISTINCT FROM (CASE NEW.status WHEN 'ACTIVE' THEN 0 WHEN 'SUSPENDED' THEN 4 ELSE 5 END)
   OR (source->>'PreviousStatus')::integer IS DISTINCT FROM (CASE OLD.status WHEN 'ACTIVE' THEN 0 WHEN 'SUSPENDED' THEN 4 ELSE 5 END)
   OR (to_jsonb(NEW)-ARRAY['status','version','updated_at','operational_lifecycle_version'])
      IS DISTINCT FROM (to_jsonb(OLD)-ARRAY['status','version','updated_at','operational_lifecycle_version']) THEN
   RAISE EXCEPTION 'Organization lifecycle transition source is invalid' USING ERRCODE='23514';
  END IF;
 ELSIF NEW.operational_lifecycle_version IS DISTINCT FROM OLD.operational_lifecycle_version THEN
  RAISE EXCEPTION 'Organization lifecycle source pointer is immutable outside a transition' USING ERRCODE='23514';
 END IF;
 RETURN NULL;
END $$;
REVOKE ALL ON FUNCTION guard_organization_operational_lifecycle_parent() FROM PUBLIC;
CREATE CONSTRAINT TRIGGER organization_operational_lifecycle_parent_source AFTER INSERT OR UPDATE ON organizations
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION guard_organization_operational_lifecycle_parent();

CREATE FUNCTION journal_organization_operational_lifecycle_history() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE parent public.organizations%ROWTYPE; member public.organization_members%ROWTYPE; account public.users%ROWTYPE;
 actor uuid; event uuid; member_id uuid; caption text; state text; kind text; source_at timestamptz;
BEGIN
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN
  RAISE EXCEPTION 'Organization lifecycle tenant is invalid' USING ERRCODE='23514';
 END IF;
 IF (SELECT count(*) FROM jsonb_object_keys(NEW.source_json))<>15
  OR NOT NEW.source_json ?& ARRAY['OrganizationId','EventId','ActorId','ActorLabel','ActingCapacity','MembershipId',
   'MembershipVersion','PreviousStatus','Status','Version','CorrelationId','CreatedAt','EntityType','EntityId','EventType']
  OR jsonb_typeof(NEW.source_json->'ActorLabel') IS DISTINCT FROM 'string'
  OR jsonb_typeof(NEW.source_json->'CorrelationId') IS DISTINCT FROM 'string'
  OR jsonb_typeof(NEW.source_json->'Version') IS DISTINCT FROM 'number'
  OR jsonb_typeof(NEW.source_json->'MembershipVersion') IS DISTINCT FROM 'number'
  OR jsonb_typeof(NEW.source_json->'Status') IS DISTINCT FROM 'number'
  OR jsonb_typeof(NEW.source_json->'PreviousStatus') IS DISTINCT FROM 'number'
  OR NEW.source_json->>'Version' !~ '^[1-9][0-9]*$' OR NEW.source_json->>'MembershipVersion' !~ '^[1-9][0-9]*$'
  OR NEW.source_json->>'Status' NOT IN ('0','4','5') OR NEW.source_json->>'PreviousStatus' NOT IN ('0','4','5')
  OR NEW.source_json->>'CreatedAt' !~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]{1,6})?(\+00:00|Z)$' THEN
  RAISE EXCEPTION 'Organization lifecycle source shape is invalid' USING ERRCODE='23514';
 END IF;
 SELECT * INTO parent FROM public.organizations WHERE id=NEW.tenant_id FOR UPDATE;
 actor:=(NEW.source_json->>'ActorId')::uuid; event:=(NEW.source_json->>'EventId')::uuid;
 member_id:=(NEW.source_json->>'MembershipId')::uuid; source_at:=(NEW.source_json->>'CreatedAt')::timestamptz;
 SELECT * INTO member FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=actor FOR SHARE;
 SELECT * INTO account FROM public.users WHERE id=actor FOR SHARE;
 caption:=CASE WHEN length(btrim(account.display_name)) BETWEEN 1 AND 160 AND account.display_name !~ '[[:cntrl:]]'
  THEN account.display_name ELSE 'Member '||actor::text END;
 state:=CASE (NEW.source_json->>'Status')::integer WHEN 0 THEN 'ACTIVE' WHEN 4 THEN 'SUSPENDED' WHEN 5 THEN 'DEACTIVATED' END;
 kind:=CASE state WHEN 'ACTIVE' THEN 'ORGANIZATION_REACTIVATED' WHEN 'SUSPENDED' THEN 'ORGANIZATION_SUSPENDED' WHEN 'DEACTIVATED' THEN 'ORGANIZATION_DEACTIVATED' END;
 IF parent.id IS NULL OR parent.status IS DISTINCT FROM state OR parent.version<>NEW.version
  OR parent.operational_lifecycle_version IS DISTINCT FROM NEW.version OR source_at IS DISTINCT FROM parent.updated_at
  OR source_at>clock_timestamp() OR member.id IS DISTINCT FROM member_id OR member.status IS DISTINCT FROM 'ACTIVE'
  OR member.role NOT IN ('OWNER','ADMIN') OR account.status IS DISTINCT FROM 'ACTIVE'
  OR NEW.source_json->>'ActorLabel' IS DISTINCT FROM caption
  OR NEW.source_json->>'EntityType' IS DISTINCT FROM 'Organization' OR NEW.source_json->>'EntityId' IS DISTINCT FROM NEW.tenant_id::text
  OR NEW.source_json->>'EventType' IS DISTINCT FROM kind
  OR (NEW.source_json->>'MembershipVersion')::bigint IS DISTINCT FROM member.version
  OR NEW.source_json->>'ActingCapacity' IS DISTINCT FROM (CASE member.role WHEN 'OWNER' THEN 'ORGANIZATION_OWNER' ELSE 'ORGANIZATION_ADMIN' END) THEN
  RAISE EXCEPTION 'Organization lifecycle authority source is invalid' USING ERRCODE='23514';
 END IF;
 INSERT INTO public.audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata,created_at)
 VALUES(event,NEW.tenant_id,actor,kind,'Organization',NEW.tenant_id,NEW.source_json->>'CorrelationId',
  jsonb_build_object('version',NEW.version,'status',state),source_at);
 INSERT INTO public.organization_operational_lifecycle_events(tenant_id,version,event_id,event_json)
 VALUES(NEW.tenant_id,NEW.version,event,jsonb_build_object('EventId',event,'OrganizationId',NEW.tenant_id,'ActorId',actor,
  'EventType',kind,'EntityType','Organization','EntityId',NEW.tenant_id,'Version',NEW.version,
  'ActingCapacity',NEW.source_json->>'ActingCapacity','CorrelationId',NEW.source_json->>'CorrelationId',
  'CreatedAt',NEW.source_json->>'CreatedAt'));
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_organization_operational_lifecycle_history() FROM PUBLIC;
CREATE TRIGGER organization_operational_lifecycle_history_source AFTER INSERT ON organization_operational_lifecycle_history
 FOR EACH ROW EXECUTE FUNCTION journal_organization_operational_lifecycle_history();

CREATE FUNCTION guard_organization_operational_lifecycle_receipt() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE source public.organization_operational_lifecycle_history%ROWTYPE; fingerprint text;
BEGIN
 SELECT * INTO source FROM public.organization_operational_lifecycle_history WHERE tenant_id=NEW.tenant_id AND version=NEW.version;
 fingerprint:=upper(encode(sha256(convert_to(format('{"organization":"%s","status":%s,"expectedVersion":%s}',
  NEW.tenant_id,source.source_json->>'Status',NEW.version-1),'UTF8')),'hex'));
 IF (SELECT count(*) FROM jsonb_object_keys(NEW.receipt_json))<>5
  OR NOT NEW.receipt_json ?& ARRAY['ActorId','Key','Fingerprint','Result','ExpiresAt']
  OR jsonb_typeof(NEW.receipt_json->'ActorId') IS DISTINCT FROM 'string'
  OR jsonb_typeof(NEW.receipt_json->'Key') IS DISTINCT FROM 'string'
  OR jsonb_typeof(NEW.receipt_json->'Fingerprint') IS DISTINCT FROM 'string'
  OR jsonb_typeof(NEW.receipt_json->'ExpiresAt') IS DISTINCT FROM 'string'
  OR source.version IS NULL OR NEW.actor_id IS DISTINCT FROM source.actor_id
  OR NEW.receipt_json->'Result' IS DISTINCT FROM source.source_json
  OR NEW.receipt_json->>'ActorId' IS DISTINCT FROM NEW.actor_id::text
  OR NEW.receipt_json->>'Key' IS DISTINCT FROM NEW.key_id::text
  OR NEW.receipt_json->>'Fingerprint' IS DISTINCT FROM fingerprint
  OR (NEW.receipt_json->>'ExpiresAt')::timestamptz IS DISTINCT FROM source.source_at+interval '24 hours' THEN
  RAISE EXCEPTION 'Organization lifecycle receipt source is invalid' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION guard_organization_operational_lifecycle_receipt() FROM PUBLIC;
CREATE TRIGGER organization_operational_lifecycle_receipt_source BEFORE INSERT ON organization_operational_lifecycle_receipts
 FOR EACH ROW EXECUTE FUNCTION guard_organization_operational_lifecycle_receipt();
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT,INSERT ON organization_operational_lifecycle_history,organization_operational_lifecycle_receipts TO strataai_api_runtime;
  GRANT SELECT ON organization_operational_lifecycle_events TO strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('140_organization_operational_lifecycle');
COMMIT;
