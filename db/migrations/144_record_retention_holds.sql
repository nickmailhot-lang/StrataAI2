BEGIN;

CREATE TABLE record_retention_holds (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 id uuid NOT NULL CHECK(id<>'00000000-0000-0000-0000-000000000000'::uuid),
 version bigint NOT NULL CHECK(version IN (1,2)),
 record_class text CHECK(record_class IN ('OPERATIONAL_WORK','GOVERNANCE','AUDIT','CORRESPONDENCE','DOCUMENT','AI')),
 category text CHECK(category COLLATE "C" ~ '^[A-Za-z0-9_.-]{1,64}$'),
 category_key text GENERATED ALWAYS AS (upper(category COLLATE "C")) STORED,
 record_id uuid CHECK(record_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 status text NOT NULL CHECK(status IN ('ACTIVE','RELEASED')),
 reason text NOT NULL CHECK(length(reason) BETWEEN 1 AND 2000 AND length(btrim(reason))>0 AND translate(reason,E'\r\n\t','') !~ '[[:cntrl:]]'),
 applied_by uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT CHECK(applied_by<>'00000000-0000-0000-0000-000000000000'::uuid),
 applied_at timestamptz NOT NULL CHECK(isfinite(applied_at)),
 released_by uuid REFERENCES users(id) ON DELETE RESTRICT CHECK(released_by<>'00000000-0000-0000-0000-000000000000'::uuid),
 released_at timestamptz CHECK(isfinite(released_at)),
 PRIMARY KEY(tenant_id,id),
 CHECK((category IS NULL AND record_id IS NULL) OR record_class IS NOT NULL),
 CHECK((status='ACTIVE' AND version=1 AND released_by IS NULL AND released_at IS NULL)
  OR (status='RELEASED' AND version=2 AND released_by IS NOT NULL AND released_at IS NOT NULL AND released_at>=applied_at)),
 FOREIGN KEY(tenant_id,record_class,record_id) REFERENCES record_retention_sources(tenant_id,record_class,record_id) ON DELETE RESTRICT
);
CREATE INDEX retention_hold_applicability ON record_retention_holds(tenant_id,record_class,category_key,record_id,id);

CREATE TABLE record_retention_hold_history (
 tenant_id uuid NOT NULL, hold_id uuid NOT NULL, version bigint NOT NULL CHECK(version IN (1,2)),
 record_class text, category text, record_id uuid,
 status text NOT NULL CHECK(status IN ('ACTIVE','RELEASED')),
 reason text NOT NULL, applied_by uuid NOT NULL, applied_at timestamptz NOT NULL,
 released_by uuid, released_at timestamptz,
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT CHECK(actor_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 membership_id uuid NOT NULL, membership_version bigint NOT NULL CHECK(membership_version>0),
 actor_label text NOT NULL CHECK(length(actor_label) BETWEEN 1 AND 160 AND actor_label !~ '[[:cntrl:]]'),
 acting_capacity text NOT NULL CHECK(acting_capacity IN ('ORGANIZATION_OWNER','ORGANIZATION_ADMIN')),
 event_id uuid NOT NULL UNIQUE CHECK(event_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 correlation_id text NOT NULL CHECK(length(correlation_id) BETWEEN 1 AND 256 AND length(btrim(correlation_id))>0 AND correlation_id !~ '[[:cntrl:]]'),
 changed_at timestamptz NOT NULL CHECK(isfinite(changed_at)),
 PRIMARY KEY(tenant_id,hold_id,version), UNIQUE(tenant_id,hold_id,version,actor_id,key_id),
 FOREIGN KEY(tenant_id,hold_id) REFERENCES record_retention_holds(tenant_id,id) ON DELETE RESTRICT,
 FOREIGN KEY(membership_id,tenant_id,actor_id) REFERENCES organization_members(id,tenant_id,user_id) ON DELETE RESTRICT,
 FOREIGN KEY(event_id,tenant_id) REFERENCES audit_events(id,tenant_id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED
);
CREATE TABLE record_retention_hold_receipts (
 tenant_id uuid NOT NULL, actor_id uuid NOT NULL,
 key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 hold_id uuid NOT NULL, version bigint NOT NULL,
 fingerprint text NOT NULL CHECK(fingerprint COLLATE "C" ~ '^[A-F0-9]{64}$'),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 expires_at timestamptz NOT NULL CHECK(isfinite(expires_at) AND expires_at=created_at+interval '24 hours'),
 PRIMARY KEY(tenant_id,actor_id,key_id), UNIQUE(tenant_id,hold_id,version),
 FOREIGN KEY(tenant_id,hold_id,version,actor_id,key_id)
  REFERENCES record_retention_hold_history(tenant_id,hold_id,version,actor_id,key_id) ON DELETE RESTRICT
);
ALTER TABLE record_retention_hold_history ADD CONSTRAINT retention_hold_history_receipt
 FOREIGN KEY(tenant_id,actor_id,key_id) REFERENCES record_retention_hold_receipts(tenant_id,actor_id,key_id) DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE record_retention_holds ADD CONSTRAINT retention_hold_current_history
 FOREIGN KEY(tenant_id,id,version) REFERENCES record_retention_hold_history(tenant_id,hold_id,version) DEFERRABLE INITIALLY DEFERRED;

DO $$ DECLARE relation text; BEGIN
 FOREACH relation IN ARRAY ARRAY['record_retention_holds','record_retention_hold_history','record_retention_hold_receipts'] LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',relation);
  EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',relation);
  EXECUTE format('CREATE POLICY %I ON %I USING(tenant_id=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)
   WITH CHECK(tenant_id=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)',relation||'_tenant',relation);
  EXECUTE format('REVOKE ALL ON %I FROM PUBLIC',relation);
 END LOOP;
END $$;

CREATE FUNCTION guard_retention_hold_current() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE parent record; source record;
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Retention hold identity is retained' USING ERRCODE='23514'; END IF;
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN
  RAISE EXCEPTION 'Retention hold tenant context mismatch' USING ERRCODE='42501'; END IF;
 SELECT id,status INTO parent FROM organizations WHERE id=NEW.tenant_id FOR UPDATE;
 IF parent.id IS NULL OR parent.status<>'ACTIVE' THEN RAISE EXCEPTION 'Retention hold parent unavailable' USING ERRCODE='42501'; END IF;
 IF TG_OP='INSERT' THEN
  IF NEW.version<>1 OR NEW.status<>'ACTIVE' THEN RAISE EXCEPTION 'Hold begins Active at version one' USING ERRCODE='23514'; END IF;
 ELSE
  IF OLD.version<>1 OR OLD.status<>'ACTIVE' OR NEW.version<>2 OR NEW.status<>'RELEASED'
   OR (NEW.tenant_id,NEW.id,NEW.record_class,NEW.category,NEW.record_id,NEW.reason,NEW.applied_by,NEW.applied_at)
    IS DISTINCT FROM (OLD.tenant_id,OLD.id,OLD.record_class,OLD.category,OLD.record_id,OLD.reason,OLD.applied_by,OLD.applied_at) THEN
   RAISE EXCEPTION 'Hold release rewrites application or lifecycle' USING ERRCODE='23514'; END IF;
 END IF;
 IF NEW.record_id IS NOT NULL THEN
  SELECT category_key,registered_at INTO source FROM record_retention_sources
   WHERE tenant_id=NEW.tenant_id AND record_class=NEW.record_class AND record_id=NEW.record_id FOR SHARE;
  IF NOT FOUND OR NEW.applied_at<source.registered_at
   OR NEW.category IS NOT NULL AND upper(NEW.category COLLATE "C")<>source.category_key THEN
   RAISE EXCEPTION 'Hold typed source or category unavailable' USING ERRCODE='23514'; END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER retention_hold_current_guard BEFORE INSERT OR UPDATE OR DELETE ON record_retention_holds
 FOR EACH ROW EXECUTE FUNCTION guard_retention_hold_current();

CREATE FUNCTION journal_retention_hold_history() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE current_row record; prior record; parent record; membership record; subject record; scope_kind text;
BEGIN
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR NEW.actor_id IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid THEN
  RAISE EXCEPTION 'Hold history actor context mismatch' USING ERRCODE='42501'; END IF;
 SELECT id,status,updated_at INTO parent FROM organizations WHERE id=NEW.tenant_id FOR UPDATE;
 SELECT id,version,role,status,updated_at INTO membership FROM organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id FOR SHARE;
 SELECT id,status,display_name,updated_at INTO subject FROM users WHERE id=NEW.actor_id FOR SHARE;
 IF parent.id IS NULL OR parent.status<>'ACTIVE' OR subject.id IS NULL OR subject.status<>'ACTIVE'
  OR membership.id IS NULL OR membership.status<>'ACTIVE' OR membership.role NOT IN ('OWNER','ADMIN')
  OR NEW.membership_id<>membership.id OR NEW.membership_version<>membership.version OR NEW.actor_label<>subject.display_name
  OR NEW.acting_capacity<>(CASE WHEN membership.role='OWNER' THEN 'ORGANIZATION_OWNER' ELSE 'ORGANIZATION_ADMIN' END) THEN
  RAISE EXCEPTION 'Hold history actor unavailable' USING ERRCODE='42501'; END IF;
 SELECT * INTO current_row FROM record_retention_holds WHERE tenant_id=NEW.tenant_id AND id=NEW.hold_id FOR UPDATE;
 IF current_row.id IS NULL OR NEW.version<>current_row.version
  OR (NEW.record_class,NEW.category,NEW.record_id,NEW.status,NEW.reason,NEW.applied_by,NEW.applied_at,NEW.released_by,NEW.released_at)
   IS DISTINCT FROM (current_row.record_class,current_row.category,current_row.record_id,current_row.status,current_row.reason,current_row.applied_by,current_row.applied_at,current_row.released_by,current_row.released_at)
  OR NEW.changed_at IS DISTINCT FROM COALESCE(NEW.released_at,NEW.applied_at)
  OR NEW.actor_id IS DISTINCT FROM COALESCE(NEW.released_by,NEW.applied_by)
  OR NEW.changed_at<parent.updated_at OR NEW.changed_at<membership.updated_at OR NEW.changed_at<subject.updated_at
  OR NEW.changed_at>clock_timestamp() THEN
  RAISE EXCEPTION 'Hold history source mismatch' USING ERRCODE='23514'; END IF;
 IF NEW.version=2 THEN
  SELECT * INTO prior FROM record_retention_hold_history WHERE tenant_id=NEW.tenant_id AND hold_id=NEW.hold_id AND version=1;
  IF prior.hold_id IS NULL OR prior.status<>'ACTIVE'
   OR (NEW.record_class,NEW.category,NEW.record_id,NEW.reason,NEW.applied_by,NEW.applied_at)
    IS DISTINCT FROM (prior.record_class,prior.category,prior.record_id,prior.reason,prior.applied_by,prior.applied_at) THEN
   RAISE EXCEPTION 'Hold application predecessor unavailable' USING ERRCODE='23514'; END IF;
 END IF;
 scope_kind:=CASE WHEN NEW.record_id IS NOT NULL THEN 'RECORD' WHEN NEW.category IS NOT NULL THEN 'CATEGORY' WHEN NEW.record_class IS NOT NULL THEN 'CLASS' ELSE 'ORGANIZATION' END;
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata,created_at)
 VALUES(NEW.event_id,NEW.tenant_id,NEW.actor_id,CASE WHEN NEW.status='ACTIVE' THEN 'RETENTION_HOLD_APPLIED' ELSE 'RETENTION_HOLD_RELEASED' END,
  'RETENTION_HOLD',NEW.hold_id,NEW.correlation_id,jsonb_build_object('version',NEW.version,'status',NEW.status,'scopeKind',scope_kind,'actingCapacity',NEW.acting_capacity),NEW.changed_at);
 RETURN NEW;
END $$;
CREATE TRIGGER retention_hold_history_journal BEFORE INSERT ON record_retention_hold_history
 FOR EACH ROW EXECUTE FUNCTION journal_retention_hold_history();
CREATE TRIGGER retention_hold_history_immutable BEFORE UPDATE OR DELETE ON record_retention_hold_history
 FOR EACH ROW EXECUTE FUNCTION guard_retention_source_immutable();

CREATE FUNCTION guard_retention_hold_receipt() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE source_clock timestamptz;
BEGIN
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR NEW.actor_id IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid THEN
  RAISE EXCEPTION 'Hold receipt actor context mismatch' USING ERRCODE='42501'; END IF;
 SELECT changed_at INTO source_clock FROM record_retention_hold_history WHERE tenant_id=NEW.tenant_id AND hold_id=NEW.hold_id
  AND version=NEW.version AND actor_id=NEW.actor_id AND key_id=NEW.key_id;
 IF source_clock IS NULL OR NEW.created_at<>source_clock THEN RAISE EXCEPTION 'Hold receipt source clock mismatch' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER retention_hold_receipt_guard BEFORE INSERT ON record_retention_hold_receipts FOR EACH ROW EXECUTE FUNCTION guard_retention_hold_receipt();
CREATE TRIGGER retention_hold_receipt_immutable BEFORE UPDATE OR DELETE ON record_retention_hold_receipts FOR EACH ROW EXECUTE FUNCTION guard_retention_source_immutable();

CREATE FUNCTION guard_retention_hold_complete() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE identity uuid; current_row record; source record; highest bigint;
BEGIN
 IF TG_TABLE_NAME='record_retention_holds' THEN identity:=NEW.id; ELSE identity:=NEW.hold_id; END IF;
 SELECT * INTO current_row FROM record_retention_holds WHERE tenant_id=NEW.tenant_id AND id=identity;
 SELECT * INTO source FROM record_retention_hold_history WHERE tenant_id=NEW.tenant_id AND hold_id=identity AND version=current_row.version;
 SELECT max(version) INTO highest FROM record_retention_hold_history WHERE tenant_id=NEW.tenant_id AND hold_id=identity;
 IF current_row.id IS NULL OR source.hold_id IS NULL OR highest<>current_row.version
  OR (current_row.record_class,current_row.category,current_row.record_id,current_row.status,current_row.reason,current_row.applied_by,current_row.applied_at,current_row.released_by,current_row.released_at)
   IS DISTINCT FROM (source.record_class,source.category,source.record_id,source.status,source.reason,source.applied_by,source.applied_at,source.released_by,source.released_at) THEN
  RAISE EXCEPTION 'Hold current graph incomplete' USING ERRCODE='23514'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER retention_hold_current_complete AFTER INSERT OR UPDATE ON record_retention_holds
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION guard_retention_hold_complete();
CREATE CONSTRAINT TRIGGER retention_hold_history_complete AFTER INSERT ON record_retention_hold_history
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION guard_retention_hold_complete();
REVOKE ALL ON FUNCTION guard_retention_hold_current(),journal_retention_hold_history(),guard_retention_hold_receipt(),guard_retention_hold_complete() FROM PUBLIC;
-- Activation awaits owning scope authorization and both adapters; no runtime grants.
INSERT INTO schema_migrations(version) VALUES('144_record_retention_holds');
COMMIT;
