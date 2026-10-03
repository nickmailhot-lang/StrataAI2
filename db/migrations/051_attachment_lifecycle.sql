BEGIN;
ALTER TABLE attachments ADD COLUMN lifecycle_state text NOT NULL DEFAULT 'ACTIVE',
 ADD COLUMN archived_at timestamptz, ADD COLUMN deleted_by uuid;
-- Preserve existing tombstones without inventing historical archive or actor evidence.
UPDATE attachments SET lifecycle_state='DELETED' WHERE deleted_at IS NOT NULL;
ALTER TABLE attachments ADD CONSTRAINT attachments_lifecycle_shape CHECK (
 lifecycle_state IN ('ACTIVE','ARCHIVED','DELETED')
 AND (archived_at IS NULL OR archived_at BETWEEN created_at AND updated_at)
 AND ((lifecycle_state='ACTIVE' AND deleted_at IS NULL AND deleted_by IS NULL)
   OR (lifecycle_state='ARCHIVED' AND archived_at IS NOT NULL AND deleted_at IS NULL AND deleted_by IS NULL)
   OR (lifecycle_state='DELETED' AND deleted_at IS NOT NULL))
), ADD CONSTRAINT attachments_deleted_actor_fk FOREIGN KEY(tenant_id,deleted_by)
 REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT;
DROP INDEX ix_attachments_card_cursor;
CREATE INDEX ix_attachments_card_cursor ON attachments(tenant_id,card_id,created_at DESC,id DESC)
 WHERE lifecycle_state='ACTIVE' AND deleted_at IS NULL;
CREATE INDEX ix_attachments_archive_cursor ON attachments(tenant_id,card_id,created_at DESC,id DESC)
 WHERE lifecycle_state='ARCHIVED' AND deleted_at IS NULL;

CREATE FUNCTION enforce_attachment_lifecycle() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
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
 IF OLD.lifecycle_state='ACTIVE' AND NEW.lifecycle_state='ARCHIVED' AND NEW.archived_at=NEW.updated_at
  AND NEW.deleted_at IS NULL AND NEW.deleted_by IS NULL THEN RETURN NEW; END IF;
 IF OLD.lifecycle_state='ARCHIVED' AND NEW.lifecycle_state='ACTIVE' AND NEW.archived_at=OLD.archived_at
  AND NEW.deleted_at IS NULL AND NEW.deleted_by IS NULL THEN RETURN NEW; END IF;
 IF OLD.lifecycle_state='ARCHIVED' AND NEW.lifecycle_state='DELETED' AND NEW.archived_at=OLD.archived_at
  AND NEW.deleted_at=NEW.updated_at AND NEW.deleted_by IS NOT NULL THEN RETURN NEW; END IF;
 RAISE EXCEPTION 'Attachment lifecycle transition is invalid' USING ERRCODE='23514';
END $$;
CREATE TRIGGER attachments_lifecycle_guard BEFORE INSERT OR UPDATE ON attachments
 FOR EACH ROW EXECUTE FUNCTION enforce_attachment_lifecycle();

DROP INDEX ix_attachments_preview_sweep;
CREATE INDEX ix_attachments_preview_sweep ON attachments(tenant_id,created_at,id)
 WHERE kind='FILE' AND scan_status='CLEAN' AND deleted_at IS NULL AND lifecycle_state='ACTIVE'
 AND mime_type IN ('image/png','image/jpeg','image/webp');

-- New preview production requires Active sources. Scan verdict completion still
-- permits Archived quarantine metadata, and committed preview receipts remain immutable.

CREATE OR REPLACE FUNCTION load_attachment_preview_source(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint)
RETURNS TABLE(disposition text,source_size_bytes bigint,source_sha256 text,source_mime_type text,
 output_size_bytes bigint,output_sha256 text,width integer,height integer)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE a public.attachments%ROWTYPE; m public.attachment_previews%ROWTYPE;
