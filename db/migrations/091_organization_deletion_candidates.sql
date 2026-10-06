BEGIN;
-- Match reference-cleanup candidates as well as non-deleted rows so a seek
-- cannot fall back to sorting an entire tenant on each continuation.
CREATE INDEX organization_candidate_cards ON cards(tenant_id,id)
 WHERE lifecycle_state<>'DELETED' OR cover_attachment_id IS NOT NULL;
CREATE INDEX organization_candidate_boards ON boards(tenant_id,id)
 WHERE lifecycle_state<>'DELETED' OR background_image_id IS NOT NULL OR background_type='IMAGE';
CREATE FUNCTION load_organization_deletion_page(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_request uuid,p_step uuid,p_version bigint,p_limit integer) RETURNS jsonb
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE parent public.organizations%ROWTYPE; root public.organization_deletion_requests%ROWTYPE;
 progress public.organization_deletion_progress%ROWTYPE; claim public.background_jobs%ROWTYPE;
 candidates jsonb; last_id uuid;
BEGIN
 IF p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 128 THEN RETURN NULL; END IF;
 SELECT * INTO parent FROM public.organizations WHERE id=p_tenant FOR SHARE;
 IF parent.id IS NULL OR parent.status<>'DELETING' OR parent.version IS DISTINCT FROM p_version THEN RETURN NULL; END IF;
 SELECT * INTO root FROM public.organization_deletion_requests WHERE tenant_id=p_tenant;
 IF root.request_id IS DISTINCT FROM p_request OR root.actor_id IS DISTINCT FROM p_actor
  OR root.accepted_version IS DISTINCT FROM p_version THEN RETURN NULL; END IF;
 SELECT * INTO claim FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='ORGANIZATION_DELETE_PAGE' AND service_identity='organization-lifecycle'
  AND idempotency_key='organization-deletion/'||replace(p_request::text,'-','')||'/'||replace(p_step::text,'-','')
  AND correlation_id=root.correlation_id AND safe_metadata=jsonb_build_object('requestId',p_request,'stepId',p_step,'acceptedVersion',p_version)
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR SHARE;
 IF claim.id IS NULL THEN RETURN NULL; END IF;
 SELECT * INTO progress FROM public.organization_deletion_progress WHERE tenant_id=p_tenant AND request_id=p_request FOR SHARE;
 IF progress.step_id IS DISTINCT FROM p_step OR progress.phase='COMPLETE' THEN RETURN NULL; END IF;
 -- UUID seek includes archived descendants. No normal directory, membership,
 -- parent lifecycle, name, content or provider-key projection is consulted.
 IF progress.phase='ATTACHMENTS' THEN
  SELECT jsonb_agg(jsonb_build_object('id',s.id,'boardId',s.board_id,'cardId',s.card_id,'version',s.version,'state',s.lifecycle_state) ORDER BY s.id)
   INTO candidates FROM (SELECT a.id,c.board_id,a.card_id,a.version,a.lifecycle_state
    FROM public.attachments a JOIN public.cards c ON c.id=a.card_id AND c.tenant_id=a.tenant_id
    WHERE a.tenant_id=p_tenant AND (progress.after_id IS NULL OR a.id>progress.after_id) AND a.lifecycle_state<>'DELETED'
    ORDER BY a.id LIMIT p_limit) s;
 ELSIF progress.phase='CARDS' THEN
  SELECT jsonb_agg(jsonb_build_object('id',s.id,'boardId',s.board_id,'cardId',s.id,'version',s.version,'state',s.lifecycle_state) ORDER BY s.id)
   INTO candidates FROM (SELECT id,board_id,version,lifecycle_state FROM public.cards
    WHERE tenant_id=p_tenant AND (progress.after_id IS NULL OR id>progress.after_id)
     AND (lifecycle_state<>'DELETED' OR cover_attachment_id IS NOT NULL) ORDER BY id LIMIT p_limit) s;
 ELSIF progress.phase='LISTS' THEN
  SELECT jsonb_agg(jsonb_build_object('id',s.id,'boardId',s.board_id,'cardId',NULL,'version',s.version,'state',s.lifecycle_state) ORDER BY s.id)
   INTO candidates FROM (SELECT id,board_id,version,lifecycle_state FROM public.board_lists
    WHERE tenant_id=p_tenant AND (progress.after_id IS NULL OR id>progress.after_id) AND lifecycle_state<>'DELETED'
    ORDER BY id LIMIT p_limit) s;
 ELSIF progress.phase='BOARDS' THEN
  SELECT jsonb_agg(jsonb_build_object('id',s.id,'boardId',s.id,'cardId',NULL,'version',s.version,'state',s.lifecycle_state) ORDER BY s.id)
   INTO candidates FROM (SELECT id,version,lifecycle_state FROM public.boards
    WHERE tenant_id=p_tenant AND (progress.after_id IS NULL OR id>progress.after_id)
     AND (lifecycle_state<>'DELETED' OR background_image_id IS NOT NULL OR background_type='IMAGE') ORDER BY id LIMIT p_limit) s;
 ELSIF progress.phase<>'FINALIZE' THEN RETURN NULL;
 END IF;
 candidates=COALESCE(candidates,'[]'::jsonb);
 IF jsonb_array_length(candidates)=p_limit THEN last_id=(candidates->(p_limit-1)->>'id')::uuid; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND state='RUNNING'
  AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp()) THEN RETURN NULL; END IF;
 RETURN jsonb_build_object('phase',progress.phase,'afterId',progress.after_id,'version',progress.version,'items',candidates,'nextCursor',last_id);
END $$;
REVOKE ALL ON FUNCTION load_organization_deletion_page(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,integer) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('091_organization_deletion_candidates');
COMMIT;
