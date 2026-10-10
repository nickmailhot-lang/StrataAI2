BEGIN;
LOCK TABLE organization_configuration_events IN ACCESS EXCLUSIVE MODE;
-- Preserve committed source clocks; upgrades never invent a new event time.
DROP TRIGGER organization_configuration_events_immutable ON organization_configuration_events;
ALTER TABLE organization_configuration_events ADD COLUMN actor_id uuid,
 ADD COLUMN correlation_id text, ADD COLUMN created_at timestamptz, ADD COLUMN ready_at timestamptz;
UPDATE organization_configuration_events SET actor_id=(event_json->>'ActorId')::uuid,
 correlation_id=event_json->>'CorrelationId',created_at=(event_json->>'CreatedAt')::timestamptz;
ALTER TABLE organization_configuration_events ALTER COLUMN actor_id SET NOT NULL,
 ALTER COLUMN correlation_id SET NOT NULL, ALTER COLUMN created_at SET NOT NULL,
 ADD COLUMN updated_at timestamptz GENERATED ALWAYS AS (COALESCE(ready_at,created_at)) STORED NOT NULL,
 ADD CONSTRAINT organization_configuration_delivery_clocks CHECK(isfinite(created_at) AND isfinite(updated_at)
  AND updated_at>=created_at),
 ADD CONSTRAINT organization_configuration_delivery_correlation CHECK(length(btrim(correlation_id)) BETWEEN 1 AND 256);

CREATE FUNCTION guard_organization_configuration_delivery_source() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE source jsonb; safe jsonb;
BEGIN
 IF TG_OP<>'INSERT' THEN
  IF TG_OP='UPDATE' AND OLD.ready_at IS NULL AND NEW.ready_at IS NOT NULL
   AND ROW(NEW.tenant_id,NEW.version,NEW.event_id,NEW.event_json,NEW.actor_id,NEW.correlation_id,NEW.created_at)
    IS NOT DISTINCT FROM ROW(OLD.tenant_id,OLD.version,OLD.event_id,OLD.event_json,OLD.actor_id,OLD.correlation_id,OLD.created_at)
   AND isfinite(NEW.ready_at) AND NEW.ready_at>=OLD.created_at THEN RETURN NEW; END IF;
  RAISE EXCEPTION 'Organization configuration event is immutable' USING ERRCODE='23514';
 END IF;
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid OR NEW.ready_at IS NOT NULL THEN
  RAISE EXCEPTION 'Organization configuration event source is invalid' USING ERRCODE='23514';
 END IF;
 SELECT record_json INTO source FROM public.organization_configuration_history
  WHERE tenant_id=NEW.tenant_id AND version=NEW.version;
 IF source IS NULL THEN RAISE EXCEPTION 'Organization configuration event history is unavailable' USING ERRCODE='23514'; END IF;
 safe:=jsonb_build_object('EventId',(source->>'EventId')::uuid,'OrganizationId',NEW.tenant_id,
  'ActorId',(source->>'ActorId')::uuid,'Version',NEW.version,'CorrelationId',source->>'CorrelationId',
  'CreatedAt',source->>'UpdatedAt','EventType','ORGANIZATION_CONFIGURATION_CHANGED',
  'EntityType','OrganizationConfiguration','EntityId',NEW.tenant_id);
 IF NEW.event_json IS DISTINCT FROM safe OR NEW.event_id IS DISTINCT FROM (source->>'EventId')::uuid THEN
  RAISE EXCEPTION 'Organization configuration event history is invalid' USING ERRCODE='23514';
 END IF;
 NEW.actor_id:=(source->>'ActorId')::uuid; NEW.correlation_id:=source->>'CorrelationId';
 NEW.created_at:=(source->>'UpdatedAt')::timestamptz;
 IF NOT EXISTS(SELECT 1 FROM public.audit_events a WHERE a.id=NEW.event_id AND a.tenant_id=NEW.tenant_id
  AND a.actor_id=NEW.actor_id AND a.event_type='ORGANIZATION_CONFIGURATION_CHANGED'
  AND a.entity_type='OrganizationConfiguration' AND a.entity_id=NEW.tenant_id
  AND a.correlation_id=NEW.correlation_id AND a.created_at=NEW.created_at
  AND a.safe_metadata=jsonb_build_object('version',NEW.version)) THEN
  RAISE EXCEPTION 'Organization configuration audit source is unavailable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION guard_organization_configuration_delivery_source() FROM PUBLIC;
