BEGIN;
CREATE TABLE organization_deletion_steps (
 tenant_id uuid NOT NULL, request_id uuid NOT NULL, step_id uuid NOT NULL,
 phase text NOT NULL CHECK(phase IN ('ATTACHMENTS','CARDS','LISTS','BOARDS')),
 start_after_id uuid, end_after_id uuid, checkpoint_version bigint NOT NULL CHECK(checkpoint_version>0),
 candidate_count integer NOT NULL CHECK(candidate_count BETWEEN 0 AND 128),
 next_step_id uuid NOT NULL CHECK(next_step_id<>'00000000-0000-0000-0000-000000000000'::uuid AND next_step_id<>step_id),
 completed_at timestamptz NOT NULL CHECK(isfinite(completed_at)),
 PRIMARY KEY(tenant_id,step_id), UNIQUE(tenant_id,next_step_id),
 FOREIGN KEY(tenant_id,request_id) REFERENCES organization_deletion_requests(tenant_id,request_id) ON DELETE RESTRICT
);
ALTER TABLE organization_deletion_steps ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_deletion_steps FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_deletion_steps_tenant ON organization_deletion_steps
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION guard_organization_deletion_step() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Organization deletion steps are immutable' USING ERRCODE='23514'; END $$;
CREATE TRIGGER organization_deletion_steps_history BEFORE UPDATE OR DELETE ON organization_deletion_steps
 FOR EACH ROW EXECUTE FUNCTION guard_organization_deletion_step();
CREATE FUNCTION organization_deletion_page_is_live(p_tenant uuid,p_actor uuid) RETURNS boolean
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT session_user='strataai_worker_runtime' AND p_tenant=NULLIF(current_setting('app.tenant_id',true),'')::uuid
 AND EXISTS(SELECT 1 FROM public.organization_deletion_requests r JOIN public.organizations o ON o.id=r.tenant_id
  JOIN public.organization_deletion_progress p USING(tenant_id,request_id)
  JOIN public.background_jobs j ON j.tenant_id=r.tenant_id
  WHERE r.tenant_id=p_tenant AND r.actor_id=p_actor AND o.status='DELETING' AND o.version=r.accepted_version
   AND p.phase='ATTACHMENTS' AND j.id=NULLIF(current_setting('app.organization_deletion_job',true),'')::uuid
   AND j.actor_id=r.actor_id AND j.job_type='ORGANIZATION_DELETE_PAGE' AND j.service_identity='organization-lifecycle'
   AND j.safe_metadata=jsonb_build_object('requestId',r.request_id,'stepId',p.step_id,'acceptedVersion',r.accepted_version)
   AND j.state='RUNNING' AND j.worker_id=NULLIF(current_setting('app.organization_deletion_worker',true),'')::uuid
   AND j.lease_id=NULLIF(current_setting('app.organization_deletion_lease',true),'')::uuid AND j.lease_expires_at>clock_timestamp());
