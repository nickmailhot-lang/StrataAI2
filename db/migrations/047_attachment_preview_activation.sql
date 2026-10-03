BEGIN;
-- Old/default Workers must not claim or expire a job type they cannot dispatch.
CREATE OR REPLACE FUNCTION claim_background_job(p_worker_id uuid)
RETURNS SETOF background_jobs LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE v_now timestamptz := clock_timestamp();
BEGIN
    IF p_worker_id IS NULL THEN RAISE EXCEPTION 'worker identity is required'; END IF;
    -- A crash on the final attempt must eventually reach a terminal state.
    UPDATE background_jobs SET state = 'FAILED', lease_id = NULL, worker_id = NULL,
        lease_expires_at = NULL, last_error_code = 'lease_expired',
        updated_at = v_now, version = version + 1
    WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
      AND (job_type<>'ATTACHMENT_PREVIEW' OR current_setting('app.attachment_preview_worker',true)='enabled')
      AND state = 'RUNNING' AND lease_expires_at <= v_now AND attempt_count >= max_attempts;
    RETURN QUERY
    WITH candidate AS (
        SELECT id FROM background_jobs
        WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
          AND (job_type<>'ATTACHMENT_PREVIEW' OR current_setting('app.attachment_preview_worker',true)='enabled')
          AND attempt_count < max_attempts
          AND ((state = 'PENDING' AND available_at <= v_now)
            OR (state = 'RUNNING' AND lease_expires_at <= v_now))
        ORDER BY available_at, created_at, id
        LIMIT 1 FOR UPDATE SKIP LOCKED
    )
    UPDATE background_jobs j SET state = 'RUNNING', attempt_count = attempt_count + 1,
        lease_id = gen_random_uuid(), worker_id = p_worker_id,
        lease_expires_at = v_now + interval '2 minutes', updated_at = v_now, version = version + 1
    FROM candidate c WHERE j.id = c.id RETURNING j.*;
END;
$$;

-- The legacy eleven-argument scan finish remains available and does not enqueue
-- new job types. Only the explicitly enabled new Worker opts into this overload.
CREATE FUNCTION finish_attachment_scan(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint,p_size bigint,p_sha256 text,p_status text,p_preview_enabled boolean) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE result text; j public.background_jobs%ROWTYPE; a public.attachments%ROWTYPE; published public.background_jobs%ROWTYPE; retry_key text;
BEGIN
 result=public.finish_attachment_scan(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version,p_size,p_sha256,p_status);
 IF result<>'APPLIED' OR p_preview_enabled IS NOT TRUE OR p_status IS DISTINCT FROM 'CLEAN' THEN RETURN result; END IF;
 IF public.attachment_scan_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RAISE EXCEPTION 'Attachment preview queue lease fence failed' USING ERRCODE='23514';
 END IF;
 SELECT * INTO j FROM public.background_jobs WHERE id=p_job AND tenant_id=p_tenant;
 SELECT f.* INTO a FROM public.attachments f JOIN public.cards c ON c.id=f.card_id AND c.tenant_id=f.tenant_id
  JOIN public.boards b ON b.id=c.board_id AND b.tenant_id=c.tenant_id
  JOIN public.board_lists l ON l.id=c.list_id AND l.board_id=c.board_id AND l.tenant_id=c.tenant_id
  WHERE f.id=p_attachment AND f.tenant_id=p_tenant AND f.card_id=p_card AND f.uploader_id=p_actor
   AND f.kind='FILE' AND f.scan_status='CLEAN' AND f.deleted_at IS NULL AND f.version=p_version+1
   AND f.version<9223372036854775807 AND f.mime_type IN ('image/png','image/jpeg','image/webp')
   AND f.size_bytes=p_size AND f.sha256=p_sha256
   AND c.lifecycle_state='ACTIVE' AND b.lifecycle_state='ACTIVE' AND l.lifecycle_state='ACTIVE';
 IF a.id IS NULL THEN RETURN result; END IF;
 retry_key='attachment-preview/'||replace(a.id::text,'-','')||'/'||a.version::text;
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
  VALUES(gen_random_uuid(),p_tenant,'ATTACHMENT_PREVIEW',retry_key,p_actor,'attachment-private-preview',j.correlation_id,
   jsonb_build_object('attachmentId',p_attachment,'cardId',p_card,'version',a.version))
 ON CONFLICT(tenant_id,job_type,idempotency_key) DO NOTHING;
 SELECT * INTO published FROM public.background_jobs WHERE tenant_id=p_tenant AND job_type='ATTACHMENT_PREVIEW' AND idempotency_key=retry_key;
 IF published.id IS NULL OR published.actor_id IS DISTINCT FROM p_actor OR published.service_identity IS DISTINCT FROM 'attachment-private-preview'
  OR published.safe_metadata IS DISTINCT FROM jsonb_build_object('attachmentId',p_attachment,'cardId',p_card,'version',a.version) THEN
  RAISE EXCEPTION 'Attachment preview queue identity conflict' USING ERRCODE='23514';
 END IF;
 -- Queue insertion happens after scan effects. This final check fences both,
 -- including expiry injected after the newly published job was inserted.
 IF public.attachment_scan_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RAISE EXCEPTION 'Attachment preview queue lease fence failed' USING ERRCODE='23514';
 END IF;
 RETURN result;
END $$;
REVOKE ALL ON FUNCTION finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,boolean) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('047_attachment_preview_activation');
COMMIT;