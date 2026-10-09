BEGIN;
LOCK TABLE organization_deletion_requests IN ACCESS EXCLUSIVE MODE;
LOCK TABLE organization_deletion_progress IN ACCESS EXCLUSIVE MODE;
-- The immutable accepted request, rather than migration time, owns creation.
-- Refuse contradictory legacy history without rewriting its update clock.
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM organization_deletion_progress p
  LEFT JOIN organization_deletion_requests r USING(tenant_id,request_id)
  WHERE r.request_id IS NULL OR NOT isfinite(r.created_at)
   OR NOT isfinite(p.updated_at) OR p.updated_at<r.created_at) THEN
  RAISE EXCEPTION 'Organization deletion progress clock history is unavailable' USING ERRCODE='23514';
 END IF;
END $$;
ALTER TABLE organization_deletion_progress ADD COLUMN created_at timestamptz;
UPDATE organization_deletion_progress p SET created_at=r.created_at
 FROM organization_deletion_requests r WHERE r.tenant_id=p.tenant_id AND r.request_id=p.request_id;
ALTER TABLE organization_deletion_progress ALTER COLUMN created_at SET NOT NULL,
 ADD CONSTRAINT organization_deletion_progress_clocks CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at);
CREATE FUNCTION maintain_organization_deletion_progress_clocks() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE source_created timestamptz;
BEGIN
 SELECT r.created_at INTO source_created FROM public.organization_deletion_requests r
  WHERE r.tenant_id=NEW.tenant_id AND r.request_id=NEW.request_id;
 IF source_created IS NULL THEN
  IF row_security_active(TG_RELID)
   AND NULLIF(current_setting('app.tenant_id',true),'') IS DISTINCT FROM NEW.tenant_id::text THEN
   RAISE EXCEPTION 'Organization deletion progress requires its owning tenant context' USING ERRCODE='42501';
  END IF;
  RAISE EXCEPTION 'Organization deletion progress clock source is unavailable' USING ERRCODE='23514';
 END IF;
 IF TG_OP='UPDATE' THEN
  IF NEW.created_at IS DISTINCT FROM OLD.created_at OR NEW.created_at IS DISTINCT FROM source_created
   OR NEW.updated_at<OLD.updated_at THEN
   RAISE EXCEPTION 'Organization deletion progress clocks cannot replace history' USING ERRCODE='23514';
  END IF;
 ELSIF NEW.created_at IS NOT NULL AND NEW.created_at IS DISTINCT FROM source_created THEN
  RAISE EXCEPTION 'Organization deletion progress creation belongs to its accepted request' USING ERRCODE='23514';
 END IF;
 NEW.created_at:=source_created;
 RETURN NEW;
END $$;
CREATE TRIGGER organization_deletion_progress_clock BEFORE INSERT OR UPDATE ON organization_deletion_progress
 FOR EACH ROW EXECUTE FUNCTION maintain_organization_deletion_progress_clocks();
REVOKE ALL ON FUNCTION maintain_organization_deletion_progress_clocks() FROM PUBLIC;
-- Finalization shares the retained checkpoint clock even after wall-clock correction.
CREATE OR REPLACE FUNCTION finish_organization_deletion(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_request uuid,p_step uuid,p_version bigint) RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE parent public.organizations%ROWTYPE; progress public.organization_deletion_progress%ROWTYPE;
 claim public.background_jobs%ROWTYPE; root public.organization_deletion_requests%ROWTYPE;
 terminal_time timestamptz; terminal_event uuid;
