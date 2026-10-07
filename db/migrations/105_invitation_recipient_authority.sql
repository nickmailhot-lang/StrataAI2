BEGIN;
LOCK TABLE organization_metadata_events IN SHARE ROW EXCLUSIVE MODE;
ALTER TABLE organization_metadata_events ADD CONSTRAINT organization_metadata_event_tenant_identity UNIQUE(tenant_id,event_id);
ALTER TABLE background_jobs ADD CONSTRAINT background_job_tenant_identity UNIQUE(tenant_id,id);
-- Private checkpoints reference actual canonical sources. No legacy backfill.
CREATE TABLE invitation_recipient_authority_pages (
 tenant_id uuid NOT NULL,job_id uuid NOT NULL,source_event_id uuid NOT NULL,
 after_id uuid NOT NULL,after_created_at timestamptz NOT NULL,completed_at timestamptz,scanned_count integer,
 PRIMARY KEY(tenant_id,job_id),UNIQUE(tenant_id,source_event_id,after_id),
 FOREIGN KEY(tenant_id,job_id) REFERENCES background_jobs(tenant_id,id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,source_event_id) REFERENCES organization_metadata_events(tenant_id,event_id) ON DELETE RESTRICT,
 CHECK((after_id='00000000-0000-0000-0000-000000000000' AND after_created_at='-infinity'::timestamptz)
  OR (after_id<>'00000000-0000-0000-0000-000000000000' AND isfinite(after_created_at))),
 CHECK((completed_at IS NULL)=(scanned_count IS NULL)),
 CHECK(completed_at IS NULL OR isfinite(completed_at)),CHECK(scanned_count BETWEEN 0 AND 100)
);
CREATE INDEX ix_invitations_authority_seek ON invitations(tenant_id,created_at,id) INCLUDE(email_normalized);
CREATE TABLE invitation_recipient_authority_revisions (
 email_normalized text PRIMARY KEY CHECK(length(email_normalized) BETWEEN 3 AND 320 AND email_normalized=upper(email_normalized)),
 revision bigint NOT NULL CHECK(revision>0)
);
CREATE TABLE invitation_recipient_authority_effects (
 tenant_id uuid NOT NULL,source_event_id uuid NOT NULL,email_normalized text NOT NULL,
 PRIMARY KEY(tenant_id,source_event_id,email_normalized),
 FOREIGN KEY(tenant_id,source_event_id) REFERENCES organization_metadata_events(tenant_id,event_id) ON DELETE RESTRICT,
 FOREIGN KEY(email_normalized) REFERENCES invitation_recipient_authority_revisions(email_normalized) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED
);
ALTER TABLE invitation_recipient_authority_pages ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_authority_pages FORCE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_authority_effects ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_authority_effects FORCE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_authority_revisions ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_authority_revisions FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_authority_page_tenant ON invitation_recipient_authority_pages
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE POLICY invitation_authority_effect_tenant ON invitation_recipient_authority_effects
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE POLICY invitation_authority_revision_lookup ON invitation_recipient_authority_revisions FOR SELECT USING(
 current_setting('app.route_kind',true)='INVITATION_RECIPIENT' AND email_normalized=current_setting('app.route_key',true));
CREATE FUNCTION protect_invitation_authority_history() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_TABLE_NAME='invitation_recipient_authority_pages' AND TG_OP='UPDATE' THEN
  IF ROW(NEW.tenant_id,NEW.job_id,NEW.source_event_id,NEW.after_id,NEW.after_created_at)
    IS NOT DISTINCT FROM ROW(OLD.tenant_id,OLD.job_id,OLD.source_event_id,OLD.after_id,OLD.after_created_at)
   AND OLD.completed_at IS NULL AND NEW.completed_at IS NOT NULL THEN RETURN NEW; END IF;
 END IF;
 RAISE EXCEPTION 'Invitation authority history is immutable' USING ERRCODE='23514';
END $$;
REVOKE ALL ON FUNCTION protect_invitation_authority_history() FROM PUBLIC;
CREATE TRIGGER invitation_authority_page_history BEFORE UPDATE OR DELETE ON invitation_recipient_authority_pages
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
CREATE TRIGGER invitation_authority_effect_history BEFORE UPDATE OR DELETE ON invitation_recipient_authority_effects
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
CREATE FUNCTION publish_invitation_recipient_authority() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE job uuid:=gen_random_uuid(); start_id uuid:='00000000-0000-0000-0000-000000000000';
BEGIN
 -- Broad tenant invalidation also covers accepted rows whose cached links/names
 -- must be withdrawn. Present grant admission remains protected discovery's job.
 IF NEW.event_type NOT IN ('ORGANIZATION_UPDATED','ORGANIZATION_MEMBER_REMOVED') THEN RETURN NEW; END IF;
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
 VALUES(job,NEW.tenant_id,'INVITATION_RECIPIENT_AUTHORITY_PAGE',
  'invitation-authority/'||replace(NEW.event_id::text,'-','')||'/'||replace(start_id::text,'-',''),
  NEW.actor_id,'invitation-recipient-authority',NEW.correlation_id,jsonb_build_object('eventId',NEW.event_id));
 INSERT INTO public.invitation_recipient_authority_pages(tenant_id,job_id,source_event_id,after_id,after_created_at)
 VALUES(NEW.tenant_id,job,NEW.event_id,start_id,'-infinity');
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION publish_invitation_recipient_authority() FROM PUBLIC;
CREATE TRIGGER invitation_recipient_authority_publication AFTER INSERT ON organization_metadata_events
 FOR EACH ROW EXECUTE FUNCTION publish_invitation_recipient_authority();
