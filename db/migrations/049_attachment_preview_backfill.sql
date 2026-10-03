BEGIN;
-- Metadata maintenance only. Scan a finite indexed page before evaluating
-- parent gates/receipts, so invalid or failed sources cannot starve later rows.
CREATE INDEX ix_attachments_preview_sweep ON attachments(tenant_id,created_at,id)
 WHERE kind='FILE' AND scan_status='CLEAN' AND deleted_at IS NULL
 AND mime_type IN ('image/png','image/jpeg','image/webp');
CREATE TABLE attachment_preview_sweeps (
 tenant_id uuid PRIMARY KEY REFERENCES organizations(id) ON DELETE RESTRICT,
 cursor_created_at timestamptz, cursor_id uuid,
 CHECK((cursor_created_at IS NULL)=(cursor_id IS NULL))
);
ALTER TABLE attachment_preview_sweeps ENABLE ROW LEVEL SECURITY;
ALTER TABLE attachment_preview_sweeps FORCE ROW LEVEL SECURITY;
CREATE POLICY attachment_preview_sweeps_tenant_isolation ON attachment_preview_sweeps
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

CREATE FUNCTION enqueue_attachment_preview_backfill(p_tenant uuid,p_limit integer)
RETURNS TABLE(visited integer,enqueued integer)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE sweep public.attachment_preview_sweeps%ROWTYPE; candidate record;
 current_card public.cards%ROWTYPE; source public.attachments%ROWTYPE;
 hint_board uuid; locked_board uuid; retry_key text; queued public.background_jobs%ROWTYPE;
 last_created timestamptz; last_id uuid;