BEGIN
 IF p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN RETURN false; END IF;
 -- Parent before job/child locks, consistent with Organization command admission.
 SELECT * INTO parent FROM public.organizations WHERE id=p_tenant FOR UPDATE;
 SELECT * INTO root FROM public.organization_deletion_requests WHERE tenant_id=p_tenant;
 IF parent.id IS NULL OR root.request_id IS DISTINCT FROM p_request OR root.actor_id IS DISTINCT FROM p_actor
  OR root.accepted_version IS DISTINCT FROM p_version THEN RETURN false; END IF;
 SELECT * INTO claim FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='ORGANIZATION_DELETE_PAGE' AND service_identity='organization-lifecycle'
  AND correlation_id=root.correlation_id
  AND safe_metadata=jsonb_build_object('requestId',p_request,'stepId',p_step,'acceptedVersion',p_version)
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF claim.id IS NULL THEN RETURN false; END IF;
 SELECT * INTO progress FROM public.organization_deletion_progress WHERE tenant_id=p_tenant AND request_id=p_request FOR UPDATE;
 IF progress.step_id IS DISTINCT FROM p_step THEN RETURN false; END IF;
 IF progress.phase='COMPLETE' THEN
  RETURN parent.status='DELETED' AND parent.version=p_version+1 AND parent.deleted_by=p_actor
   AND progress.completed_at=parent.deleted_at
   AND EXISTS(SELECT 1 FROM public.organization_lifecycle_events e JOIN public.background_jobs j ON j.tenant_id=e.tenant_id
    WHERE e.tenant_id=p_tenant AND e.actor_id=p_actor AND e.entity_version=parent.version AND e.created_at=parent.deleted_at
     AND e.correlation_id=root.correlation_id AND j.job_type='ORGANIZATION_LIFECYCLE_EVENT_READY'
     AND j.idempotency_key='organization-lifecycle-event/'||replace(e.event_id::text,'-','')
     AND j.actor_id=e.actor_id AND j.service_identity='organization-lifecycle-delivery'
     AND j.correlation_id=e.correlation_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id));
 END IF;
 IF parent.status<>'DELETING' OR parent.version<>p_version OR progress.phase<>'FINALIZE'
  OR EXISTS(SELECT 1 FROM public.boards WHERE tenant_id=p_tenant AND (lifecycle_state<>'DELETED' OR background_image_id IS NOT NULL OR background_type='IMAGE'))
  OR EXISTS(SELECT 1 FROM public.board_lists WHERE tenant_id=p_tenant AND lifecycle_state<>'DELETED')
  OR EXISTS(SELECT 1 FROM public.cards WHERE tenant_id=p_tenant AND (lifecycle_state<>'DELETED' OR cover_attachment_id IS NOT NULL))
  OR EXISTS(SELECT 1 FROM public.attachments WHERE tenant_id=p_tenant AND lifecycle_state<>'DELETED') THEN RETURN false; END IF;
 PERFORM set_config('app.organization_deletion_job',p_job::text,true);
 PERFORM set_config('app.organization_deletion_lease',p_lease::text,true);
 PERFORM set_config('app.organization_deletion_worker',p_worker::text,true);
 terminal_time=GREATEST(clock_timestamp(),parent.updated_at,progress.updated_at); terminal_event=gen_random_uuid();
 UPDATE public.organizations SET status='DELETED',deleted_by=p_actor,deleted_at=terminal_time,
  updated_at=terminal_time,version=version+1 WHERE id=p_tenant;
 UPDATE public.organization_deletion_progress SET phase='COMPLETE',completed_at=terminal_time,
  updated_at=terminal_time,version=version+1 WHERE tenant_id=p_tenant;
 INSERT INTO public.audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
  VALUES(gen_random_uuid(),p_tenant,p_actor,'ORGANIZATION_DELETED','Organization',p_tenant,root.correlation_id,terminal_time);
 INSERT INTO public.organization_lifecycle_events(tenant_id,event_id,event_type,actor_id,entity_type,entity_id,entity_version,correlation_id,created_at)
  VALUES(p_tenant,terminal_event,'ORGANIZATION_DELETED',p_actor,'Organization',p_tenant,parent.version+1,root.correlation_id,terminal_time);
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
  VALUES(gen_random_uuid(),p_tenant,'ORGANIZATION_LIFECYCLE_EVENT_READY','organization-lifecycle-event/'||replace(terminal_event::text,'-',''),
   p_actor,'organization-lifecycle-delivery',root.correlation_id,jsonb_build_object('eventId',terminal_event));
 -- Expiry during any late write refuses and rolls back ALL terminal effects.
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp()) THEN
  RAISE EXCEPTION 'Organization terminal lease expired' USING ERRCODE='23514';
 END IF;
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION finish_organization_deletion(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('124_organization_deletion_progress_clocks');
COMMIT;