CREATE TRIGGER organization_configuration_delivery_source BEFORE INSERT OR UPDATE OR DELETE ON organization_configuration_events
 FOR EACH ROW EXECUTE FUNCTION guard_organization_configuration_delivery_source();
-- Configuration already accepts 256-character correlation references. Preserve
-- them in this typed queue without widening other producers' existing contract.
ALTER TABLE background_jobs DROP CONSTRAINT background_jobs_correlation_id_check;
ALTER TABLE background_jobs ADD CONSTRAINT background_jobs_correlation_id_check CHECK(
 length(btrim(correlation_id)) BETWEEN 1 AND CASE WHEN job_type='ORGANIZATION_CONFIGURATION_EVENT_READY'
 AND service_identity='organization-configuration-delivery' THEN 256 ELSE 120 END);
CREATE FUNCTION publish_organization_configuration_delivery() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
 VALUES(gen_random_uuid(),NEW.tenant_id,'ORGANIZATION_CONFIGURATION_EVENT_READY',
  'organization-configuration-event/'||replace(NEW.event_id::text,'-',''),NEW.actor_id,
  'organization-configuration-delivery',NEW.correlation_id,jsonb_build_object('eventId',NEW.event_id));
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION publish_organization_configuration_delivery() FROM PUBLIC;
CREATE TRIGGER organization_configuration_delivery_publication AFTER INSERT ON organization_configuration_events
 FOR EACH ROW EXECUTE FUNCTION publish_organization_configuration_delivery();
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
 SELECT gen_random_uuid(),tenant_id,'ORGANIZATION_CONFIGURATION_EVENT_READY',
 'organization-configuration-event/'||replace(event_id::text,'-',''),actor_id,
 'organization-configuration-delivery',correlation_id,jsonb_build_object('eventId',event_id)
 FROM organization_configuration_events;
CREATE FUNCTION deliver_organization_configuration_event(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_event uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE source public.organization_configuration_events%ROWTYPE; claim public.background_jobs%ROWTYPE;
BEGIN
 IF p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN RETURN false; END IF;
 SELECT * INTO claim FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='ORGANIZATION_CONFIGURATION_EVENT_READY' AND service_identity='organization-configuration-delivery'
  AND idempotency_key='organization-configuration-event/'||replace(p_event::text,'-','')
  AND safe_metadata=jsonb_build_object('eventId',p_event) AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease
  AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF claim.id IS NULL THEN RETURN false; END IF;
 SELECT * INTO source FROM public.organization_configuration_events WHERE tenant_id=p_tenant AND event_id=p_event FOR UPDATE;
 IF source.event_id IS NULL OR source.actor_id IS DISTINCT FROM p_actor OR source.correlation_id IS DISTINCT FROM claim.correlation_id
  OR NOT EXISTS(SELECT 1 FROM public.organization_configuration_history h WHERE h.tenant_id=p_tenant AND h.version=source.version
   AND (h.record_json->>'EventId')::uuid=p_event AND (h.record_json->>'ActorId')::uuid=p_actor
   AND h.record_json->>'CorrelationId'=claim.correlation_id AND (h.record_json->>'UpdatedAt')::timestamptz=source.created_at) THEN RETURN false; END IF;
 -- A committed fact survives subsequent revisions and actor departure. Private
 -- replay must independently check each recipient's current Owner/Admin authority.
 IF source.ready_at IS NULL THEN
  UPDATE public.organization_configuration_events SET ready_at=GREATEST(clock_timestamp(),created_at)
   WHERE tenant_id=p_tenant AND event_id=p_event;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND state='RUNNING'
  AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp()) THEN
  RAISE EXCEPTION 'Organization configuration delivery lease expired' USING ERRCODE='23514';
 END IF;
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION deliver_organization_configuration_event(uuid,uuid,uuid,uuid,uuid,uuid) FROM PUBLIC;
CREATE FUNCTION discover_organization_configuration_scopes(p_after uuid,p_limit integer)
RETURNS TABLE(tenant_id uuid) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF p_limit IS NULL OR p_limit<1 OR p_limit>100 THEN
  RAISE EXCEPTION 'Configuration scope page limit is invalid' USING ERRCODE='22023';
 END IF;
 RETURN QUERY SELECT s.tenant_id FROM public.organization_configurations s
 WHERE (p_after IS NULL OR s.tenant_id>p_after)
 AND EXISTS(SELECT 1 FROM public.organization_configuration_events e JOIN public.background_jobs j ON j.tenant_id=e.tenant_id
  WHERE e.tenant_id=s.tenant_id AND j.job_type='ORGANIZATION_CONFIGURATION_EVENT_READY'
   AND j.service_identity='organization-configuration-delivery' AND j.actor_id=e.actor_id
   AND j.correlation_id=e.correlation_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id)
   AND j.idempotency_key='organization-configuration-event/'||replace(e.event_id::text,'-','')
   AND ((j.state='PENDING' AND j.available_at<=clock_timestamp() AND j.attempt_count<j.max_attempts)
    OR (j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp())))
 ORDER BY s.tenant_id LIMIT p_limit;
