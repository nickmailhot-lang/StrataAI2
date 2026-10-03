BEGIN;
-- A crash after the last claim can leave quarantine Pending even after the
-- ordinary queue has expired the lease. Recover metadata, never provider bytes.
CREATE INDEX ix_background_jobs_scan_recovery ON background_jobs(tenant_id,created_at,id)
 WHERE job_type='ATTACHMENT_SCAN' AND state IN ('RUNNING','FAILED');
CREATE TABLE attachment_scan_sweeps (
 tenant_id uuid PRIMARY KEY REFERENCES organizations(id) ON DELETE RESTRICT,
 cursor_created_at timestamptz, cursor_id uuid,
 CHECK((cursor_created_at IS NULL)=(cursor_id IS NULL))
);
ALTER TABLE attachment_scan_sweeps ENABLE ROW LEVEL SECURITY;
ALTER TABLE attachment_scan_sweeps FORCE ROW LEVEL SECURITY;
CREATE POLICY attachment_scan_sweeps_tenant_isolation ON attachment_scan_sweeps
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

CREATE FUNCTION recover_attachment_scan_page(p_tenant uuid,p_limit integer)
RETURNS TABLE(visited integer,recovered integer)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE sweep public.attachment_scan_sweeps%ROWTYPE; candidate record;
 j public.background_jobs%ROWTYPE; final_job public.background_jobs%ROWTYPE;
 source public.attachments%ROWTYPE; current_card public.cards%ROWTYPE;
 target_attachment uuid; target_card uuid; source_version bigint; hint_board uuid; locked_board uuid;
 last_created timestamptz; last_id uuid; effect_time timestamptz; sequence_number bigint;
