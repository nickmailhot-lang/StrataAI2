BEGIN;
CREATE FUNCTION deliver_organization_lifecycle_event(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_event uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE parent public.organizations%ROWTYPE; source public.organization_lifecycle_events%ROWTYPE; claim public.background_jobs%ROWTYPE;
BEGIN
 IF p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN RETURN false; END IF;
 SELECT * INTO parent FROM public.organizations WHERE id=p_tenant FOR SHARE;
 IF parent.id IS NULL OR parent.status<>'DELETED' OR parent.deleted_by IS DISTINCT FROM p_actor THEN RETURN false; END IF;
 SELECT * INTO claim FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='ORGANIZATION_LIFECYCLE_EVENT_READY' AND service_identity='organization-lifecycle-delivery'
  AND idempotency_key='organization-lifecycle-event/'||replace(p_event::text,'-','')
  AND safe_metadata=jsonb_build_object('eventId',p_event)
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF claim.id IS NULL THEN RETURN false; END IF;
 SELECT * INTO source FROM public.organization_lifecycle_events WHERE tenant_id=p_tenant AND event_id=p_event FOR UPDATE;
 IF source.event_id IS NULL OR source.actor_id IS DISTINCT FROM p_actor OR source.entity_version IS DISTINCT FROM parent.version
  OR source.created_at IS DISTINCT FROM parent.deleted_at OR source.correlation_id IS DISTINCT FROM claim.correlation_id
  OR NOT EXISTS(SELECT 1 FROM public.organization_deletion_progress WHERE tenant_id=p_tenant
   AND phase='COMPLETE' AND completed_at=source.created_at) THEN RETURN false; END IF;
 IF source.ready_at IS NULL THEN
  UPDATE public.organization_lifecycle_events SET ready_at=GREATEST(clock_timestamp(),created_at)
   WHERE tenant_id=p_tenant AND event_id=p_event;
 END IF;
 -- A late fence also applies to duplicate delivery: expired claims never succeed.
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp()) THEN
  RAISE EXCEPTION 'Organization lifecycle delivery lease expired' USING ERRCODE='23514';
 END IF;
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION deliver_organization_lifecycle_event(uuid,uuid,uuid,uuid,uuid,uuid) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('090_organization_lifecycle_delivery');
COMMIT;
