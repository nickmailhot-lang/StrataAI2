BEGIN;
LOCK TABLE organization_metadata_events,work_events,invitation_recipient_authority_pages,invitation_recipient_authority_effects IN SHARE ROW EXCLUSIVE MODE;
-- References retain their owning canonical source. No synthesized events,
-- actors or legacy Work-event backfill are introduced.
CREATE TABLE invitation_recipient_authority_sources (
 tenant_id uuid NOT NULL,event_id uuid NOT NULL,metadata_event_id uuid,work_event_id uuid,
 PRIMARY KEY(tenant_id,event_id),
 FOREIGN KEY(tenant_id,metadata_event_id) REFERENCES organization_metadata_events(tenant_id,event_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,work_event_id) REFERENCES work_events(tenant_id,event_id) ON DELETE RESTRICT,
 CHECK((metadata_event_id=event_id AND work_event_id IS NULL)
    OR (work_event_id=event_id AND metadata_event_id IS NULL)),
 CHECK(num_nonnulls(metadata_event_id,work_event_id)=1)
);
ALTER TABLE invitation_recipient_authority_sources ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_authority_sources FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_authority_source_tenant ON invitation_recipient_authority_sources
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
-- Repoint existing checkpoints to their already-proven metadata references.
INSERT INTO invitation_recipient_authority_sources(tenant_id,event_id,metadata_event_id)
 SELECT tenant_id,source_event_id,source_event_id FROM invitation_recipient_authority_pages
 UNION SELECT tenant_id,source_event_id,source_event_id FROM invitation_recipient_authority_effects;
ALTER TABLE invitation_recipient_authority_pages
 DROP CONSTRAINT invitation_recipient_authority_p_tenant_id_source_event_id_fkey,
 ADD CONSTRAINT invitation_authority_page_source FOREIGN KEY(tenant_id,source_event_id)
 REFERENCES invitation_recipient_authority_sources(tenant_id,event_id) ON DELETE RESTRICT;
ALTER TABLE invitation_recipient_authority_effects
 DROP CONSTRAINT invitation_recipient_authority_e_tenant_id_source_event_id_fkey,
 ADD CONSTRAINT invitation_authority_effect_source FOREIGN KEY(tenant_id,source_event_id)
 REFERENCES invitation_recipient_authority_sources(tenant_id,event_id) ON DELETE RESTRICT;
CREATE TRIGGER invitation_authority_source_history BEFORE UPDATE OR DELETE ON invitation_recipient_authority_sources
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
-- This private view is consumed only inside the existing leased/discovery
-- capabilities. Neither runtime role receives SELECT or reference writes.
CREATE VIEW invitation_recipient_authority_source_rows AS
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN organization_metadata_events e
 ON e.tenant_id=s.tenant_id AND e.event_id=s.metadata_event_id
 WHERE e.event_type IN ('ORGANIZATION_UPDATED','ORGANIZATION_MEMBER_REMOVED')
 UNION ALL
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN work_events e
 ON e.tenant_id=s.tenant_id AND e.event_id=s.work_event_id
 WHERE e.entity_type='Board' AND e.entity_id=e.board_id
 AND e.event_type IN ('BOARD_UPDATED','BOARD_VISIBILITY_CHANGED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED','BOARD_MEMBER_UPDATED','BOARD_MEMBER_REMOVED');