BEGIN
 visited=0; recovered=0;
 IF p_tenant IS NULL OR p_tenant='00000000-0000-0000-0000-000000000000'
  OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR p_limit IS NULL OR p_limit NOT BETWEEN 1 AND 32 THEN RETURN NEXT; RETURN; END IF;
 PERFORM 1 FROM public.organizations WHERE id=p_tenant FOR SHARE SKIP LOCKED;
 IF NOT FOUND THEN RETURN NEXT; RETURN; END IF;
 INSERT INTO public.attachment_scan_sweeps(tenant_id) VALUES(p_tenant) ON CONFLICT DO NOTHING;
 SELECT * INTO sweep FROM public.attachment_scan_sweeps WHERE tenant_id=p_tenant FOR UPDATE SKIP LOCKED;
 IF NOT FOUND THEN RETURN NEXT; RETURN; END IF;
 FOR candidate IN
  SELECT q.id,q.created_at FROM public.background_jobs q WHERE q.tenant_id=p_tenant
   AND q.job_type='ATTACHMENT_SCAN' AND q.state IN ('RUNNING','FAILED')
   AND (q.created_at,q.id)>(COALESCE(sweep.cursor_created_at,'-infinity'::timestamptz),
    COALESCE(sweep.cursor_id,'00000000-0000-0000-0000-000000000000'::uuid))
   ORDER BY q.created_at,q.id LIMIT p_limit
 LOOP
  visited=visited+1; last_created=candidate.created_at; last_id=candidate.id;
  SELECT * INTO j FROM public.background_jobs WHERE id=candidate.id AND tenant_id=p_tenant;
  IF NOT FOUND OR j.job_type IS DISTINCT FROM 'ATTACHMENT_SCAN' OR j.service_identity IS DISTINCT FROM 'attachment-quarantine-scan'
   OR j.actor_id='00000000-0000-0000-0000-000000000000' OR j.attempt_count<j.max_attempts
   OR NOT(j.state='FAILED' OR (j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp()))
   OR jsonb_typeof(j.safe_metadata) IS DISTINCT FROM 'object' THEN CONTINUE; END IF;
  -- Validate before casts, including legacy queue rows predating shape guards.
  IF (SELECT count(*) FROM jsonb_object_keys(j.safe_metadata))<>3
   OR jsonb_typeof(j.safe_metadata->'attachmentId') IS DISTINCT FROM 'string'
   OR jsonb_typeof(j.safe_metadata->'cardId') IS DISTINCT FROM 'string'
   OR jsonb_typeof(j.safe_metadata->'version') IS DISTINCT FROM 'number'
   OR COALESCE(j.safe_metadata->>'attachmentId','') !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
   OR COALESCE(j.safe_metadata->>'cardId','') !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
   OR COALESCE(j.safe_metadata->>'version','') !~ '^[1-9][0-9]{0,18}$' THEN CONTINUE; END IF;
  IF (j.safe_metadata->>'version')::numeric NOT BETWEEN 1 AND 9223372036854775806 THEN CONTINUE; END IF;
  target_attachment=(j.safe_metadata->>'attachmentId')::uuid; target_card=(j.safe_metadata->>'cardId')::uuid;
  source_version=(j.safe_metadata->>'version')::bigint;
  IF target_attachment='00000000-0000-0000-0000-000000000000' OR target_card='00000000-0000-0000-0000-000000000000'
   OR j.idempotency_key IS DISTINCT FROM 'attachment-scan/'||replace(target_attachment::text,'-','')||'/'||source_version::text THEN CONTINUE; END IF;
  SELECT c.board_id INTO hint_board FROM public.cards c WHERE c.id=target_card AND c.tenant_id=p_tenant;
  IF hint_board IS NULL THEN CONTINUE; END IF;
  SELECT b.id INTO locked_board FROM public.boards b WHERE b.id=hint_board AND b.tenant_id=p_tenant
   AND b.lifecycle_state IN ('ACTIVE','ARCHIVED') FOR UPDATE SKIP LOCKED;
  IF NOT FOUND THEN CONTINUE; END IF;
  SELECT c.* INTO current_card FROM public.cards c WHERE c.id=target_card AND c.tenant_id=p_tenant
   AND c.lifecycle_state IN ('ACTIVE','ARCHIVED') FOR UPDATE SKIP LOCKED;
  IF NOT FOUND OR current_card.board_id IS DISTINCT FROM hint_board THEN CONTINUE; END IF;
  PERFORM 1 FROM public.board_lists l WHERE l.id=current_card.list_id AND l.board_id=hint_board
   AND l.tenant_id=p_tenant AND l.lifecycle_state IN ('ACTIVE','ARCHIVED') FOR SHARE SKIP LOCKED;
  IF NOT FOUND THEN CONTINUE; END IF;
  -- Same owning gate order as normal scan completion; a current live claim or
  -- a non-final attempt can never be converted into terminal failure.
  SELECT q.* INTO j FROM public.background_jobs q WHERE q.id=candidate.id AND q.tenant_id=p_tenant FOR UPDATE SKIP LOCKED;
  IF NOT FOUND OR j.job_type IS DISTINCT FROM 'ATTACHMENT_SCAN' OR j.service_identity IS DISTINCT FROM 'attachment-quarantine-scan'
   OR j.attempt_count<j.max_attempts
   OR (j.state='RUNNING' AND j.version=9223372036854775807)
   OR NOT(j.state='FAILED' OR (j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp()))
   OR j.safe_metadata IS DISTINCT FROM jsonb_build_object('attachmentId',target_attachment,'cardId',target_card,'version',source_version)
   OR j.idempotency_key IS DISTINCT FROM 'attachment-scan/'||replace(target_attachment::text,'-','')||'/'||source_version::text THEN CONTINUE; END IF;
  SELECT f.* INTO source FROM public.attachments f WHERE f.id=target_attachment AND f.tenant_id=p_tenant
   AND f.card_id=target_card FOR UPDATE SKIP LOCKED;
  IF NOT FOUND OR source.kind IS DISTINCT FROM 'FILE' OR source.deleted_at IS NOT NULL
   OR source.scan_status IS DISTINCT FROM 'PENDING' OR source.scanned_at IS NOT NULL
   OR source.version IS DISTINCT FROM source_version OR source.uploader_id IS DISTINCT FROM j.actor_id
   OR source.sha256 IS NULL OR source.size_bytes IS NULL
   OR source.storage_key IS DISTINCT FROM 'attachments/'||replace(p_tenant::text,'-','')||'/'||replace(target_attachment::text,'-','')
   OR current_card.version=9223372036854775807 THEN CONTINUE; END IF;
  IF NOT EXISTS(SELECT 1 FROM public.attachment_upload_intents u WHERE u.id=target_attachment AND u.tenant_id=p_tenant
   AND u.card_id=target_card AND u.uploader_id=j.actor_id AND u.status='PUBLISHED' AND u.published_attachment_id=target_attachment
   AND u.expected_sha256=source.sha256 AND u.expected_size_bytes=source.size_bytes AND u.verified_mime_type=source.mime_type) THEN CONTINUE; END IF;
  IF j.state='RUNNING' THEN
   UPDATE public.background_jobs SET state='FAILED',lease_id=NULL,worker_id=NULL,lease_expires_at=NULL,
    last_error_code='lease_expired',updated_at=GREATEST(updated_at,clock_timestamp()),version=version+1
    WHERE id=j.id AND tenant_id=p_tenant RETURNING * INTO j;
  END IF;
  effect_time=GREATEST(clock_timestamp(),source.updated_at,current_card.updated_at);
  UPDATE public.attachments f SET scan_status='FAILED',scanned_at=effect_time,updated_at=effect_time,version=f.version+1
   WHERE f.id=target_attachment AND f.tenant_id=p_tenant AND f.card_id=target_card AND f.scan_status='PENDING' AND f.version=source_version;
  IF NOT FOUND THEN CONTINUE; END IF;
  UPDATE public.cards SET updated_at=effect_time,version=version+1 WHERE id=target_card AND tenant_id=p_tenant RETURNING * INTO current_card;
  INSERT INTO public.work_event_streams(tenant_id,board_id) VALUES(p_tenant,current_card.board_id) ON CONFLICT DO NOTHING;
  UPDATE public.work_event_streams SET last_sequence=last_sequence+1,updated_at=effect_time
   WHERE tenant_id=p_tenant AND board_id=current_card.board_id RETURNING last_sequence INTO sequence_number;
  INSERT INTO public.work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at,ready_at)
   VALUES(p_tenant,j.id,current_card.board_id,sequence_number,j.actor_id,'ATTACHMENT_SCAN_COMPLETED','Card',target_card,
    current_card.version,j.correlation_id,effect_time,effect_time);
  INSERT INTO public.audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
   VALUES(j.id,p_tenant,j.actor_id,'ATTACHMENT_SCAN_COMPLETED','Attachment',target_attachment,j.correlation_id,effect_time);
  SELECT q.* INTO final_job FROM public.background_jobs q WHERE q.id=j.id AND q.tenant_id=p_tenant;
  IF NOT FOUND OR final_job IS DISTINCT FROM j THEN
   RAISE EXCEPTION 'Attachment scan recovery terminal fence failed' USING ERRCODE='23514';
  END IF;
  recovered=recovered+1;
 END LOOP;
 UPDATE public.attachment_scan_sweeps SET
  cursor_created_at=CASE WHEN visited<p_limit THEN NULL ELSE last_created END,
  cursor_id=CASE WHEN visited<p_limit THEN NULL ELSE last_id END WHERE tenant_id=p_tenant;
 RETURN NEXT;
END $$;
REVOKE ALL ON FUNCTION recover_attachment_scan_page(uuid,integer) FROM PUBLIC;
DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN
  GRANT EXECUTE ON FUNCTION recover_attachment_scan_page(uuid,integer) TO strataai_worker_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('050_attachment_scan_recovery');
COMMIT;
