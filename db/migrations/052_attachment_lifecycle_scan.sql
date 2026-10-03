BEGIN;
-- This private count separates guarded lifecycle changes from scan-source
-- generations. It never appears in public metadata or queue references.
ALTER TABLE attachments ADD COLUMN lifecycle_revision bigint NOT NULL DEFAULT 0;
-- Upgrade real lifecycle commands already committed after 051. Only Pending
-- nondeleted files with matching canonical audit history receive a count.
UPDATE attachments a SET lifecycle_revision=history.transitions FROM (
 SELECT tenant_id,entity_id,count(*) AS transitions FROM audit_events
 WHERE entity_type='Attachment' AND event_type IN ('ATTACHMENT_ARCHIVED','ATTACHMENT_RESTORED')
 GROUP BY tenant_id,entity_id
) history WHERE a.tenant_id=history.tenant_id AND a.id=history.entity_id AND a.kind='FILE'
 AND a.scan_status='PENDING' AND a.deleted_at IS NULL AND a.archived_at IS NOT NULL
 AND history.transitions=a.version-1;
ALTER TABLE attachments ADD CONSTRAINT attachments_lifecycle_revision_shape
 CHECK(lifecycle_revision BETWEEN 0 AND version-1);
CREATE FUNCTION enforce_attachment_lifecycle_revision() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  IF NEW.lifecycle_revision<>0 THEN RAISE EXCEPTION 'New lifecycle count must be zero' USING ERRCODE='23514'; END IF;
  RETURN NEW;
 END IF;
 IF NEW.lifecycle_revision IS DISTINCT FROM OLD.lifecycle_revision THEN
  RAISE EXCEPTION 'Lifecycle count is server maintained' USING ERRCODE='23514';
 END IF;
 IF NEW.lifecycle_state IS DISTINCT FROM OLD.lifecycle_state THEN
  IF OLD.lifecycle_revision=9223372036854775807 THEN RAISE EXCEPTION 'Lifecycle count exhausted' USING ERRCODE='23514'; END IF;
  NEW.lifecycle_revision=OLD.lifecycle_revision+1;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER attachments_lifecycle_count_guard BEFORE INSERT OR UPDATE ON attachments
 FOR EACH ROW EXECUTE FUNCTION enforce_attachment_lifecycle_revision();

-- Static replacements preserve private ACLs and all immutable source, exact
-- lease, canonical upload, parent/job/File locks and final rollback fences.
CREATE OR REPLACE FUNCTION load_attachment_scan(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint)
RETURNS TABLE(disposition text,scan_size_bytes bigint,scan_sha256 text)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE a public.attachments%ROWTYPE;
BEGIN
 IF public.attachment_scan_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RETURN QUERY SELECT 'LEASE_LOST'::text,NULL::bigint,NULL::text; RETURN;
 END IF;
 SELECT f.* INTO a FROM public.attachments f JOIN public.attachment_upload_intents u
  ON u.published_attachment_id=f.id AND u.tenant_id=f.tenant_id AND u.card_id=f.card_id AND u.id=f.id
  JOIN public.cards c ON c.id=f.card_id AND c.tenant_id=f.tenant_id
  JOIN public.boards b ON b.id=c.board_id AND b.tenant_id=c.tenant_id
  JOIN public.board_lists l ON l.id=c.list_id AND l.board_id=c.board_id AND l.tenant_id=c.tenant_id
  JOIN public.organizations o ON o.id=c.tenant_id
  WHERE f.id=p_attachment AND f.tenant_id=p_tenant AND f.card_id=p_card AND f.uploader_id=p_actor
   AND u.uploader_id=p_actor AND u.status='PUBLISHED' AND f.kind='FILE' AND f.deleted_at IS NULL
   AND f.lifecycle_state IN ('ACTIVE','ARCHIVED') AND o.status IN ('ACTIVE','ARCHIVED')
   AND c.lifecycle_state IN ('ACTIVE','ARCHIVED') AND b.lifecycle_state IN ('ACTIVE','ARCHIVED') AND l.lifecycle_state IN ('ACTIVE','ARCHIVED')
   AND f.sha256=u.expected_sha256 AND f.size_bytes=u.expected_size_bytes AND f.mime_type=u.verified_mime_type
   AND f.storage_key='attachments/'||replace(p_tenant::text,'-','')||'/'||replace(p_attachment::text,'-','');
 -- Recheck after reads; expiry/replacement never returns private integrity.
 IF public.attachment_scan_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RETURN QUERY SELECT 'LEASE_LOST'::text,NULL::bigint,NULL::text; RETURN;
 END IF;
 IF a.id IS NULL THEN RETURN QUERY SELECT 'SUPERSEDED'::text,NULL::bigint,NULL::text; RETURN; END IF;
 IF a.version-a.lifecycle_revision=p_version+1 AND a.scan_status IN ('CLEAN','REJECTED','FAILED') AND EXISTS(
  SELECT 1 FROM public.audit_events e JOIN public.work_events w ON w.event_id=e.id AND w.tenant_id=e.tenant_id
  WHERE e.id=p_job AND e.tenant_id=p_tenant AND e.actor_id=p_actor AND e.event_type='ATTACHMENT_SCAN_COMPLETED'
   AND e.entity_type='Attachment' AND e.entity_id=p_attachment AND w.actor_id=p_actor
   AND w.event_type='ATTACHMENT_SCAN_COMPLETED' AND w.entity_type='Card' AND w.entity_id=p_card) THEN
  RETURN QUERY SELECT 'APPLIED'::text,NULL::bigint,NULL::text; RETURN;
 END IF;
 IF a.version-a.lifecycle_revision<>p_version OR a.version=9223372036854775807 OR a.scan_status<>'PENDING' THEN
  RETURN QUERY SELECT 'SUPERSEDED'::text,NULL::bigint,NULL::text; RETURN;
 END IF;
 RETURN QUERY SELECT 'READY'::text,a.size_bytes,a.sha256;
