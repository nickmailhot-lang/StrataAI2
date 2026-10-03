BEGIN;
-- Worker can update queue leases, not convert another job into a scan or
-- redirect a published scan capability. Other job types retain their contracts.
CREATE FUNCTION enforce_attachment_scan_job() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='UPDATE' AND (OLD.job_type='ATTACHMENT_SCAN' OR NEW.job_type='ATTACHMENT_SCAN') AND
  ROW(NEW.id,NEW.tenant_id,NEW.job_type,NEW.idempotency_key,NEW.actor_id,NEW.service_identity,NEW.correlation_id,NEW.safe_metadata,NEW.max_attempts,NEW.created_at)
  IS DISTINCT FROM ROW(OLD.id,OLD.tenant_id,OLD.job_type,OLD.idempotency_key,OLD.actor_id,OLD.service_identity,OLD.correlation_id,OLD.safe_metadata,OLD.max_attempts,OLD.created_at) THEN
  RAISE EXCEPTION 'Attachment scan job identity is immutable' USING ERRCODE='23514';
 END IF;
 IF NEW.job_type='ATTACHMENT_SCAN' THEN
  IF jsonb_typeof(NEW.safe_metadata) IS DISTINCT FROM 'object' OR octet_length(NEW.safe_metadata::text)>256 THEN
   RAISE EXCEPTION 'Attachment scan job references are invalid' USING ERRCODE='23514';
  END IF;
  IF NEW.id='00000000-0000-0000-0000-000000000000' OR NEW.actor_id='00000000-0000-0000-0000-000000000000'
   OR NEW.service_identity<>'attachment-quarantine-scan' OR NEW.correlation_id !~ '^[A-Za-z0-9._-]{1,64}$'
   OR (SELECT count(*) FROM jsonb_object_keys(NEW.safe_metadata))<>3
   OR jsonb_typeof(NEW.safe_metadata->'attachmentId') IS DISTINCT FROM 'string'
   OR jsonb_typeof(NEW.safe_metadata->'cardId') IS DISTINCT FROM 'string'
   OR COALESCE(NEW.safe_metadata->>'attachmentId','') !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
   OR COALESCE(NEW.safe_metadata->>'cardId','') !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
   OR NEW.safe_metadata->>'attachmentId'='00000000-0000-0000-0000-000000000000'
   OR NEW.safe_metadata->>'cardId'='00000000-0000-0000-0000-000000000000'
   OR jsonb_typeof(NEW.safe_metadata->'version') IS DISTINCT FROM 'number'
   OR COALESCE(NEW.safe_metadata->>'version','') !~ '^[1-9][0-9]{0,18}$' THEN
   RAISE EXCEPTION 'Attachment scan job references are invalid' USING ERRCODE='23514';
  END IF;
  IF (NEW.safe_metadata->>'version')::numeric>=9223372036854775807
   OR NEW.idempotency_key<>'attachment-scan/'||replace(NEW.safe_metadata->>'attachmentId','-','')||'/'||(NEW.safe_metadata->>'version') THEN
   RAISE EXCEPTION 'Attachment scan job retry identity is invalid' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER attachment_scan_job_guard BEFORE INSERT OR UPDATE ON background_jobs
 FOR EACH ROW EXECUTE FUNCTION enforce_attachment_scan_job();

CREATE FUNCTION attachment_scan_claim_is_live(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint) RETURNS boolean
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT p_tenant IS NOT NULL AND p_tenant=NULLIF(current_setting('app.tenant_id',true),'')::uuid
  AND p_version BETWEEN 1 AND 9223372036854775806
  AND p_job<>'00000000-0000-0000-0000-000000000000' AND p_actor<>'00000000-0000-0000-0000-000000000000'
  AND p_tenant<>'00000000-0000-0000-0000-000000000000' AND p_worker<>'00000000-0000-0000-0000-000000000000'
  AND p_lease<>'00000000-0000-0000-0000-000000000000' AND p_attachment<>'00000000-0000-0000-0000-000000000000'
  AND p_card<>'00000000-0000-0000-0000-000000000000' AND EXISTS(SELECT 1 FROM public.background_jobs j WHERE j.id=p_job AND j.tenant_id=p_tenant
   AND j.actor_id=p_actor AND j.worker_id=p_worker AND j.lease_id=p_lease AND j.state='RUNNING'
   AND j.lease_expires_at>clock_timestamp() AND j.attempt_count>0
   AND j.job_type='ATTACHMENT_SCAN' AND j.service_identity='attachment-quarantine-scan'
   AND j.safe_metadata=jsonb_build_object('attachmentId',p_attachment,'cardId',p_card,'version',p_version)
   AND j.idempotency_key='attachment-scan/'||replace(p_attachment::text,'-','')||'/'||p_version::text);
$$;
REVOKE ALL ON FUNCTION attachment_scan_claim_is_live(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM PUBLIC;

CREATE FUNCTION load_attachment_scan(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
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
  WHERE f.id=p_attachment AND f.tenant_id=p_tenant AND f.card_id=p_card AND f.uploader_id=p_actor
   AND u.uploader_id=p_actor AND u.status='PUBLISHED' AND f.kind='FILE' AND f.deleted_at IS NULL
   AND f.sha256=u.expected_sha256 AND f.size_bytes=u.expected_size_bytes AND f.mime_type=u.verified_mime_type
   AND f.storage_key='attachments/'||replace(p_tenant::text,'-','')||'/'||replace(p_attachment::text,'-','');
 -- Recheck after reads; expiry/replacement never returns private integrity.
 IF public.attachment_scan_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RETURN QUERY SELECT 'LEASE_LOST'::text,NULL::bigint,NULL::text; RETURN;
 END IF;
 IF a.id IS NULL THEN RETURN QUERY SELECT 'SUPERSEDED'::text,NULL::bigint,NULL::text; RETURN; END IF;
 IF a.version=p_version+1 AND a.scan_status IN ('CLEAN','REJECTED','FAILED') AND EXISTS(
  SELECT 1 FROM public.audit_events e JOIN public.work_events w ON w.event_id=e.id AND w.tenant_id=e.tenant_id
  WHERE e.id=p_job AND e.tenant_id=p_tenant AND e.actor_id=p_actor AND e.event_type='ATTACHMENT_SCAN_COMPLETED'
   AND e.entity_type='Attachment' AND e.entity_id=p_attachment AND w.actor_id=p_actor
   AND w.event_type='ATTACHMENT_SCAN_COMPLETED' AND w.entity_type='Card' AND w.entity_id=p_card) THEN
  RETURN QUERY SELECT 'APPLIED'::text,NULL::bigint,NULL::text; RETURN;
 END IF;
 IF a.version<>p_version OR a.scan_status<>'PENDING' THEN
  RETURN QUERY SELECT 'SUPERSEDED'::text,NULL::bigint,NULL::text; RETURN;
 END IF;
 RETURN QUERY SELECT 'READY'::text,a.size_bytes,a.sha256;
END $$;
REVOKE ALL ON FUNCTION load_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM PUBLIC;

CREATE FUNCTION finish_attachment_scan(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
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
  WHERE id=p_attachment AND tenant_id=p_tenant AND card_id=p_card AND version=p_version AND scan_status='PENDING';
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
REVOKE ALL ON FUNCTION finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('044_attachment_scan_delivery');
COMMIT;
