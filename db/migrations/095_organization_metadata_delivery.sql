BEGIN;
LOCK TABLE organization_metadata_events IN SHARE ROW EXCLUSIVE MODE;
ALTER TABLE organization_metadata_events ADD COLUMN ready_at timestamptz,
 ADD CONSTRAINT organization_metadata_ready_time CHECK(ready_at IS NULL OR (isfinite(ready_at) AND ready_at>=created_at));
CREATE OR REPLACE FUNCTION guard_organization_metadata_event_history() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='DELETE' OR TG_OP='UPDATE' AND
  (ROW(NEW.tenant_id,NEW.sequence,NEW.event_id,NEW.event_type,NEW.actor_id,NEW.entity_type,NEW.entity_id,
       NEW.entity_version,NEW.correlation_id,NEW.metadata,NEW.created_at)
   IS DISTINCT FROM ROW(OLD.tenant_id,OLD.sequence,OLD.event_id,OLD.event_type,OLD.actor_id,OLD.entity_type,OLD.entity_id,
       OLD.entity_version,OLD.correlation_id,OLD.metadata,OLD.created_at)
   OR OLD.ready_at IS NOT NULL AND NEW.ready_at IS DISTINCT FROM OLD.ready_at) THEN
  RAISE EXCEPTION 'Organization metadata event history is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE FUNCTION publish_organization_metadata_delivery() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
 VALUES(gen_random_uuid(),NEW.tenant_id,'ORGANIZATION_METADATA_EVENT_READY',
  'organization-metadata-event/'||replace(NEW.event_id::text,'-',''),NEW.actor_id,
  'organization-metadata-delivery',NEW.correlation_id,jsonb_build_object('eventId',NEW.event_id));
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION publish_organization_metadata_delivery() FROM PUBLIC;
CREATE TRIGGER organization_metadata_delivery_publication AFTER INSERT ON organization_metadata_events
 FOR EACH ROW EXECUTE FUNCTION publish_organization_metadata_delivery();
-- Existing rows were already captured from real canonical commands in 094.
-- Add reference jobs, preserving their immutable source identity and version.
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
 SELECT gen_random_uuid(),tenant_id,'ORGANIZATION_METADATA_EVENT_READY',
  'organization-metadata-event/'||replace(event_id::text,'-',''),actor_id,
  'organization-metadata-delivery',correlation_id,jsonb_build_object('eventId',event_id)
 FROM organization_metadata_events;

CREATE FUNCTION deliver_organization_metadata_event(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_event uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE source public.organization_metadata_events%ROWTYPE; claim public.background_jobs%ROWTYPE;
BEGIN
 IF p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN RETURN false; END IF;
 SELECT * INTO claim FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='ORGANIZATION_METADATA_EVENT_READY' AND service_identity='organization-metadata-delivery'
  AND idempotency_key='organization-metadata-event/'||replace(p_event::text,'-','')
  AND safe_metadata=jsonb_build_object('eventId',p_event)
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF claim.id IS NULL THEN RETURN false; END IF;
 SELECT * INTO source FROM public.organization_metadata_events WHERE tenant_id=p_tenant AND event_id=p_event FOR UPDATE;
 IF source.event_id IS NULL OR source.actor_id IS DISTINCT FROM p_actor OR source.correlation_id IS DISTINCT FROM claim.correlation_id
  OR NOT EXISTS(SELECT 1 FROM public.organizations WHERE id=p_tenant AND version>=source.entity_version) THEN RETURN false; END IF;
 -- Historical committed events remain deliverable after newer edits or actor
 -- departure. Every eventual reader must perform fresh current authorization.
 IF source.ready_at IS NULL THEN
  UPDATE public.organization_metadata_events SET ready_at=GREATEST(clock_timestamp(),created_at)
   WHERE tenant_id=p_tenant AND event_id=p_event;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp()) THEN
  RAISE EXCEPTION 'Organization metadata delivery lease expired' USING ERRCODE='23514';
 END IF;
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION deliver_organization_metadata_event(uuid,uuid,uuid,uuid,uuid,uuid) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('095_organization_metadata_delivery');
COMMIT;