$$;
CREATE OR REPLACE FUNCTION enforce_attachment_lifecycle() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  IF NEW.lifecycle_state<>'ACTIVE' OR NEW.archived_at IS NOT NULL OR NEW.deleted_at IS NOT NULL OR NEW.deleted_by IS NOT NULL THEN
   RAISE EXCEPTION 'New attachments must be active' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
 END IF;
 IF OLD.lifecycle_state='DELETED' AND NEW IS DISTINCT FROM OLD THEN
  RAISE EXCEPTION 'Deleted attachments are immutable' USING ERRCODE='23514';
 END IF;
 IF NEW.id IS DISTINCT FROM OLD.id OR NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
  OR NEW.card_id IS DISTINCT FROM OLD.card_id OR NEW.uploader_id IS DISTINCT FROM OLD.uploader_id
  OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
  RAISE EXCEPTION 'Attachment ownership is immutable' USING ERRCODE='23514';
 END IF;
 IF NEW.lifecycle_state IS NOT DISTINCT FROM OLD.lifecycle_state THEN
  IF NEW.archived_at IS DISTINCT FROM OLD.archived_at OR NEW.deleted_at IS DISTINCT FROM OLD.deleted_at OR NEW.deleted_by IS DISTINCT FROM OLD.deleted_by THEN
   RAISE EXCEPTION 'Attachment lifecycle evidence is immutable outside a transition' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
 END IF;
 IF OLD.version=9223372036854775807 OR NEW.version<>OLD.version+1 OR NEW.updated_at<OLD.updated_at THEN
  RAISE EXCEPTION 'Attachment lifecycle revision is invalid' USING ERRCODE='23514';
 END IF;
 -- Organization completion can directly tombstone an active source while
 -- preserving its prior archive history, using ONLY the leased Worker scope.
 -- Keep the private capability call in a separate procedural branch: SQL
 -- boolean expressions may evaluate it before unrelated transition predicates.
 IF OLD.lifecycle_state='ACTIVE' AND NEW.lifecycle_state='DELETED' AND session_user='strataai_worker_runtime' THEN
  IF NEW.archived_at IS NOT DISTINCT FROM OLD.archived_at AND NEW.deleted_at=NEW.updated_at
   AND NEW.deleted_by IS NOT NULL THEN
   IF public.organization_deletion_page_is_live(NEW.tenant_id,NEW.deleted_by) THEN RETURN NEW; END IF;
  END IF;
 END IF;
 IF OLD.lifecycle_state='ACTIVE' AND NEW.lifecycle_state='ARCHIVED' AND NEW.archived_at=NEW.updated_at
  AND NEW.deleted_at IS NULL AND NEW.deleted_by IS NULL THEN RETURN NEW; END IF;
 IF OLD.lifecycle_state='ARCHIVED' AND NEW.lifecycle_state='ACTIVE' AND NEW.archived_at=OLD.archived_at
  AND NEW.deleted_at IS NULL AND NEW.deleted_by IS NULL THEN RETURN NEW; END IF;
 IF OLD.lifecycle_state='ARCHIVED' AND NEW.lifecycle_state='DELETED' AND NEW.archived_at=OLD.archived_at
  AND NEW.deleted_at=NEW.updated_at AND NEW.deleted_by IS NOT NULL THEN RETURN NEW; END IF;
 RAISE EXCEPTION 'Attachment lifecycle transition is invalid' USING ERRCODE='23514';
END $$;

CREATE FUNCTION append_organization_deletion_work_event(p_tenant uuid,p_board uuid,p_actor uuid,p_type text,
 p_entity_type text,p_entity uuid,p_version bigint,p_correlation text,p_time timestamptz) RETURNS void
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE event_id uuid=gen_random_uuid(); next_sequence bigint;
BEGIN
 IF p_type<>'ATTACHMENT_DELETED' THEN
  INSERT INTO public.audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
   VALUES(gen_random_uuid(),p_tenant,p_actor,p_type,p_entity_type,p_entity,p_correlation,p_time);
 END IF;
 INSERT INTO public.work_event_streams(tenant_id,board_id) VALUES(p_tenant,p_board) ON CONFLICT DO NOTHING;
 UPDATE public.work_event_streams SET last_sequence=last_sequence+1,updated_at=GREATEST(updated_at,p_time)
  WHERE tenant_id=p_tenant AND board_id=p_board RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
  VALUES(p_tenant,event_id,p_board,next_sequence,p_actor,p_type,p_entity_type,p_entity,p_version,p_correlation,p_time);
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
  VALUES(gen_random_uuid(),p_tenant,'WORK_EVENT_READY','work-event/'||replace(event_id::text,'-',''),p_actor,
   'work-event-delivery',p_correlation,jsonb_build_object('eventId',event_id,'boardId',p_board));
END $$;
CREATE FUNCTION apply_organization_deletion_page(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_request uuid,p_step uuid,p_version bigint,p_limit integer) RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE parent public.organizations%ROWTYPE; root public.organization_deletion_requests%ROWTYPE;
 progress public.organization_deletion_progress%ROWTYPE; claim public.background_jobs%ROWTYPE; receipt public.organization_deletion_steps%ROWTYPE;
 a public.attachments%ROWTYPE; c public.cards%ROWTYPE; l public.board_lists%ROWTYPE; b public.boards%ROWTYPE;
 page jsonb; candidate jsonb; effect_time timestamptz; next_step uuid; next_phase text; next_cursor uuid; end_cursor uuid;
 changed_cover boolean; was_deleted boolean;
