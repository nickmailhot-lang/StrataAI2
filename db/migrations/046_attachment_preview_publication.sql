BEGIN;
-- A declaration is immutable recovery data. This separate receipt establishes
-- publication only with the same transaction's audit/current-Board event.
ALTER TABLE attachment_previews ADD CONSTRAINT uq_attachment_previews_id_tenant UNIQUE(id,tenant_id);
CREATE TABLE attachment_preview_publications (
 id uuid PRIMARY KEY, tenant_id uuid NOT NULL,
 attachment_version bigint NOT NULL CHECK(attachment_version>2),
 card_version bigint NOT NULL CHECK(card_version>0),
 board_id uuid NOT NULL, published_at timestamptz NOT NULL,
 FOREIGN KEY(id,tenant_id) REFERENCES attachment_previews(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,id) REFERENCES work_events(tenant_id,event_id) ON DELETE RESTRICT,
 FOREIGN KEY(board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(id) REFERENCES audit_events(id) ON DELETE RESTRICT
);
ALTER TABLE attachment_preview_publications ENABLE ROW LEVEL SECURITY;
ALTER TABLE attachment_preview_publications FORCE ROW LEVEL SECURITY;
CREATE POLICY attachment_preview_publications_tenant_isolation ON attachment_preview_publications
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION enforce_attachment_preview_publication() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Attachment preview publications are immutable' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(
  SELECT 1 FROM public.attachment_previews m JOIN public.background_jobs j ON j.id=m.id AND j.tenant_id=m.tenant_id
   JOIN public.audit_events a ON a.id=m.id AND a.tenant_id=m.tenant_id
   JOIN public.work_events e ON e.event_id=m.id AND e.tenant_id=m.tenant_id
  WHERE m.id=NEW.id AND m.tenant_id=NEW.tenant_id AND NEW.attachment_version=m.source_version+1
   AND NEW.published_at>=m.created_at AND a.created_at=NEW.published_at AND e.created_at=NEW.published_at
   AND a.actor_id=j.actor_id AND a.event_type='ATTACHMENT_PREVIEW_PUBLISHED' AND a.entity_type='Attachment' AND a.entity_id=m.attachment_id
   AND e.actor_id=j.actor_id AND e.event_type='ATTACHMENT_PREVIEW_PUBLISHED' AND e.entity_type='Card' AND e.entity_id=m.card_id
   AND e.board_id=NEW.board_id AND e.entity_version=NEW.card_version AND e.ready_at=NEW.published_at
 ) THEN RAISE EXCEPTION 'Attachment preview publication evidence is invalid' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER attachment_preview_publication_guard BEFORE INSERT OR UPDATE OR DELETE ON attachment_preview_publications
 FOR EACH ROW EXECUTE FUNCTION enforce_attachment_preview_publication();