END $$;
REVOKE ALL ON FUNCTION discover_organization_configuration_scopes(uuid,integer) FROM PUBLIC;

-- This invoker path retains ordinary queue grants and forced tenant RLS.
-- Automatic metadata delivery must never claim or retire unrelated providers.
CREATE FUNCTION claim_organization_configuration_job(p_worker_id uuid) RETURNS SETOF background_jobs
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE v_now timestamptz := clock_timestamp();
BEGIN
 IF p_worker_id IS NULL OR p_worker_id='00000000-0000-0000-0000-000000000000'::uuid THEN
  RAISE EXCEPTION 'Worker identity is required' USING ERRCODE='22023';
 END IF;
 UPDATE public.background_jobs SET state='FAILED',lease_id=NULL,worker_id=NULL,lease_expires_at=NULL,
  last_error_code='lease_expired',updated_at=v_now,version=version+1
 WHERE tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid
  AND job_type='ORGANIZATION_CONFIGURATION_EVENT_READY' AND service_identity='organization-configuration-delivery'
  AND state='RUNNING' AND lease_expires_at<=v_now AND attempt_count>=max_attempts;
 RETURN QUERY WITH candidate AS (
  SELECT id FROM public.background_jobs WHERE tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid
   AND job_type='ORGANIZATION_CONFIGURATION_EVENT_READY' AND service_identity='organization-configuration-delivery'
   AND attempt_count<max_attempts
   AND ((state='PENDING' AND available_at<=v_now) OR (state='RUNNING' AND lease_expires_at<=v_now))
  ORDER BY available_at,created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
 ) UPDATE public.background_jobs j SET state='RUNNING',attempt_count=attempt_count+1,lease_id=gen_random_uuid(),
  worker_id=p_worker_id,lease_expires_at=v_now+interval '2 minutes',updated_at=v_now,version=version+1
 FROM candidate c WHERE j.id=c.id RETURNING j.*;
END $$;
REVOKE ALL ON FUNCTION claim_organization_configuration_job(uuid) FROM PUBLIC;

DO $$ BEGIN IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN
 GRANT EXECUTE ON FUNCTION deliver_organization_configuration_event(uuid,uuid,uuid,uuid,uuid,uuid),
 discover_organization_configuration_scopes(uuid,integer),claim_organization_configuration_job(uuid) TO strataai_worker_runtime;
END IF; END $$;
INSERT INTO schema_migrations(version) VALUES('138_organization_configuration_delivery');
COMMIT;