END $$;

CREATE OR REPLACE FUNCTION finish_attachment_scan(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint,p_size bigint,p_sha256 text,p_status text) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE hint_board uuid; current_board uuid; j public.background_jobs%ROWTYPE;
 a public.attachments%ROWTYPE; c public.cards%ROWTYPE; loaded record; effect_time timestamptz; sequence_number bigint;
BEGIN
 IF p_status IS NULL OR p_status NOT IN ('CLEAN','REJECTED','FAILED') OR p_size IS NULL OR p_sha256 IS NULL
  OR public.attachment_scan_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN RETURN 'LEASE_LOST'; END IF;
 SELECT card.board_id INTO hint_board FROM public.cards card JOIN public.attachments f
  ON f.card_id=card.id AND f.tenant_id=card.tenant_id
  WHERE card.tenant_id=p_tenant AND card.id=p_card AND f.id=p_attachment;
 IF hint_board IS NULL THEN RETURN 'SUPERSEDED'; END IF;
 -- Same parent gate/order as interactive commands. Card movement before this
 -- gate was acquired retries against its new Board; no old-Board publication.
 PERFORM 1 FROM public.organizations WHERE id=p_tenant FOR SHARE;
 SELECT id INTO current_board FROM public.boards WHERE tenant_id=p_tenant AND id=hint_board FOR UPDATE;
 SELECT * INTO c FROM public.cards WHERE tenant_id=p_tenant AND id=p_card FOR UPDATE;
 IF current_board IS NULL OR c.board_id IS DISTINCT FROM hint_board THEN RETURN 'LEASE_LOST'; END IF;
 SELECT * INTO j FROM public.background_jobs WHERE id=p_job AND tenant_id=p_tenant FOR UPDATE;
 SELECT * INTO a FROM public.attachments WHERE id=p_attachment AND tenant_id=p_tenant AND card_id=p_card FOR UPDATE;
 SELECT * INTO loaded FROM public.load_attachment_scan(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version);
 IF loaded.disposition IN ('LEASE_LOST','SUPERSEDED','APPLIED') THEN RETURN loaded.disposition; END IF;
 IF loaded.disposition IS DISTINCT FROM 'READY' OR loaded.scan_size_bytes IS DISTINCT FROM p_size
  OR loaded.scan_sha256 IS DISTINCT FROM p_sha256 THEN RETURN 'LEASE_LOST'; END IF;
 IF p_status='FAILED' AND j.attempt_count<j.max_attempts THEN RETURN 'RETRY'; END IF;
 effect_time=GREATEST(clock_timestamp(),a.updated_at,c.updated_at);
 UPDATE public.attachments SET scan_status=p_status,scanned_at=effect_time,updated_at=effect_time,version=version+1
  WHERE id=p_attachment AND tenant_id=p_tenant AND card_id=p_card AND version=a.version AND version-lifecycle_revision=p_version AND scan_status='PENDING';
 IF NOT FOUND THEN RETURN 'SUPERSEDED'; END IF;
 UPDATE public.cards SET updated_at=effect_time,version=version+1 WHERE id=p_card AND tenant_id=p_tenant RETURNING * INTO c;
 INSERT INTO public.work_event_streams(tenant_id,board_id) VALUES(p_tenant,c.board_id) ON CONFLICT DO NOTHING;
 UPDATE public.work_event_streams SET last_sequence=last_sequence+1,updated_at=effect_time
  WHERE tenant_id=p_tenant AND board_id=c.board_id RETURNING last_sequence INTO sequence_number;
 INSERT INTO public.work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at,ready_at)
  VALUES(p_tenant,p_job,c.board_id,sequence_number,p_actor,'ATTACHMENT_SCAN_COMPLETED','Card',p_card,c.version,j.correlation_id,effect_time,effect_time);
 INSERT INTO public.audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
  VALUES(p_job,p_tenant,p_actor,'ATTACHMENT_SCAN_COMPLETED','Attachment',p_attachment,j.correlation_id,effect_time);
 IF public.attachment_scan_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RAISE EXCEPTION 'Attachment scan lease fence failed' USING ERRCODE='23514';
 END IF;
 RETURN 'APPLIED';
END $$;

CREATE OR REPLACE FUNCTION recover_attachment_scan_page(p_tenant uuid,p_limit integer)
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
   OR source.version-source.lifecycle_revision IS DISTINCT FROM source_version OR source.version=9223372036854775807 OR source.uploader_id IS DISTINCT FROM j.actor_id
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
   WHERE f.id=target_attachment AND f.tenant_id=p_tenant AND f.card_id=target_card AND f.scan_status='PENDING' AND f.version=source.version AND f.version-f.lifecycle_revision=source_version;
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
   AND f.kind='FILE' AND f.scan_status='CLEAN' AND f.deleted_at IS NULL AND f.lifecycle_state='ACTIVE' AND f.version-f.lifecycle_revision=p_version+1
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
INSERT INTO schema_migrations(version) VALUES('052_attachment_lifecycle_scan');
COMMIT;
