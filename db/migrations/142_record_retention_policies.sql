BEGIN;

-- Private policy identity and immutable versions. No runtime write grants yet.
CREATE TABLE record_retention_policies (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 id uuid NOT NULL CHECK(id<>'00000000-0000-0000-0000-000000000000'::uuid),
 version bigint NOT NULL CHECK(version>0),
 record_class text NOT NULL CHECK(record_class IN ('OPERATIONAL_WORK','GOVERNANCE','AUDIT','CORRESPONDENCE','DOCUMENT','AI')),
 category text NOT NULL CHECK(category COLLATE "C" ~ '^[A-Za-z0-9_.-]{1,64}$' AND category !~ '[[:cntrl:]]'),
 category_key text GENERATED ALWAYS AS (upper(category COLLATE "C")) STORED,
 jurisdiction text CHECK(jurisdiction IS NULL OR (length(jurisdiction) BETWEEN 1 AND 120 AND length(btrim(jurisdiction))>0 AND jurisdiction !~ '[[:cntrl:]]')),
 jurisdiction_key text GENERATED ALWAYS AS (COALESCE(organization_configuration_namespace(jurisdiction),'')) STORED,
 CHECK(jurisdiction IS NULL OR jurisdiction_key<>''),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 updated_at timestamptz NOT NULL CHECK(isfinite(updated_at) AND updated_at>=created_at),
 PRIMARY KEY(tenant_id,id),
 UNIQUE(tenant_id,record_class,category_key,jurisdiction_key),
 UNIQUE(tenant_id,id,record_class,category_key,jurisdiction_key)
);
CREATE TABLE record_retention_policy_history (
 tenant_id uuid NOT NULL,
 policy_id uuid NOT NULL,
 version bigint NOT NULL CHECK(version>0),
 record_class text NOT NULL CHECK(record_class IN ('OPERATIONAL_WORK','GOVERNANCE','AUDIT','CORRESPONDENCE','DOCUMENT','AI')),
 category text NOT NULL CHECK(category COLLATE "C" ~ '^[A-Za-z0-9_.-]{1,64}$' AND category !~ '[[:cntrl:]]'),
 category_key text GENERATED ALWAYS AS (upper(category COLLATE "C")) STORED,
 jurisdiction text CHECK(jurisdiction IS NULL OR (length(jurisdiction) BETWEEN 1 AND 120 AND length(btrim(jurisdiction))>0 AND jurisdiction !~ '[[:cntrl:]]')),
 jurisdiction_key text GENERATED ALWAYS AS (COALESCE(organization_configuration_namespace(jurisdiction),'')) STORED,
 CHECK(jurisdiction IS NULL OR jurisdiction_key<>''),
 anchor text NOT NULL CHECK(anchor IN ('CREATED','ARCHIVED','CLOSED','APPROVED')),
 retention_days integer CHECK(retention_days BETWEEN 0 AND 3652500),
 action text NOT NULL CHECK(action IN ('PRESERVE','DELETE','ANONYMIZE')),
 source text NOT NULL CHECK(length(source) BETWEEN 1 AND 1000 AND length(btrim(source))>0 AND translate(source,E'\r\n\t','') !~ '[[:cntrl:]]'),
 notes text CHECK(notes IS NULL OR (length(notes) BETWEEN 1 AND 2000 AND length(btrim(notes))>0 AND translate(notes,E'\r\n\t','') !~ '[[:cntrl:]]')),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 updated_at timestamptz NOT NULL CHECK(isfinite(updated_at) AND updated_at>=created_at),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 membership_id uuid NOT NULL,
 membership_version bigint NOT NULL CHECK(membership_version>0),
 actor_label text NOT NULL CHECK(length(btrim(actor_label)) BETWEEN 1 AND 160 AND actor_label !~ '[[:cntrl:]]'),
 acting_capacity text NOT NULL CHECK(acting_capacity IN ('ORGANIZATION_OWNER','ORGANIZATION_ADMIN')),
 event_id uuid NOT NULL UNIQUE CHECK(event_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 correlation_id text NOT NULL CHECK(length(btrim(correlation_id)) BETWEEN 1 AND 256 AND correlation_id !~ '[[:cntrl:]]'),
 PRIMARY KEY(tenant_id,policy_id,version),
 UNIQUE(tenant_id,policy_id,version,actor_id,key_id),
 FOREIGN KEY(tenant_id,policy_id,record_class,category_key,jurisdiction_key)
  REFERENCES record_retention_policies(tenant_id,id,record_class,category_key,jurisdiction_key) ON DELETE RESTRICT,
 FOREIGN KEY(membership_id,tenant_id,actor_id) REFERENCES organization_members(id,tenant_id,user_id) ON DELETE RESTRICT,
 CHECK(retention_days IS NOT NULL OR action='PRESERVE')
);
CREATE TABLE record_retention_policy_receipts (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 policy_id uuid NOT NULL,
 version bigint NOT NULL,
 fingerprint text NOT NULL CHECK(fingerprint COLLATE "C" ~ '^[A-F0-9]{64}$'),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 expires_at timestamptz NOT NULL CHECK(isfinite(expires_at) AND expires_at=created_at+interval '24 hours'),
 PRIMARY KEY(tenant_id,actor_id,key_id), UNIQUE(tenant_id,policy_id,version),
 FOREIGN KEY(tenant_id,policy_id,version,actor_id,key_id)
  REFERENCES record_retention_policy_history(tenant_id,policy_id,version,actor_id,key_id) ON DELETE RESTRICT
);
ALTER TABLE record_retention_policy_history ADD CONSTRAINT retention_policy_history_receipt
 FOREIGN KEY(tenant_id,actor_id,key_id) REFERENCES record_retention_policy_receipts(tenant_id,actor_id,key_id) DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE record_retention_policy_history ADD CONSTRAINT retention_policy_history_audit
 FOREIGN KEY(event_id) REFERENCES audit_events(id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE record_retention_policies ADD CONSTRAINT retention_policy_current_history
 FOREIGN KEY(tenant_id,id,version) REFERENCES record_retention_policy_history(tenant_id,policy_id,version) DEFERRABLE INITIALLY DEFERRED;

DO $$ DECLARE relation text; BEGIN
 FOREACH relation IN ARRAY ARRAY['record_retention_policies','record_retention_policy_history','record_retention_policy_receipts'] LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',relation);
  EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',relation);
  EXECUTE format('CREATE POLICY %I ON %I USING (tenant_id=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)
   WITH CHECK (tenant_id=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)',relation||'_tenant',relation);
 END LOOP;
END $$;

CREATE FUNCTION guard_retention_policy_current() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Retention policy identity is retained' USING ERRCODE='23514'; END IF;
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN
  RAISE EXCEPTION 'Retention policy tenant context mismatch' USING ERRCODE='42501';
 END IF;
 IF TG_OP='INSERT' THEN
  IF NEW.version<>1 THEN RAISE EXCEPTION 'Retention policy starts at revision one' USING ERRCODE='23514'; END IF;
 ELSE
  IF (NEW.tenant_id,NEW.id,NEW.record_class,upper(NEW.category COLLATE "C"),COALESCE(organization_configuration_namespace(NEW.jurisdiction),''),NEW.created_at)
   IS DISTINCT FROM (OLD.tenant_id,OLD.id,OLD.record_class,OLD.category_key,OLD.jurisdiction_key,OLD.created_at)
   OR OLD.version=9223372036854775807 OR NEW.version<>OLD.version+1 OR NEW.updated_at<OLD.updated_at THEN
   RAISE EXCEPTION 'Retention policy source identity or revision changed invalidly' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER retention_policy_current_guard BEFORE INSERT OR UPDATE OR DELETE ON record_retention_policies
 FOR EACH ROW EXECUTE FUNCTION guard_retention_policy_current();

CREATE FUNCTION journal_retention_policy_history() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE prior record; subject record; parent record; membership record;
BEGIN
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR NEW.actor_id IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid THEN
  RAISE EXCEPTION 'Retention policy actor context mismatch' USING ERRCODE='42501';
 END IF;
 SELECT id,display_name,status INTO subject FROM users WHERE id=NEW.actor_id FOR SHARE;
 SELECT id,status,created_at INTO parent FROM organizations WHERE id=NEW.tenant_id FOR UPDATE;
 SELECT id,version,role,status INTO membership FROM organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id FOR SHARE;
 IF subject.id IS NULL OR subject.status<>'ACTIVE' OR parent.id IS NULL OR parent.status<>'ACTIVE'
  OR membership.id IS NULL OR membership.status<>'ACTIVE' OR membership.role NOT IN ('OWNER','ADMIN')
  OR NEW.membership_id<>membership.id OR NEW.membership_version<>membership.version
  OR NEW.actor_label<>subject.display_name
  OR NEW.acting_capacity<>(CASE WHEN membership.role='OWNER' THEN 'ORGANIZATION_OWNER' ELSE 'ORGANIZATION_ADMIN' END) THEN
  RAISE EXCEPTION 'Retention policy source actor is unavailable' USING ERRCODE='42501';
 END IF;
 IF NEW.created_at<parent.created_at THEN RAISE EXCEPTION 'Retention policy predates Organization' USING ERRCODE='23514'; END IF;
 IF NEW.version>1 THEN
  SELECT created_at,updated_at INTO prior FROM record_retention_policy_history
   WHERE tenant_id=NEW.tenant_id AND policy_id=NEW.policy_id AND version=NEW.version-1;
  IF NOT FOUND OR NEW.created_at<>prior.created_at OR NEW.updated_at<prior.updated_at THEN
   RAISE EXCEPTION 'Retention policy predecessor is unavailable' USING ERRCODE='23514';
  END IF;
 END IF;
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata,created_at)
 VALUES(NEW.event_id,NEW.tenant_id,NEW.actor_id,'RECORD_RETENTION_POLICY_CHANGED','RECORD_RETENTION_POLICY',NEW.policy_id,
  NEW.correlation_id,jsonb_build_object('version',NEW.version,'recordClass',NEW.record_class,'action',NEW.action,'actingCapacity',NEW.acting_capacity),NEW.updated_at);
 RETURN NEW;
END $$;
CREATE TRIGGER retention_policy_history_journal BEFORE INSERT ON record_retention_policy_history
 FOR EACH ROW EXECUTE FUNCTION journal_retention_policy_history();
CREATE TRIGGER retention_policy_history_immutable BEFORE UPDATE OR DELETE ON record_retention_policy_history
 FOR EACH ROW EXECUTE FUNCTION prevent_audit_event_mutation();
CREATE FUNCTION guard_retention_policy_receipt() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE source_at timestamptz;
BEGIN
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR NEW.actor_id IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid THEN
  RAISE EXCEPTION 'Retention policy receipt actor context mismatch' USING ERRCODE='42501';
 END IF;
 SELECT updated_at INTO source_at FROM record_retention_policy_history WHERE tenant_id=NEW.tenant_id
  AND policy_id=NEW.policy_id AND version=NEW.version AND actor_id=NEW.actor_id AND key_id=NEW.key_id;
 IF source_at IS NULL OR NEW.created_at<>source_at THEN
  RAISE EXCEPTION 'Retention policy receipt source clock mismatch' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER retention_policy_receipt_source BEFORE INSERT ON record_retention_policy_receipts
 FOR EACH ROW EXECUTE FUNCTION guard_retention_policy_receipt();
CREATE TRIGGER retention_policy_receipts_immutable BEFORE UPDATE OR DELETE ON record_retention_policy_receipts
 FOR EACH ROW EXECUTE FUNCTION prevent_audit_event_mutation();

CREATE FUNCTION guard_retention_policy_complete_source() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE current_row record; source_row record; highest bigint; source_id uuid;
BEGIN
 IF TG_TABLE_NAME='record_retention_policies' THEN source_id:=NEW.id; ELSE source_id:=NEW.policy_id; END IF;
 SELECT * INTO current_row FROM record_retention_policies WHERE tenant_id=NEW.tenant_id AND id=source_id;
 SELECT * INTO source_row FROM record_retention_policy_history WHERE tenant_id=NEW.tenant_id AND policy_id=source_id AND version=current_row.version;
 SELECT max(version) INTO highest FROM record_retention_policy_history WHERE tenant_id=NEW.tenant_id AND policy_id=source_id;
 IF current_row.id IS NULL OR source_row.policy_id IS NULL OR highest<>current_row.version
  OR (current_row.category,current_row.jurisdiction,current_row.created_at,current_row.updated_at)
   IS DISTINCT FROM (source_row.category,source_row.jurisdiction,source_row.created_at,source_row.updated_at) THEN
  RAISE EXCEPTION 'Retention policy current source is incomplete' USING ERRCODE='23514';
 END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER retention_policy_current_complete AFTER INSERT OR UPDATE ON record_retention_policies
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION guard_retention_policy_complete_source();
CREATE CONSTRAINT TRIGGER retention_policy_history_complete AFTER INSERT ON record_retention_policy_history
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION guard_retention_policy_complete_source();

REVOKE ALL ON FUNCTION guard_retention_policy_current(),journal_retention_policy_history(),guard_retention_policy_complete_source(),guard_retention_policy_receipt() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('142_record_retention_policies');
COMMIT;