BEGIN
 IF public.attachment_preview_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RETURN QUERY SELECT 'LEASE_LOST'::text,NULL::bigint,NULL::text,NULL::text,NULL::bigint,NULL::text,NULL::integer,NULL::integer; RETURN;
 END IF;
 SELECT f.* INTO a FROM public.attachments f
  JOIN public.attachment_upload_intents u ON u.id=f.id AND u.published_attachment_id=f.id AND u.tenant_id=f.tenant_id AND u.card_id=f.card_id
  JOIN public.cards c ON c.id=f.card_id AND c.tenant_id=f.tenant_id
  JOIN public.boards b ON b.id=c.board_id AND b.tenant_id=c.tenant_id
  JOIN public.board_lists l ON l.id=c.list_id AND l.board_id=c.board_id AND l.tenant_id=c.tenant_id
 WHERE f.id=p_attachment AND f.tenant_id=p_tenant AND f.card_id=p_card
  AND f.uploader_id=p_actor AND u.uploader_id=p_actor AND u.status='PUBLISHED'
  AND f.kind='FILE' AND f.deleted_at IS NULL AND f.lifecycle_state='ACTIVE' AND f.scan_status='CLEAN' AND f.version=p_version
  AND f.mime_type IN ('image/png','image/jpeg','image/webp')
  AND f.sha256=u.expected_sha256 AND f.size_bytes=u.expected_size_bytes AND f.mime_type=u.verified_mime_type
  AND f.storage_key='attachments/'||replace(p_tenant::text,'-','')||'/'||replace(p_attachment::text,'-','')
  AND c.lifecycle_state='ACTIVE' AND b.lifecycle_state='ACTIVE' AND l.lifecycle_state='ACTIVE';
 SELECT p.* INTO m FROM public.attachment_previews p WHERE p.id=p_job AND p.tenant_id=p_tenant
  AND p.attachment_id=p_attachment AND p.card_id=p_card AND p.source_version=p_version;
 IF public.attachment_preview_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RETURN QUERY SELECT 'LEASE_LOST'::text,NULL::bigint,NULL::text,NULL::text,NULL::bigint,NULL::text,NULL::integer,NULL::integer; RETURN;
 END IF;
 IF a.id IS NULL THEN
  RETURN QUERY SELECT 'SUPERSEDED'::text,NULL::bigint,NULL::text,NULL::text,NULL::bigint,NULL::text,NULL::integer,NULL::integer; RETURN;
 END IF;
 IF m.id IS NOT NULL AND (m.source_size_bytes IS DISTINCT FROM a.size_bytes OR m.source_sha256 IS DISTINCT FROM a.sha256
  OR m.source_mime_type IS DISTINCT FROM a.mime_type) THEN
  RETURN QUERY SELECT 'SUPERSEDED'::text,NULL::bigint,NULL::text,NULL::text,NULL::bigint,NULL::text,NULL::integer,NULL::integer; RETURN;
 END IF;
 RETURN QUERY SELECT 'READY'::text,a.size_bytes,a.sha256,a.mime_type,m.output_size_bytes,m.output_sha256,m.width,m.height;
END $$;

CREATE OR REPLACE FUNCTION finish_attachment_scan(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
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
   AND f.kind='FILE' AND f.scan_status='CLEAN' AND f.deleted_at IS NULL AND f.lifecycle_state='ACTIVE' AND f.version=p_version+1
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

CREATE OR REPLACE FUNCTION enqueue_attachment_preview_backfill(p_tenant uuid,p_limit integer)
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
   WHERE f.tenant_id=p_tenant AND f.kind='FILE' AND f.scan_status='CLEAN' AND f.deleted_at IS NULL AND f.lifecycle_state='ACTIVE'
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
   OR source.lifecycle_state IS DISTINCT FROM 'ACTIVE'
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

INSERT INTO schema_migrations(version) VALUES('051_attachment_lifecycle');
COMMIT;