CREATE OR REPLACE FUNCTION publish_invitation_recipient_authority() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE job uuid:=gen_random_uuid(); start_id uuid:='00000000-0000-0000-0000-000000000000';
BEGIN
 -- Broad tenant invalidation also covers accepted rows whose cached links/names
 -- must be withdrawn. Present grant admission remains protected discovery's job.
 IF TG_TABLE_NAME='organization_metadata_events' THEN
  IF NEW.event_type NOT IN ('ORGANIZATION_UPDATED','ORGANIZATION_MEMBER_REMOVED') THEN RETURN NEW; END IF;
  INSERT INTO public.invitation_recipient_authority_sources(tenant_id,event_id,metadata_event_id)
  VALUES(NEW.tenant_id,NEW.event_id,NEW.event_id);
 ELSE
  IF NEW.entity_type<>'Board' OR NEW.entity_id<>NEW.board_id OR NEW.event_type NOT IN
   ('BOARD_UPDATED','BOARD_VISIBILITY_CHANGED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED','BOARD_MEMBER_UPDATED','BOARD_MEMBER_REMOVED') THEN RETURN NEW; END IF;
  INSERT INTO public.invitation_recipient_authority_sources(tenant_id,event_id,work_event_id)
  VALUES(NEW.tenant_id,NEW.event_id,NEW.event_id);
 END IF;
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
 VALUES(job,NEW.tenant_id,'INVITATION_RECIPIENT_AUTHORITY_PAGE',
  'invitation-authority/'||replace(NEW.event_id::text,'-','')||'/'||replace(start_id::text,'-',''),
  NEW.actor_id,'invitation-recipient-authority',NEW.correlation_id,jsonb_build_object('eventId',NEW.event_id));
 INSERT INTO public.invitation_recipient_authority_pages(tenant_id,job_id,source_event_id,after_id,after_created_at)
 VALUES(NEW.tenant_id,job,NEW.event_id,start_id,'-infinity');
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION publish_invitation_recipient_authority() FROM PUBLIC;
CREATE TRIGGER invitation_recipient_board_authority_publication AFTER INSERT ON work_events
 FOR EACH ROW EXECUTE FUNCTION publish_invitation_recipient_authority();
CREATE OR REPLACE FUNCTION deliver_invitation_recipient_authority(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_event uuid,p_limit integer)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE claim public.background_jobs%ROWTYPE; source record;
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
 SELECT * INTO source FROM public.invitation_recipient_authority_source_rows WHERE tenant_id=p_tenant AND event_id=p_event;
 IF source.event_id IS NULL OR source.actor_id IS DISTINCT FROM p_actor OR source.correlation_id IS DISTINCT FROM claim.correlation_id
  OR NOT ((source.entity_type='Organization' AND EXISTS(SELECT 1 FROM public.organizations WHERE id=p_tenant AND version>=source.entity_version))
   OR (source.entity_type='OrganizationMembership' AND EXISTS(SELECT 1 FROM public.organization_members WHERE tenant_id=p_tenant AND id=source.entity_id AND version>=source.entity_version))
   OR (source.entity_type='Board' AND EXISTS(SELECT 1 FROM public.boards WHERE tenant_id=p_tenant AND id=source.entity_id AND version>=source.entity_version))) THEN RETURN false; END IF;
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

CREATE OR REPLACE FUNCTION discover_invitation_recipient_authority_scopes(p_after uuid,p_limit integer)
RETURNS TABLE(tenant_id uuid) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF p_limit IS NULL OR p_limit<1 OR p_limit>100 THEN
  RAISE EXCEPTION 'Authority scope page limit is invalid' USING ERRCODE='22023';
 END IF;
 RETURN QUERY SELECT p.tenant_id FROM public.invitation_recipient_authority_pages p
 JOIN public.invitation_recipient_authority_source_rows e ON e.tenant_id=p.tenant_id AND e.event_id=p.source_event_id
 JOIN public.background_jobs j ON j.tenant_id=p.tenant_id AND j.id=p.job_id
 WHERE (p_after IS NULL OR p.tenant_id>p_after)
  AND j.job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE' AND j.service_identity='invitation-recipient-authority'
  AND j.actor_id=e.actor_id AND j.correlation_id=e.correlation_id
  AND j.safe_metadata=jsonb_build_object('eventId',e.event_id)
  AND j.idempotency_key='invitation-authority/'||replace(e.event_id::text,'-','')||'/'||replace(p.after_id::text,'-','')
  AND ((j.state='PENDING' AND j.available_at<=clock_timestamp() AND j.attempt_count<j.max_attempts)
    OR (j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp()))
 GROUP BY p.tenant_id ORDER BY p.tenant_id LIMIT p_limit;
END $$;
REVOKE ALL ON FUNCTION discover_invitation_recipient_authority_scopes(uuid,integer) FROM PUBLIC;


INSERT INTO schema_migrations(version) VALUES('107_invitation_recipient_board_authority');
COMMIT;
