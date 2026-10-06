BEGIN;
ALTER TABLE organizations DROP CONSTRAINT organizations_status_check;
ALTER TABLE organizations ADD CONSTRAINT organizations_status_check CHECK(status IN ('ACTIVE','ARCHIVED','DELETING','DELETED'));
ALTER TABLE organizations ADD COLUMN deleted_at timestamptz,
 ADD COLUMN deleted_by uuid REFERENCES users(id) ON DELETE RESTRICT,
 ADD CONSTRAINT organization_terminal_shape CHECK(
  (status='DELETED' AND deleted_at IS NOT NULL AND deleted_by IS NOT NULL AND isfinite(deleted_at)
   AND deleted_at>=created_at AND deleted_at=updated_at)
  OR (status<>'DELETED' AND deleted_at IS NULL AND deleted_by IS NULL));
CREATE TABLE organization_lifecycle_events (
 tenant_id uuid PRIMARY KEY REFERENCES organizations(id) ON DELETE RESTRICT,
 event_id uuid NOT NULL UNIQUE,
 event_type text NOT NULL CHECK(event_type='ORGANIZATION_DELETED'),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 entity_type text NOT NULL CHECK(entity_type='Organization'),
 entity_id uuid NOT NULL CHECK(entity_id=tenant_id),
 entity_version bigint NOT NULL CHECK(entity_version>=3),
 correlation_id text NOT NULL CHECK(correlation_id ~ '^[A-Za-z0-9._-]{1,64}$'),
 metadata jsonb NOT NULL DEFAULT '{}'::jsonb CHECK(metadata='{}'::jsonb),
 created_at timestamptz NOT NULL, ready_at timestamptz,
 CHECK(ready_at IS NULL OR ready_at>=created_at)
);
ALTER TABLE organization_lifecycle_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_lifecycle_events FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_lifecycle_events_tenant ON organization_lifecycle_events
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
-- Final graph proof must remain cheap after a large tenant has been processed.
CREATE INDEX organization_remaining_boards ON boards(tenant_id,id) WHERE lifecycle_state<>'DELETED';
CREATE INDEX organization_remaining_lists ON board_lists(tenant_id,id) WHERE lifecycle_state<>'DELETED';
CREATE INDEX organization_remaining_cards ON cards(tenant_id,id) WHERE lifecycle_state<>'DELETED';
CREATE INDEX organization_remaining_attachments ON attachments(tenant_id,id) WHERE lifecycle_state<>'DELETED';
CREATE FUNCTION guard_organization_terminal() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='UPDATE' AND OLD.status='DELETED' AND NEW IS DISTINCT FROM OLD THEN
  RAISE EXCEPTION 'Deleted Organization is immutable' USING ERRCODE='23514';
 END IF;
 IF TG_OP='DELETE' THEN
  IF OLD.status='DELETED' THEN RAISE EXCEPTION 'Deleted Organization is retained' USING ERRCODE='23514'; END IF;
  RETURN OLD;
 END IF;
 IF NEW.status='DELETED' AND (TG_OP='INSERT' OR OLD.status IS DISTINCT FROM 'DELETED') THEN
  IF TG_OP='INSERT' OR OLD.status<>'DELETING' OR session_user<>'strataai_worker_runtime'
   OR NEW.version<>OLD.version+1 OR NEW.name IS DISTINCT FROM OLD.name
   OR NEW.owner_user_id IS DISTINCT FROM OLD.owner_user_id OR NEW.created_at IS DISTINCT FROM OLD.created_at
   OR NEW.description IS DISTINCT FROM OLD.description OR NEW.logo_url IS DISTINCT FROM OLD.logo_url
   OR NOT EXISTS(SELECT 1 FROM public.organization_deletion_requests r
    JOIN public.organization_deletion_progress p USING(tenant_id,request_id)
    JOIN public.background_jobs j ON j.tenant_id=r.tenant_id
    WHERE r.tenant_id=NEW.id AND r.actor_id=NEW.deleted_by AND r.accepted_version=OLD.version
     AND p.phase='FINALIZE' AND j.id=NULLIF(current_setting('app.organization_deletion_job',true),'')::uuid
     AND j.actor_id=r.actor_id AND j.job_type='ORGANIZATION_DELETE_PAGE' AND j.service_identity='organization-lifecycle'
     AND j.safe_metadata=jsonb_build_object('requestId',r.request_id,'stepId',p.step_id,'acceptedVersion',r.accepted_version)
     AND j.state='RUNNING' AND j.lease_id=NULLIF(current_setting('app.organization_deletion_lease',true),'')::uuid
     AND j.worker_id=NULLIF(current_setting('app.organization_deletion_worker',true),'')::uuid
     AND j.lease_expires_at>clock_timestamp()) THEN
   RAISE EXCEPTION 'Organization terminal authority is unavailable' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER organizations_terminal_guard BEFORE INSERT OR UPDATE OR DELETE ON organizations
 FOR EACH ROW EXECUTE FUNCTION guard_organization_terminal();
CREATE FUNCTION guard_organization_lifecycle_event() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='DELETE' OR TG_OP='UPDATE' AND
  (ROW(NEW.tenant_id,NEW.event_id,NEW.event_type,NEW.actor_id,NEW.entity_type,NEW.entity_id,NEW.entity_version,NEW.correlation_id,NEW.metadata,NEW.created_at)
   IS DISTINCT FROM ROW(OLD.tenant_id,OLD.event_id,OLD.event_type,OLD.actor_id,OLD.entity_type,OLD.entity_id,OLD.entity_version,OLD.correlation_id,OLD.metadata,OLD.created_at)
   OR OLD.ready_at IS NOT NULL AND NEW.ready_at IS DISTINCT FROM OLD.ready_at) THEN
  RAISE EXCEPTION 'Organization lifecycle history is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER organization_lifecycle_history BEFORE UPDATE OR DELETE ON organization_lifecycle_events
 FOR EACH ROW EXECUTE FUNCTION guard_organization_lifecycle_event();
CREATE FUNCTION finish_organization_deletion(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,
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
 terminal_time=GREATEST(clock_timestamp(),parent.updated_at); terminal_event=gen_random_uuid();
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
REVOKE ALL ON organization_lifecycle_events FROM PUBLIC;
REVOKE ALL ON FUNCTION guard_organization_terminal(),guard_organization_lifecycle_event(),
 finish_organization_deletion(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('089_organization_deletion_terminal');
COMMIT;
