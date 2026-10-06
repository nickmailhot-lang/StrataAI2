BEGIN;
-- Future canonical metadata commands only. Historical audits lack the entity
-- version, so no invented backfill is exposed as an authoritative event.
LOCK TABLE audit_events IN SHARE ROW EXCLUSIVE MODE;
CREATE TABLE organization_metadata_event_streams (
 tenant_id uuid PRIMARY KEY REFERENCES organizations(id) ON DELETE RESTRICT,
 last_sequence bigint NOT NULL CHECK(last_sequence>0)
);
CREATE TABLE organization_metadata_events (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 sequence bigint NOT NULL CHECK(sequence>0),
 event_id uuid NOT NULL REFERENCES audit_events(id) ON DELETE RESTRICT,
 event_type text NOT NULL CHECK(event_type IN ('ORGANIZATION_CREATED','ORGANIZATION_UPDATED')),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 entity_type text NOT NULL CHECK(entity_type='Organization'),
 entity_id uuid NOT NULL CHECK(entity_id=tenant_id),
 entity_version bigint NOT NULL CHECK(entity_version>0),
 correlation_id text NOT NULL CHECK(correlation_id ~ '^[A-Za-z0-9._-]{1,64}$'),
 metadata jsonb NOT NULL CHECK(metadata='{}'::jsonb),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 PRIMARY KEY(tenant_id,sequence), UNIQUE(event_id), UNIQUE(tenant_id,entity_version),
 FOREIGN KEY(tenant_id) REFERENCES organization_metadata_event_streams(tenant_id) ON DELETE RESTRICT,
 CHECK((event_type='ORGANIZATION_CREATED' AND entity_version=1)
  OR (event_type='ORGANIZATION_UPDATED' AND entity_version>1))
);
ALTER TABLE organization_metadata_event_streams ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_metadata_event_streams FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_metadata_stream_tenant ON organization_metadata_event_streams
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
ALTER TABLE organization_metadata_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_metadata_events FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_metadata_event_tenant ON organization_metadata_events
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

-- A private trigger capability reads the actual parent version and timestamp
-- while the owning command holds its parent lock. Runtime roles cannot write
-- either the journal or its counter directly. No names, email or logo content.
CREATE FUNCTION journal_organization_metadata_event() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE parent public.organizations%ROWTYPE; next_sequence bigint;
BEGIN
 IF NEW.event_type NOT IN ('ORGANIZATION_CREATED','ORGANIZATION_UPDATED') THEN RETURN NEW; END IF;
 IF NEW.tenant_id IS NULL OR NEW.entity_type<>'Organization' OR NEW.entity_id IS DISTINCT FROM NEW.tenant_id
  OR NEW.actor_id IS NULL OR NEW.safe_metadata<>'{}'::jsonb THEN
  RAISE EXCEPTION 'Organization metadata source is invalid' USING ERRCODE='23514';
 END IF;
 SELECT * INTO parent FROM public.organizations WHERE id=NEW.tenant_id FOR SHARE;
 IF parent.id IS NULL OR parent.status<>'ACTIVE'
  OR NEW.event_type='ORGANIZATION_CREATED' AND (parent.version<>1 OR parent.owner_user_id IS DISTINCT FROM NEW.actor_id)
  OR NEW.event_type='ORGANIZATION_UPDATED' AND parent.version<=1
  OR NOT EXISTS(SELECT 1 FROM public.organization_members m JOIN public.users u ON u.id=m.user_id
   WHERE m.tenant_id=NEW.tenant_id AND m.user_id=NEW.actor_id AND m.status='ACTIVE'
   AND m.role IN ('OWNER','ADMIN') AND u.status='ACTIVE') THEN
  RAISE EXCEPTION 'Organization metadata authority is unavailable' USING ERRCODE='23514';
 END IF;
 INSERT INTO public.organization_metadata_event_streams(tenant_id,last_sequence) VALUES(NEW.tenant_id,1)
 ON CONFLICT(tenant_id) DO UPDATE SET last_sequence=organization_metadata_event_streams.last_sequence+1
 RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.organization_metadata_events(tenant_id,sequence,event_id,event_type,actor_id,entity_type,entity_id,
  entity_version,correlation_id,metadata,created_at)
 VALUES(NEW.tenant_id,next_sequence,NEW.id,NEW.event_type,NEW.actor_id,'Organization',NEW.tenant_id,
  parent.version,NEW.correlation_id,'{}'::jsonb,parent.updated_at);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_organization_metadata_event() FROM PUBLIC;
CREATE TRIGGER organization_metadata_journal AFTER INSERT ON audit_events
 FOR EACH ROW EXECUTE FUNCTION journal_organization_metadata_event();
CREATE FUNCTION guard_organization_metadata_event_history() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 RAISE EXCEPTION 'Organization metadata event history is immutable' USING ERRCODE='23514';
END $$;
REVOKE ALL ON FUNCTION guard_organization_metadata_event_history() FROM PUBLIC;
CREATE TRIGGER organization_metadata_event_history BEFORE UPDATE OR DELETE ON organization_metadata_events
 FOR EACH ROW EXECUTE FUNCTION guard_organization_metadata_event_history();
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT ON organization_metadata_events,organization_metadata_event_streams TO strataai_api_runtime;
  REVOKE INSERT,UPDATE,DELETE ON organization_metadata_events,organization_metadata_event_streams FROM strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('094_organization_metadata_events');
COMMIT;