-- Preserve the existing canonical source loader as a private implementation.
-- A committed publication replay discloses no source/output measurements.
ALTER FUNCTION load_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) RENAME TO load_attachment_preview_source;
REVOKE ALL ON FUNCTION load_attachment_preview_source(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM PUBLIC;
DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN
  EXECUTE 'REVOKE ALL ON FUNCTION public.load_attachment_preview_source(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM strataai_worker_runtime';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  EXECUTE 'REVOKE ALL ON FUNCTION public.load_attachment_preview_source(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM strataai_api_runtime';
 END IF;
END $$;
CREATE FUNCTION load_attachment_preview(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint)
RETURNS TABLE(disposition text,source_size_bytes bigint,source_sha256 text,source_mime_type text,
 output_size_bytes bigint,output_sha256 text,width integer,height integer)
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF public.attachment_preview_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RETURN QUERY SELECT 'LEASE_LOST'::text,NULL::bigint,NULL::text,NULL::text,NULL::bigint,NULL::text,NULL::integer,NULL::integer; RETURN;
 END IF;
 IF EXISTS(SELECT 1 FROM public.attachment_preview_publications r JOIN public.attachment_previews m ON m.id=r.id AND m.tenant_id=r.tenant_id
  WHERE r.id=p_job AND r.tenant_id=p_tenant AND m.attachment_id=p_attachment AND m.card_id=p_card AND m.source_version=p_version) THEN
  IF public.attachment_preview_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
   RETURN QUERY SELECT 'LEASE_LOST'::text,NULL::bigint,NULL::text,NULL::text,NULL::bigint,NULL::text,NULL::integer,NULL::integer; RETURN;
  END IF;
  RETURN QUERY SELECT 'APPLIED'::text,NULL::bigint,NULL::text,NULL::text,NULL::bigint,NULL::text,NULL::integer,NULL::integer; RETURN;
 END IF;
 RETURN QUERY SELECT * FROM public.load_attachment_preview_source(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version);
END $$;
REVOKE ALL ON FUNCTION load_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM PUBLIC;

CREATE FUNCTION finish_attachment_preview(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_attachment uuid,p_card uuid,p_version bigint,p_output_size bigint,p_output_sha256 text,p_width integer,p_height integer) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE hint_board uuid; current_board uuid; c public.cards%ROWTYPE; a public.attachments%ROWTYPE;
 j public.background_jobs%ROWTYPE; m public.attachment_previews%ROWTYPE; loaded record; effect_time timestamptz; sequence_number bigint;
BEGIN
 IF p_output_size IS NULL OR p_output_sha256 IS NULL OR p_width IS NULL OR p_height IS NULL
  OR public.attachment_preview_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN RETURN 'LEASE_LOST'; END IF;
 SELECT card.board_id INTO hint_board FROM public.cards card WHERE card.id=p_card AND card.tenant_id=p_tenant;
 IF hint_board IS NULL THEN RETURN 'SUPERSEDED'; END IF;
 PERFORM 1 FROM public.organizations WHERE id=p_tenant FOR SHARE;
 SELECT id INTO current_board FROM public.boards WHERE tenant_id=p_tenant AND id=hint_board FOR UPDATE;
 SELECT * INTO c FROM public.cards WHERE tenant_id=p_tenant AND id=p_card FOR UPDATE;
 IF current_board IS NULL OR c.board_id IS DISTINCT FROM hint_board THEN RETURN 'LEASE_LOST'; END IF;
 PERFORM 1 FROM public.board_lists WHERE tenant_id=p_tenant AND id=c.list_id AND board_id=c.board_id FOR SHARE;
 SELECT * INTO j FROM public.background_jobs WHERE id=p_job AND tenant_id=p_tenant FOR UPDATE;
 SELECT * INTO a FROM public.attachments WHERE id=p_attachment AND tenant_id=p_tenant AND card_id=p_card FOR UPDATE;
 SELECT * INTO m FROM public.attachment_previews WHERE id=p_job AND tenant_id=p_tenant FOR UPDATE;
 SELECT * INTO loaded FROM public.load_attachment_preview(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version);
 IF loaded.disposition IN ('LEASE_LOST','SUPERSEDED','APPLIED') THEN RETURN loaded.disposition; END IF;
 IF loaded.disposition IS DISTINCT FROM 'READY' OR m.id IS NULL OR m.attachment_id IS DISTINCT FROM p_attachment
  OR m.card_id IS DISTINCT FROM p_card OR m.source_version IS DISTINCT FROM p_version
  OR m.output_size_bytes IS DISTINCT FROM p_output_size OR m.output_sha256 IS DISTINCT FROM p_output_sha256
  OR m.width IS DISTINCT FROM p_width OR m.height IS DISTINCT FROM p_height
  OR loaded.output_size_bytes IS DISTINCT FROM p_output_size OR loaded.output_sha256 IS DISTINCT FROM p_output_sha256
  OR loaded.width IS DISTINCT FROM p_width OR loaded.height IS DISTINCT FROM p_height THEN RETURN 'LEASE_LOST'; END IF;
 effect_time=GREATEST(clock_timestamp(),a.updated_at,c.updated_at,m.created_at);
 UPDATE public.attachments SET updated_at=effect_time,version=version+1
  WHERE id=p_attachment AND tenant_id=p_tenant AND card_id=p_card AND version=p_version AND scan_status='CLEAN' AND deleted_at IS NULL;
 IF NOT FOUND THEN RETURN 'SUPERSEDED'; END IF;
 UPDATE public.cards SET updated_at=effect_time,version=version+1 WHERE id=p_card AND tenant_id=p_tenant RETURNING * INTO c;
 INSERT INTO public.work_event_streams(tenant_id,board_id) VALUES(p_tenant,c.board_id) ON CONFLICT DO NOTHING;
 UPDATE public.work_event_streams SET last_sequence=last_sequence+1,updated_at=effect_time
  WHERE tenant_id=p_tenant AND board_id=c.board_id RETURNING last_sequence INTO sequence_number;
 INSERT INTO public.work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at,ready_at)
  VALUES(p_tenant,p_job,c.board_id,sequence_number,p_actor,'ATTACHMENT_PREVIEW_PUBLISHED','Card',p_card,c.version,j.correlation_id,effect_time,effect_time);
 INSERT INTO public.audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
  VALUES(p_job,p_tenant,p_actor,'ATTACHMENT_PREVIEW_PUBLISHED','Attachment',p_attachment,j.correlation_id,effect_time);
 INSERT INTO public.attachment_preview_publications(id,tenant_id,attachment_version,card_version,board_id,published_at)
  VALUES(p_job,p_tenant,p_version+1,c.version,c.board_id,effect_time);
 IF public.attachment_preview_claim_is_live(p_job,p_tenant,p_actor,p_worker,p_lease,p_attachment,p_card,p_version) IS NOT TRUE THEN
  RAISE EXCEPTION 'Attachment preview publication lease fence failed' USING ERRCODE='23514';
 END IF;
 RETURN 'APPLIED';
END $$;
REVOKE ALL ON FUNCTION finish_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,integer,integer) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('046_attachment_preview_publication');
COMMIT;
