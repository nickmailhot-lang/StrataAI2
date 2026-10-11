BEGIN;

-- Producer identity, not a permissive polymorphic UUID. Additional producer
-- kinds require real tenant-safe foreign keys before they can be registered.
ALTER TABLE audit_events ADD CONSTRAINT audit_event_retention_identity UNIQUE(id,tenant_id);
CREATE TABLE record_retention_sources (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 record_class text NOT NULL CHECK(record_class IN ('OPERATIONAL_WORK','GOVERNANCE','AUDIT','CORRESPONDENCE','DOCUMENT','AI')),
 record_id uuid NOT NULL CHECK(record_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 source_kind text NOT NULL CHECK(source_kind IN ('CARD','CARD_COMMENT','ATTACHMENT','AUDIT_EVENT')),
 version bigint NOT NULL DEFAULT 1 CHECK(version=1),
 category text NOT NULL CHECK(category COLLATE "C" ~ '^[A-Za-z0-9_.-]{1,64}$'),
 category_key text GENERATED ALWAYS AS (upper(category COLLATE "C")) STORED,
 jurisdiction text CHECK(jurisdiction IS NULL OR (length(jurisdiction) BETWEEN 1 AND 120
  AND jurisdiction !~ '[[:cntrl:]]' AND organization_configuration_namespace(jurisdiction)<>'')),
 parent_card_id uuid,
 card_id uuid GENERATED ALWAYS AS (CASE WHEN source_kind='CARD' THEN record_id END) STORED,
 comment_id uuid GENERATED ALWAYS AS (CASE WHEN source_kind='CARD_COMMENT' THEN record_id END) STORED,
 attachment_id uuid GENERATED ALWAYS AS (CASE WHEN source_kind='ATTACHMENT' THEN record_id END) STORED,
 audit_id uuid GENERATED ALWAYS AS (CASE WHEN source_kind='AUDIT_EVENT' THEN record_id END) STORED,
 registered_at timestamptz NOT NULL CHECK(isfinite(registered_at)),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 membership_id uuid NOT NULL,
 membership_version bigint NOT NULL CHECK(membership_version>0),
 actor_label text NOT NULL CHECK(length(actor_label) BETWEEN 1 AND 160 AND actor_label !~ '[[:cntrl:]]'),
 acting_capacity text NOT NULL CHECK(acting_capacity IN ('ORGANIZATION_OWNER','ORGANIZATION_ADMIN')),
 event_id uuid NOT NULL UNIQUE CHECK(event_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 correlation_id text NOT NULL CHECK(length(btrim(correlation_id)) BETWEEN 1 AND 256 AND correlation_id !~ '[[:cntrl:]]'),
 PRIMARY KEY(tenant_id,record_class,record_id),
 UNIQUE(tenant_id,record_class,record_id,category_key),
 CHECK((source_kind IN ('CARD','CARD_COMMENT') AND record_class='OPERATIONAL_WORK')
  OR (source_kind='ATTACHMENT' AND record_class='DOCUMENT')
  OR (source_kind='AUDIT_EVENT' AND record_class='AUDIT')),
 CHECK((source_kind IN ('CARD_COMMENT','ATTACHMENT'))=(parent_card_id IS NOT NULL)),
 FOREIGN KEY(card_id,tenant_id) REFERENCES cards(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(comment_id,tenant_id,parent_card_id) REFERENCES card_comments(id,tenant_id,card_id) ON DELETE RESTRICT,
 FOREIGN KEY(attachment_id,tenant_id,parent_card_id) REFERENCES attachments(id,tenant_id,card_id) ON DELETE RESTRICT,
 FOREIGN KEY(audit_id,tenant_id) REFERENCES audit_events(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(membership_id,tenant_id,actor_id) REFERENCES organization_members(id,tenant_id,user_id) ON DELETE RESTRICT,
 FOREIGN KEY(event_id) REFERENCES audit_events(id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED
);
CREATE INDEX record_retention_sources_category ON record_retention_sources(tenant_id,record_class,category_key,record_id);
ALTER TABLE record_retention_sources ENABLE ROW LEVEL SECURITY;
ALTER TABLE record_retention_sources FORCE ROW LEVEL SECURITY;
CREATE POLICY record_retention_sources_tenant ON record_retention_sources
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

CREATE FUNCTION journal_retention_source() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE parent record; membership record; subject record; source_clock timestamptz;
BEGIN
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR NEW.actor_id IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid THEN
  RAISE EXCEPTION 'Retention source context mismatch' USING ERRCODE='42501';
 END IF;
 SELECT id,status,created_at,updated_at INTO parent FROM organizations WHERE id=NEW.tenant_id FOR UPDATE;
 SELECT id,version,role,status,updated_at INTO membership FROM organization_members
  WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id FOR SHARE;
 SELECT id,status,display_name,updated_at INTO subject FROM users WHERE id=NEW.actor_id FOR SHARE;
 IF parent.id IS NULL OR parent.status<>'ACTIVE' OR membership.id IS NULL OR membership.status<>'ACTIVE'
  OR membership.role NOT IN ('OWNER','ADMIN') OR subject.id IS NULL OR subject.status<>'ACTIVE'
  OR NEW.membership_id<>membership.id OR NEW.membership_version<>membership.version
  OR NEW.actor_label<>subject.display_name
  OR NEW.acting_capacity<>(CASE WHEN membership.role='OWNER' THEN 'ORGANIZATION_OWNER' ELSE 'ORGANIZATION_ADMIN' END) THEN
  RAISE EXCEPTION 'Retention source actor unavailable' USING ERRCODE='42501';
 END IF;
 CASE NEW.source_kind
  WHEN 'CARD' THEN SELECT created_at INTO source_clock FROM cards WHERE id=NEW.record_id AND tenant_id=NEW.tenant_id FOR SHARE;
  WHEN 'CARD_COMMENT' THEN SELECT created_at INTO source_clock FROM card_comments WHERE id=NEW.record_id AND tenant_id=NEW.tenant_id AND card_id=NEW.parent_card_id FOR SHARE;
  WHEN 'ATTACHMENT' THEN SELECT created_at INTO source_clock FROM attachments WHERE id=NEW.record_id AND tenant_id=NEW.tenant_id AND card_id=NEW.parent_card_id FOR SHARE;
  WHEN 'AUDIT_EVENT' THEN SELECT created_at INTO source_clock FROM audit_events WHERE id=NEW.record_id AND tenant_id=NEW.tenant_id FOR SHARE;
  ELSE RAISE EXCEPTION 'Unknown retention producer' USING ERRCODE='23514';
 END CASE;
 IF source_clock IS NULL OR NOT isfinite(source_clock) OR NEW.registered_at<source_clock
  OR NEW.registered_at<parent.created_at OR NEW.registered_at<parent.updated_at
  OR NEW.registered_at<membership.updated_at OR NEW.registered_at<subject.updated_at
  OR NEW.registered_at>clock_timestamp() THEN
  RAISE EXCEPTION 'Retention producer source unavailable' USING ERRCODE='23514';
 END IF;
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata,created_at)
 VALUES(NEW.event_id,NEW.tenant_id,NEW.actor_id,'RECORD_RETENTION_SOURCE_REGISTERED','RECORD_RETENTION_SOURCE',NEW.record_id,
  NEW.correlation_id,jsonb_build_object('recordClass',NEW.record_class,'sourceKind',NEW.source_kind,'actingCapacity',NEW.acting_capacity),NEW.registered_at);
 RETURN NEW;
END $$;
CREATE TRIGGER record_retention_source_journal BEFORE INSERT ON record_retention_sources
 FOR EACH ROW EXECUTE FUNCTION journal_retention_source();
CREATE FUNCTION guard_retention_source_immutable() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$ BEGIN
 RAISE EXCEPTION 'Retention source identity and classification are retained' USING ERRCODE='23514';
END $$;
CREATE TRIGGER record_retention_source_immutable BEFORE UPDATE OR DELETE ON record_retention_sources
 FOR EACH ROW EXECUTE FUNCTION guard_retention_source_immutable();
REVOKE ALL ON record_retention_sources FROM PUBLIC;
REVOKE ALL ON FUNCTION journal_retention_source(),guard_retention_source_immutable() FROM PUBLIC;
-- No runtime capabilities or automatic classifications: owning record
-- authorization, Demo parity and commands must be implemented before activation.
INSERT INTO schema_migrations(version) VALUES('143_record_retention_sources');
COMMIT;