CREATE FUNCTION deliver_invitation_recipient_authority(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_event uuid,p_limit integer)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE claim public.background_jobs%ROWTYPE; source public.organization_metadata_events%ROWTYPE;
 page public.invitation_recipient_authority_pages%ROWTYPE; recipient record;
 candidate_ids uuid[]; candidate_times timestamptz[]; recipient_emails text[]; last_id uuid; next_job uuid; count_rows integer;
BEGIN
 IF p_limit IS DISTINCT FROM 100 OR p_tenant IS NULL
  OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN RETURN false; END IF;
 SELECT * INTO claim FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE' AND service_identity='invitation-recipient-authority'
  AND safe_metadata=jsonb_build_object('eventId',p_event)
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF claim.id IS NULL THEN RETURN false; END IF;
 SELECT * INTO page FROM public.invitation_recipient_authority_pages WHERE tenant_id=p_tenant AND job_id=p_job AND source_event_id=p_event FOR UPDATE;
 IF page.job_id IS NULL OR claim.idempotency_key IS DISTINCT FROM
  'invitation-authority/'||replace(p_event::text,'-','')||'/'||replace(page.after_id::text,'-','') THEN RETURN false; END IF;
 SELECT * INTO source FROM public.organization_metadata_events WHERE tenant_id=p_tenant AND event_id=p_event;
 IF source.event_id IS NULL OR source.actor_id IS DISTINCT FROM p_actor OR source.correlation_id IS DISTINCT FROM claim.correlation_id
  OR source.event_type NOT IN ('ORGANIZATION_UPDATED','ORGANIZATION_MEMBER_REMOVED')
  OR NOT ((source.entity_type='Organization' AND EXISTS(SELECT 1 FROM public.organizations WHERE id=p_tenant AND version>=source.entity_version))
   OR (source.entity_type='OrganizationMembership' AND EXISTS(SELECT 1 FROM public.organization_members WHERE tenant_id=p_tenant AND id=source.entity_id AND version>=source.entity_version))) THEN RETURN false; END IF;
 IF page.completed_at IS NULL THEN
  -- Seek over a fixed source-time cutoff, never OFFSET or an HTTP fanout.
  -- Do not filter current status: a revoked/accepted recipient may still hold
  -- cached disclosure. Future creations have their own invitation source.
  SELECT array_agg(id ORDER BY created_at,id),array_agg(created_at ORDER BY created_at,id),
   array_agg(email_normalized ORDER BY created_at,id) INTO candidate_ids,candidate_times,recipient_emails
  FROM (SELECT id,created_at,email_normalized FROM public.invitations WHERE tenant_id=p_tenant
   AND (created_at,id)>(page.after_created_at,page.after_id) AND created_at<=source.created_at
   ORDER BY created_at,id LIMIT p_limit) bounded;
  count_rows:=COALESCE(cardinality(candidate_ids),0);
  -- Fixed email order avoids cross-source/global counter lock inversion.
  FOR recipient IN SELECT DISTINCT email FROM unnest(recipient_emails) AS emails(email) ORDER BY email LOOP
   INSERT INTO public.invitation_recipient_authority_effects(tenant_id,source_event_id,email_normalized)
   VALUES(p_tenant,p_event,recipient.email) ON CONFLICT DO NOTHING;
   IF FOUND THEN
    INSERT INTO public.invitation_recipient_authority_revisions(email_normalized,revision) VALUES(recipient.email,1)
    ON CONFLICT(email_normalized) DO UPDATE SET revision=invitation_recipient_authority_revisions.revision+1;
   END IF;
  END LOOP;
  IF count_rows=100 THEN
   last_id:=candidate_ids[count_rows]; next_job:=gen_random_uuid();
   INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
   VALUES(next_job,p_tenant,claim.job_type,'invitation-authority/'||replace(p_event::text,'-','')||'/'||replace(last_id::text,'-',''),
    source.actor_id,claim.service_identity,source.correlation_id,jsonb_build_object('eventId',p_event));
   INSERT INTO public.invitation_recipient_authority_pages(tenant_id,job_id,source_event_id,after_id,after_created_at)
   VALUES(p_tenant,next_job,p_event,last_id,candidate_times[count_rows]);
  END IF;
  UPDATE public.invitation_recipient_authority_pages SET completed_at=clock_timestamp(),scanned_count=count_rows
   WHERE tenant_id=p_tenant AND job_id=p_job;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp()) THEN
  RAISE EXCEPTION 'Invitation authority delivery lease expired' USING ERRCODE='23514';
 END IF;
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION deliver_invitation_recipient_authority(uuid,uuid,uuid,uuid,uuid,uuid,integer) FROM PUBLIC;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT(email_normalized,revision) ON invitation_recipient_authority_revisions TO strataai_api_runtime;
 END IF;
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN
  GRANT EXECUTE ON FUNCTION deliver_invitation_recipient_authority(uuid,uuid,uuid,uuid,uuid,uuid,integer) TO strataai_worker_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('105_invitation_recipient_authority');
COMMIT;