BEGIN
 IF p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 128 THEN RETURN false; END IF;
 SELECT * INTO parent FROM public.organizations WHERE id=p_tenant FOR UPDATE;
 SELECT * INTO root FROM public.organization_deletion_requests WHERE tenant_id=p_tenant;
 IF parent.id IS NULL OR root.request_id IS DISTINCT FROM p_request OR root.actor_id IS DISTINCT FROM p_actor
  OR root.accepted_version IS DISTINCT FROM p_version THEN RETURN false; END IF;
 SELECT * INTO claim FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='ORGANIZATION_DELETE_PAGE' AND service_identity='organization-lifecycle' AND correlation_id=root.correlation_id
  AND idempotency_key='organization-deletion/'||replace(p_request::text,'-','')||'/'||replace(p_step::text,'-','')
  AND safe_metadata=jsonb_build_object('requestId',p_request,'stepId',p_step,'acceptedVersion',p_version)
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF claim.id IS NULL THEN RETURN false; END IF;
 SELECT * INTO receipt FROM public.organization_deletion_steps WHERE tenant_id=p_tenant AND step_id=p_step AND request_id=p_request;
 IF receipt.step_id IS NOT NULL THEN
  RETURN (parent.status='DELETING' AND parent.version=p_version OR parent.status='DELETED' AND parent.version=p_version+1)
   AND EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND job_type='ORGANIZATION_DELETE_PAGE'
    AND actor_id=p_actor AND service_identity='organization-lifecycle' AND correlation_id=root.correlation_id
    AND idempotency_key='organization-deletion/'||replace(p_request::text,'-','')||'/'||replace(receipt.next_step_id::text,'-','')
    AND safe_metadata=jsonb_build_object('requestId',p_request,'stepId',receipt.next_step_id,'acceptedVersion',p_version))
   AND EXISTS(SELECT 1 FROM public.background_jobs WHERE id=p_job AND tenant_id=p_tenant AND lease_expires_at>clock_timestamp());
 END IF;
 SELECT * INTO progress FROM public.organization_deletion_progress WHERE tenant_id=p_tenant AND request_id=p_request FOR UPDATE;
 IF progress.step_id IS DISTINCT FROM p_step THEN RETURN false; END IF;
 IF progress.phase IN ('FINALIZE','COMPLETE') THEN
  RETURN public.finish_organization_deletion(p_tenant,p_job,p_actor,p_worker,p_lease,p_request,p_step,p_version);
 END IF;
 IF parent.status<>'DELETING' OR parent.version<>p_version THEN RETURN false; END IF;
 page=public.load_organization_deletion_page(p_tenant,p_job,p_actor,p_worker,p_lease,p_request,p_step,p_version,p_limit);
 IF page IS NULL THEN RETURN false; END IF;
 PERFORM set_config('app.organization_deletion_job',p_job::text,true);
 PERFORM set_config('app.organization_deletion_worker',p_worker::text,true);
 PERFORM set_config('app.organization_deletion_lease',p_lease::text,true);
 FOR candidate IN SELECT value FROM jsonb_array_elements(page->'items') LOOP
  -- Every source is re-read under its Board/child locks before effects.
  PERFORM 1 FROM public.boards WHERE tenant_id=p_tenant AND id=(candidate->>'boardId')::uuid FOR UPDATE;
  IF NOT FOUND THEN RAISE EXCEPTION 'Deletion candidate parent changed' USING ERRCODE='23514'; END IF;
  IF progress.phase='ATTACHMENTS' THEN
   SELECT * INTO c FROM public.cards WHERE tenant_id=p_tenant AND id=(candidate->>'cardId')::uuid FOR UPDATE;
   SELECT * INTO a FROM public.attachments WHERE tenant_id=p_tenant AND id=(candidate->>'id')::uuid FOR UPDATE;
   IF a.version IS DISTINCT FROM (candidate->>'version')::bigint OR a.lifecycle_state='DELETED'
    OR a.card_id IS DISTINCT FROM c.id OR c.board_id IS DISTINCT FROM (candidate->>'boardId')::uuid THEN
    RAISE EXCEPTION 'Deletion candidate changed' USING ERRCODE='23514'; END IF;
   effect_time=GREATEST(clock_timestamp(),parent.updated_at,c.updated_at,a.updated_at); changed_cover=c.cover_attachment_id=a.id;
   IF c.lifecycle_state<>'DELETED' OR changed_cover THEN
    UPDATE public.cards SET cover_attachment_id=CASE WHEN changed_cover THEN NULL ELSE cover_attachment_id END,
     version=version+1,updated_at=effect_time WHERE tenant_id=p_tenant AND id=c.id RETURNING * INTO c;
   END IF;
   UPDATE public.attachments SET lifecycle_state='DELETED',deleted_at=effect_time,deleted_by=p_actor,version=version+1,updated_at=effect_time
    WHERE tenant_id=p_tenant AND id=a.id;
   INSERT INTO public.audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
    VALUES(gen_random_uuid(),p_tenant,p_actor,'ATTACHMENT_DELETED','Attachment',a.id,root.correlation_id,effect_time);
   IF changed_cover THEN PERFORM public.append_organization_deletion_work_event(p_tenant,c.board_id,p_actor,'CARD_COVER_CHANGED','Card',c.id,c.version,root.correlation_id,effect_time); END IF;
   -- Attachment audit identity differs from its content-free Card invalidation.
   PERFORM public.append_organization_deletion_work_event(p_tenant,c.board_id,p_actor,'ATTACHMENT_DELETED','Card',c.id,c.version,root.correlation_id,effect_time);
  ELSIF progress.phase='CARDS' THEN
   SELECT * INTO c FROM public.cards WHERE tenant_id=p_tenant AND id=(candidate->>'id')::uuid FOR UPDATE;
   IF c.version IS DISTINCT FROM (candidate->>'version')::bigint THEN RAISE EXCEPTION 'Deletion candidate changed' USING ERRCODE='23514'; END IF;
   was_deleted=c.lifecycle_state='DELETED'; changed_cover=c.cover_attachment_id IS NOT NULL;
   effect_time=GREATEST(clock_timestamp(),parent.updated_at,c.updated_at);
   UPDATE public.cards SET lifecycle_state='DELETED',cover_attachment_id=NULL,version=version+1,updated_at=effect_time,
    deleted_at=CASE WHEN was_deleted THEN deleted_at ELSE effect_time END,deleted_by=CASE WHEN was_deleted THEN deleted_by ELSE p_actor END
    WHERE tenant_id=p_tenant AND id=c.id RETURNING * INTO c;
   IF changed_cover THEN PERFORM public.append_organization_deletion_work_event(p_tenant,c.board_id,p_actor,'CARD_COVER_CHANGED','Card',c.id,c.version,root.correlation_id,effect_time); END IF;
   IF NOT was_deleted THEN PERFORM public.append_organization_deletion_work_event(p_tenant,c.board_id,p_actor,'CARD_DELETED','Card',c.id,c.version,root.correlation_id,effect_time); END IF;
  ELSIF progress.phase='LISTS' THEN
   SELECT * INTO l FROM public.board_lists WHERE tenant_id=p_tenant AND id=(candidate->>'id')::uuid FOR UPDATE;
   IF l.version IS DISTINCT FROM (candidate->>'version')::bigint OR l.lifecycle_state='DELETED' THEN RAISE EXCEPTION 'Deletion candidate changed' USING ERRCODE='23514'; END IF;
   effect_time=GREATEST(clock_timestamp(),parent.updated_at,l.updated_at);
   UPDATE public.board_lists SET lifecycle_state='DELETED',deleted_at=effect_time,deleted_by=p_actor,version=version+1,updated_at=effect_time
    WHERE tenant_id=p_tenant AND id=l.id RETURNING * INTO l;
   PERFORM public.append_organization_deletion_work_event(p_tenant,l.board_id,p_actor,'LIST_DELETED','List',l.id,l.version,root.correlation_id,effect_time);
  ELSIF progress.phase='BOARDS' THEN
   SELECT * INTO b FROM public.boards WHERE tenant_id=p_tenant AND id=(candidate->>'id')::uuid FOR UPDATE;
   IF b.version IS DISTINCT FROM (candidate->>'version')::bigint THEN RAISE EXCEPTION 'Deletion candidate changed' USING ERRCODE='23514'; END IF;
   was_deleted=b.lifecycle_state='DELETED'; effect_time=GREATEST(clock_timestamp(),parent.updated_at,b.updated_at);
   UPDATE public.boards SET lifecycle_state='DELETED',background_type='COLOR',
    background_value=CASE WHEN background_type='IMAGE' OR background_image_id IS NOT NULL THEN NULL ELSE background_value END,background_image_id=NULL,
    deleted_at=CASE WHEN was_deleted THEN deleted_at ELSE effect_time END,deleted_by=CASE WHEN was_deleted THEN deleted_by ELSE p_actor END,
    version=version+1,updated_at=effect_time WHERE tenant_id=p_tenant AND id=b.id RETURNING * INTO b;
   PERFORM public.append_organization_deletion_work_event(p_tenant,b.id,p_actor,CASE WHEN was_deleted THEN 'BOARD_UPDATED' ELSE 'BOARD_DELETED' END,'Board',b.id,b.version,root.correlation_id,effect_time);
  END IF;
 END LOOP;
 next_step=gen_random_uuid(); next_cursor=(page->>'nextCursor')::uuid;
 end_cursor=COALESCE((page->'items'->(jsonb_array_length(page->'items')-1)->>'id')::uuid,progress.after_id);
 next_phase=CASE WHEN next_cursor IS NOT NULL THEN progress.phase ELSE CASE progress.phase
  WHEN 'ATTACHMENTS' THEN 'CARDS' WHEN 'CARDS' THEN 'LISTS' WHEN 'LISTS' THEN 'BOARDS' WHEN 'BOARDS' THEN 'FINALIZE' END END;
 effect_time=GREATEST(clock_timestamp(),progress.updated_at);
 INSERT INTO public.organization_deletion_steps(tenant_id,request_id,step_id,phase,start_after_id,end_after_id,checkpoint_version,candidate_count,next_step_id,completed_at)
  VALUES(p_tenant,p_request,p_step,progress.phase,progress.after_id,end_cursor,progress.version,jsonb_array_length(page->'items'),next_step,effect_time);
 UPDATE public.organization_deletion_progress SET phase=next_phase,after_id=next_cursor,step_id=next_step,version=version+1,updated_at=effect_time
  WHERE tenant_id=p_tenant;
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
  VALUES(gen_random_uuid(),p_tenant,'ORGANIZATION_DELETE_PAGE','organization-deletion/'||replace(p_request::text,'-','')||'/'||replace(next_step::text,'-',''),
   p_actor,'organization-lifecycle',root.correlation_id,jsonb_build_object('requestId',p_request,'stepId',next_step,'acceptedVersion',p_version));
 SET CONSTRAINTS ALL IMMEDIATE;
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND state='RUNNING'
  AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp()) THEN
  RAISE EXCEPTION 'Organization deletion page lease expired' USING ERRCODE='23514'; END IF;
 RETURN true;
END $$;
REVOKE ALL ON organization_deletion_steps FROM PUBLIC;
REVOKE ALL ON FUNCTION guard_organization_deletion_step(),organization_deletion_page_is_live(uuid,uuid),
 append_organization_deletion_work_event(uuid,uuid,uuid,text,text,uuid,bigint,text,timestamptz),
 apply_organization_deletion_page(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,integer) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('092_organization_deletion_pages');
COMMIT;