BEGIN
 visited=0; enqueued=0;
 IF p_tenant IS NULL OR p_tenant='00000000-0000-0000-0000-000000000000'
  OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 32 THEN
  RETURN NEXT; RETURN;
 END IF;
 PERFORM 1 FROM public.organizations WHERE id=p_tenant FOR SHARE SKIP LOCKED;
 IF NOT FOUND THEN RETURN NEXT; RETURN; END IF;
 INSERT INTO public.attachment_preview_sweeps(tenant_id) VALUES(p_tenant) ON CONFLICT DO NOTHING;
 SELECT * INTO sweep FROM public.attachment_preview_sweeps WHERE tenant_id=p_tenant FOR UPDATE SKIP LOCKED;
 IF NOT FOUND THEN RETURN NEXT; RETURN; END IF;
 FOR candidate IN
  SELECT f.id,f.card_id,f.created_at FROM public.attachments f
   WHERE f.tenant_id=p_tenant AND f.kind='FILE' AND f.scan_status='CLEAN' AND f.deleted_at IS NULL
    AND f.mime_type IN ('image/png','image/jpeg','image/webp')
    AND (f.created_at,f.id)>(COALESCE(sweep.cursor_created_at,'-infinity'::timestamptz),
      COALESCE(sweep.cursor_id,'00000000-0000-0000-0000-000000000000'::uuid))
   ORDER BY f.created_at,f.id LIMIT p_limit
 LOOP
  visited=visited+1; last_created=candidate.created_at; last_id=candidate.id;
  SELECT c.board_id INTO hint_board FROM public.cards c WHERE c.id=candidate.card_id AND c.tenant_id=p_tenant;
  IF hint_board IS NULL THEN CONTINUE; END IF;
  SELECT b.id INTO locked_board FROM public.boards b WHERE b.id=hint_board AND b.tenant_id=p_tenant
   AND b.lifecycle_state='ACTIVE' FOR UPDATE SKIP LOCKED;
  IF NOT FOUND THEN CONTINUE; END IF;
  SELECT c.* INTO current_card FROM public.cards c WHERE c.id=candidate.card_id AND c.tenant_id=p_tenant
   AND c.lifecycle_state='ACTIVE' FOR UPDATE SKIP LOCKED;
  IF NOT FOUND OR current_card.board_id IS DISTINCT FROM hint_board THEN CONTINUE; END IF;
  PERFORM 1 FROM public.board_lists l WHERE l.id=current_card.list_id AND l.board_id=hint_board
   AND l.tenant_id=p_tenant AND l.lifecycle_state='ACTIVE' FOR SHARE SKIP LOCKED;
  IF NOT FOUND THEN CONTINUE; END IF;
  SELECT f.* INTO source FROM public.attachments f WHERE f.id=candidate.id AND f.tenant_id=p_tenant
   AND f.card_id=current_card.id FOR UPDATE SKIP LOCKED;
  IF NOT FOUND OR source.kind IS DISTINCT FROM 'FILE' OR source.scan_status IS DISTINCT FROM 'CLEAN'
   OR source.deleted_at IS NOT NULL OR source.scanned_at IS NULL
   OR source.mime_type NOT IN ('image/png','image/jpeg','image/webp')
   OR source.version NOT BETWEEN 2 AND 9223372036854775806
   OR source.storage_key IS DISTINCT FROM 'attachments/'||replace(p_tenant::text,'-','')||'/'||replace(source.id::text,'-','')
   THEN CONTINUE; END IF;
  IF NOT EXISTS(SELECT 1 FROM public.attachment_upload_intents u
   WHERE u.id=source.id AND u.published_attachment_id=source.id AND u.tenant_id=p_tenant
    AND u.card_id=source.card_id AND u.uploader_id=source.uploader_id AND u.status='PUBLISHED'
    AND u.expected_sha256=source.sha256 AND u.expected_size_bytes=source.size_bytes
    AND u.verified_mime_type=source.mime_type) THEN CONTINUE; END IF;
  -- A still-current committed derivative survives later metadata revisions.
  IF EXISTS(SELECT 1 FROM public.attachment_previews m
   JOIN public.attachment_preview_publications r ON r.id=m.id AND r.tenant_id=m.tenant_id
   WHERE m.tenant_id=p_tenant AND m.attachment_id=source.id AND m.card_id=source.card_id
    AND m.policy_version=1 AND r.attachment_version=m.source_version+1 AND source.version>=r.attachment_version
    AND m.source_sha256=source.sha256 AND m.source_size_bytes=source.size_bytes
    AND m.source_mime_type=source.mime_type) THEN CONTINUE; END IF;
  retry_key='attachment-preview/'||replace(source.id::text,'-','')||'/'||source.version::text;
  INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
   VALUES(gen_random_uuid(),p_tenant,'ATTACHMENT_PREVIEW',retry_key,source.uploader_id,
    'attachment-private-preview','preview-backfill',
    jsonb_build_object('attachmentId',source.id,'cardId',source.card_id,'version',source.version))
   ON CONFLICT(tenant_id,job_type,idempotency_key) DO NOTHING;
  IF FOUND THEN enqueued=enqueued+1; END IF;
  SELECT * INTO queued FROM public.background_jobs WHERE tenant_id=p_tenant
   AND job_type='ATTACHMENT_PREVIEW' AND idempotency_key=retry_key;
  IF queued.id IS NULL OR queued.actor_id IS DISTINCT FROM source.uploader_id
   OR queued.service_identity IS DISTINCT FROM 'attachment-private-preview'
   OR queued.safe_metadata IS DISTINCT FROM jsonb_build_object('attachmentId',source.id,'cardId',source.card_id,'version',source.version) THEN
   RAISE EXCEPTION 'Attachment preview backfill identity conflict' USING ERRCODE='23514';
  END IF;
  -- Existing jobs, including FAILED, keep their attempts, leases and manifests.
 END LOOP;
 UPDATE public.attachment_preview_sweeps SET
  cursor_created_at=CASE WHEN visited<p_limit THEN NULL ELSE last_created END,
  cursor_id=CASE WHEN visited<p_limit THEN NULL ELSE last_id END WHERE tenant_id=p_tenant;
 RETURN NEXT;
END $$;
REVOKE ALL ON FUNCTION enqueue_attachment_preview_backfill(uuid,integer) FROM PUBLIC;
DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN
  GRANT EXECUTE ON FUNCTION enqueue_attachment_preview_backfill(uuid,integer) TO strataai_worker_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('049_attachment_preview_backfill');
COMMIT;
