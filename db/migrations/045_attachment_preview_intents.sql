BEGIN;
-- Private, metadata-only recovery ledger. No job is enqueued by this migration.
ALTER TABLE background_jobs ADD CONSTRAINT uq_background_jobs_id_tenant UNIQUE(id,tenant_id);
CREATE TABLE attachment_previews (
 id uuid PRIMARY KEY CHECK(id<>'00000000-0000-0000-0000-000000000000'),
 tenant_id uuid NOT NULL, attachment_id uuid NOT NULL, card_id uuid NOT NULL,
 source_version bigint NOT NULL CHECK(source_version BETWEEN 2 AND 9223372036854775806),
 source_size_bytes bigint NOT NULL CHECK(source_size_bytes BETWEEN 1 AND 1073741824),
 source_sha256 text NOT NULL CHECK(source_sha256 ~ '^[0-9a-f]{64}$'),
 source_mime_type text NOT NULL CHECK(source_mime_type IN ('image/png','image/jpeg','image/webp')),
 output_size_bytes bigint NOT NULL CHECK(output_size_bytes BETWEEN 45 AND 8388608),
 output_sha256 text NOT NULL CHECK(output_sha256 ~ '^[0-9a-f]{64}$'),
 width integer NOT NULL CHECK(width BETWEEN 1 AND 1024),
 height integer NOT NULL CHECK(height BETWEEN 1 AND 1024),
 policy_version integer NOT NULL DEFAULT 1 CHECK(policy_version=1),
 created_at timestamptz NOT NULL,
 UNIQUE(tenant_id,attachment_id,source_version),
 CHECK(id<>attachment_id),
 FOREIGN KEY(id,tenant_id) REFERENCES background_jobs(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(attachment_id,tenant_id,card_id) REFERENCES attachments(id,tenant_id,card_id) ON DELETE RESTRICT
);
ALTER TABLE attachment_previews ENABLE ROW LEVEL SECURITY;
ALTER TABLE attachment_previews FORCE ROW LEVEL SECURITY;
CREATE POLICY attachment_previews_tenant_isolation ON attachment_previews
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
-- A declared encoding cannot be replaced on retry or redirected to new bytes.
CREATE FUNCTION enforce_attachment_preview_manifest() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='INSERT' AND NOT EXISTS(
  SELECT 1 FROM public.background_jobs j WHERE j.id=NEW.id AND j.tenant_id=NEW.tenant_id
   AND j.job_type='ATTACHMENT_PREVIEW' AND j.service_identity='attachment-private-preview'
   AND j.safe_metadata=jsonb_build_object('attachmentId',NEW.attachment_id,'cardId',NEW.card_id,'version',NEW.source_version)
 ) THEN
  RAISE EXCEPTION 'Attachment preview manifest identity is invalid' USING ERRCODE='23514';
 END IF;
 IF TG_OP='UPDATE' AND NEW IS DISTINCT FROM OLD THEN
  RAISE EXCEPTION 'Attachment preview manifest is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER attachment_preview_manifest_guard BEFORE INSERT OR UPDATE ON attachment_previews
 FOR EACH ROW EXECUTE FUNCTION enforce_attachment_preview_manifest();
CREATE FUNCTION enforce_attachment_preview_job() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='UPDATE' AND (OLD.job_type='ATTACHMENT_PREVIEW' OR NEW.job_type='ATTACHMENT_PREVIEW') AND
  ROW(NEW.id,NEW.tenant_id,NEW.job_type,NEW.idempotency_key,NEW.actor_id,NEW.service_identity,NEW.correlation_id,NEW.safe_metadata,NEW.max_attempts,NEW.created_at)
  IS DISTINCT FROM ROW(OLD.id,OLD.tenant_id,OLD.job_type,OLD.idempotency_key,OLD.actor_id,OLD.service_identity,OLD.correlation_id,OLD.safe_metadata,OLD.max_attempts,OLD.created_at) THEN
  RAISE EXCEPTION 'Attachment preview job identity is immutable' USING ERRCODE='23514';
 END IF;
 IF NEW.job_type='ATTACHMENT_PREVIEW' THEN
  IF jsonb_typeof(NEW.safe_metadata) IS DISTINCT FROM 'object' OR octet_length(NEW.safe_metadata::text)>256 THEN
   RAISE EXCEPTION 'Attachment preview job references are invalid' USING ERRCODE='23514';
  END IF;
  IF NEW.id='00000000-0000-0000-0000-000000000000' OR NEW.actor_id='00000000-0000-0000-0000-000000000000'
   OR NEW.service_identity<>'attachment-private-preview' OR NEW.correlation_id !~ '^[A-Za-z0-9._-]{1,64}$'
   OR (SELECT count(*) FROM jsonb_object_keys(NEW.safe_metadata))<>3
   OR jsonb_typeof(NEW.safe_metadata->'attachmentId') IS DISTINCT FROM 'string'
   OR jsonb_typeof(NEW.safe_metadata->'cardId') IS DISTINCT FROM 'string'
   OR COALESCE(NEW.safe_metadata->>'attachmentId','') !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
   OR COALESCE(NEW.safe_metadata->>'cardId','') !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
   OR NEW.safe_metadata->>'attachmentId'='00000000-0000-0000-0000-000000000000'
   OR NEW.safe_metadata->>'cardId'='00000000-0000-0000-0000-000000000000'
   OR jsonb_typeof(NEW.safe_metadata->'version') IS DISTINCT FROM 'number'
   OR COALESCE(NEW.safe_metadata->>'version','') !~ '^[1-9][0-9]{0,18}$' THEN
   RAISE EXCEPTION 'Attachment preview job references are invalid' USING ERRCODE='23514';
  END IF;
  IF (NEW.safe_metadata->>'version')::numeric<2 OR (NEW.safe_metadata->>'version')::numeric>=9223372036854775807
   OR NEW.idempotency_key<>'attachment-preview/'||replace(NEW.safe_metadata->>'attachmentId','-','')||'/'||(NEW.safe_metadata->>'version') THEN
   RAISE EXCEPTION 'Attachment preview job retry identity is invalid' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER attachment_preview_job_guard BEFORE INSERT OR UPDATE ON background_jobs
 FOR EACH ROW EXECUTE FUNCTION enforce_attachment_preview_job();

CREATE FUNCTION attachment_preview_claim_is_live(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint) RETURNS boolean
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT p_tenant IS NOT NULL AND p_tenant=NULLIF(current_setting('app.tenant_id',true),'')::uuid
  AND p_version BETWEEN 2 AND 9223372036854775806
  AND p_job<>'00000000-0000-0000-0000-000000000000' AND p_actor<>'00000000-0000-0000-0000-000000000000'
  AND p_tenant<>'00000000-0000-0000-0000-000000000000' AND p_worker<>'00000000-0000-0000-0000-000000000000'
  AND p_lease<>'00000000-0000-0000-0000-000000000000' AND p_attachment<>'00000000-0000-0000-0000-000000000000'
  AND p_card<>'00000000-0000-0000-0000-000000000000' AND EXISTS(SELECT 1 FROM public.background_jobs j WHERE j.id=p_job AND j.tenant_id=p_tenant
   AND j.actor_id=p_actor AND j.worker_id=p_worker AND j.lease_id=p_lease AND j.state='RUNNING'
   AND j.lease_expires_at>clock_timestamp() AND j.attempt_count>0
   AND j.job_type='ATTACHMENT_PREVIEW' AND j.service_identity='attachment-private-preview'
   AND j.safe_metadata=jsonb_build_object('attachmentId',p_attachment,'cardId',p_card,'version',p_version)
   AND j.idempotency_key='attachment-preview/'||replace(p_attachment::text,'-','')||'/'||p_version::text);
$$;
REVOKE ALL ON FUNCTION attachment_preview_claim_is_live(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM PUBLIC;

CREATE FUNCTION load_attachment_preview(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
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
  AND f.kind='FILE' AND f.deleted_at IS NULL AND f.scan_status='CLEAN' AND f.version=p_version
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
REVOKE ALL ON FUNCTION load_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM PUBLIC;

-- Record exact sanitized output before provider I/O. Repeated declaration must
-- match the durable bytes; an unknown write can then be reconciled, not replaced.
CREATE FUNCTION declare_attachment_preview(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint,p_source_size bigint,p_source_sha256 text,p_source_mime text,
 p_output_size bigint,p_output_sha256 text,p_width integer,p_height integer) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE hint_board uuid; current_board uuid; current_card public.cards%ROWTYPE; loaded record; manifest public.attachment_previews%ROWTYPE;
BEGIN
 IF p_source_size IS NULL OR p_source_sha256 IS NULL OR p_source_mime IS NULL
  OR p_output_size IS NULL OR p_output_size NOT BETWEEN 45 AND 8388608
  OR p_output_sha256 IS NULL OR p_output_sha256 !~ '^[0-9a-f]{64}$'
  OR p_width IS NULL OR p_width NOT BETWEEN 1 AND 1024 OR p_height IS NULL OR p_height NOT BETWEEN 1 AND 1024
  OR p_job=p_attachment
  OR public.attachment_preview_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN RETURN 'LEASE_LOST'; END IF;
 SELECT c.board_id INTO hint_board FROM public.cards c WHERE c.id=p_card AND c.tenant_id=p_tenant;
 IF hint_board IS NULL THEN RETURN 'SUPERSEDED'; END IF;
 PERFORM 1 FROM public.organizations WHERE id=p_tenant FOR SHARE;
 SELECT id INTO current_board FROM public.boards WHERE id=hint_board AND tenant_id=p_tenant FOR UPDATE;
 SELECT * INTO current_card FROM public.cards WHERE id=p_card AND tenant_id=p_tenant FOR UPDATE;
 IF current_board IS NULL OR current_card.board_id IS DISTINCT FROM hint_board THEN RETURN 'LEASE_LOST'; END IF;
 PERFORM 1 FROM public.background_jobs WHERE id=p_job AND tenant_id=p_tenant FOR UPDATE;
 PERFORM 1 FROM public.attachments WHERE id=p_attachment AND tenant_id=p_tenant AND card_id=p_card FOR UPDATE;
 SELECT * INTO loaded FROM public.load_attachment_preview(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version);
 IF loaded.disposition IS DISTINCT FROM 'READY' THEN RETURN loaded.disposition; END IF;
 IF loaded.source_size_bytes IS DISTINCT FROM p_source_size OR loaded.source_sha256 IS DISTINCT FROM p_source_sha256
  OR loaded.source_mime_type IS DISTINCT FROM p_source_mime THEN RETURN 'LEASE_LOST'; END IF;
 INSERT INTO public.attachment_previews(id,tenant_id,attachment_id,card_id,source_version,source_size_bytes,source_sha256,source_mime_type,
  output_size_bytes,output_sha256,width,height,created_at)
 VALUES(p_job,p_tenant,p_attachment,p_card,p_version,p_source_size,p_source_sha256,p_source_mime,p_output_size,p_output_sha256,p_width,p_height,clock_timestamp())
 ON CONFLICT DO NOTHING;
 SELECT * INTO manifest FROM public.attachment_previews WHERE id=p_job AND tenant_id=p_tenant;
 IF manifest.id IS NULL OR manifest.attachment_id IS DISTINCT FROM p_attachment OR manifest.card_id IS DISTINCT FROM p_card
  OR manifest.source_version IS DISTINCT FROM p_version OR manifest.source_size_bytes IS DISTINCT FROM p_source_size
  OR manifest.source_sha256 IS DISTINCT FROM p_source_sha256 OR manifest.source_mime_type IS DISTINCT FROM p_source_mime
  OR manifest.output_size_bytes IS DISTINCT FROM p_output_size OR manifest.output_sha256 IS DISTINCT FROM p_output_sha256
  OR manifest.width IS DISTINCT FROM p_width OR manifest.height IS DISTINCT FROM p_height THEN RETURN 'CONFLICT'; END IF;
 IF public.attachment_preview_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RAISE EXCEPTION 'Attachment preview lease fence failed' USING ERRCODE='23514';
 END IF;
 RETURN 'DECLARED';
END $$;
REVOKE ALL ON FUNCTION declare_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,bigint,text,integer,integer) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('045_attachment_preview_intents');
COMMIT;