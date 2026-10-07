BEGIN;
LOCK TABLE users,identity_events,invitation_recipient_authority_sources IN SHARE ROW EXCLUSIVE MODE;
-- Only future canonical account transitions. Never infer historical actors/events.
CREATE TABLE invitation_issuer_authority_proofs (
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 entity_version bigint NOT NULL CHECK(entity_version>1),changed_at timestamptz NOT NULL CHECK(isfinite(changed_at)),
 owning_transaction bigint NOT NULL CHECK(owning_transaction>0),PRIMARY KEY(actor_id,entity_version)
);
CREATE TABLE invitation_issuer_authority_sources (
 event_id uuid PRIMARY KEY REFERENCES identity_events(event_id) ON DELETE RESTRICT,
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,event_type text NOT NULL CHECK(event_type='USER_DEACTIVATED'),
 entity_type text NOT NULL CHECK(entity_type='User'),entity_id uuid NOT NULL CHECK(entity_id=actor_id),
 entity_version bigint NOT NULL,correlation_id text NOT NULL CHECK(length(btrim(correlation_id)) BETWEEN 1 AND 120),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),UNIQUE(event_id,actor_id),UNIQUE(actor_id,entity_version),
 FOREIGN KEY(actor_id,entity_version) REFERENCES invitation_issuer_authority_proofs(actor_id,entity_version) ON DELETE RESTRICT
);
CREATE TABLE invitation_issuer_authority_jobs (
 id uuid PRIMARY KEY,event_id uuid NOT NULL,actor_id uuid NOT NULL,after_tenant uuid NOT NULL,
 state text NOT NULL DEFAULT 'PENDING' CHECK(state IN ('PENDING','RUNNING','SUCCEEDED')),
 attempt_count integer NOT NULL DEFAULT 0 CHECK(attempt_count BETWEEN 0 AND 5),
 worker_id uuid,lease_id uuid,lease_expires_at timestamptz,
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),completed_at timestamptz,scanned_count integer,
 UNIQUE(event_id,after_tenant),FOREIGN KEY(event_id,actor_id) REFERENCES invitation_issuer_authority_sources(event_id,actor_id) ON DELETE RESTRICT,
 CHECK((state='RUNNING')=(worker_id IS NOT NULL AND lease_id IS NOT NULL AND lease_expires_at IS NOT NULL)),
 CHECK(state='RUNNING' OR (worker_id IS NULL AND lease_id IS NULL AND lease_expires_at IS NULL)),
 CHECK((state='SUCCEEDED')=(completed_at IS NOT NULL AND scanned_count IS NOT NULL)),
 CHECK((completed_at IS NULL)=(scanned_count IS NULL)),
 CHECK(isfinite(created_at) AND (completed_at IS NULL OR isfinite(completed_at)) AND (lease_expires_at IS NULL OR isfinite(lease_expires_at))),
 CHECK(scanned_count BETWEEN 0 AND 100)
);
CREATE TABLE invitation_issuer_authority_effects (
 event_id uuid NOT NULL,actor_id uuid NOT NULL,email_normalized text NOT NULL,
 PRIMARY KEY(event_id,email_normalized),
 FOREIGN KEY(event_id,actor_id) REFERENCES invitation_issuer_authority_sources(event_id,actor_id) ON DELETE RESTRICT,
 FOREIGN KEY(email_normalized) REFERENCES invitation_recipient_authority_revisions(email_normalized) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED
);
DO $$ DECLARE relation text; BEGIN
 FOREACH relation IN ARRAY ARRAY['invitation_issuer_authority_proofs','invitation_issuer_authority_sources','invitation_issuer_authority_jobs','invitation_issuer_authority_effects'] LOOP
  EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY',relation);
  EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY',relation);
  EXECUTE format($policy$CREATE POLICY issuer_authority_subject ON public.%I USING(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid) WITH CHECK(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)$policy$,relation);
 END LOOP;
END $$;
CREATE TRIGGER issuer_authority_proof_history BEFORE UPDATE OR DELETE ON invitation_issuer_authority_proofs FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
CREATE TRIGGER issuer_authority_source_history BEFORE UPDATE OR DELETE ON invitation_issuer_authority_sources FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
CREATE TRIGGER issuer_authority_effect_history BEFORE UPDATE OR DELETE ON invitation_issuer_authority_effects FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
CREATE TRIGGER issuer_identity_source_history BEFORE UPDATE OR DELETE ON identity_events
 FOR EACH ROW WHEN (OLD.event_type='USER_DEACTIVATED') EXECUTE FUNCTION protect_invitation_authority_history();
CREATE INDEX ix_invitations_issuer_authority_seek ON invitations(created_by_user_id,tenant_id,created_at);
CREATE FUNCTION capture_invitation_issuer_authority_transition() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF NEW.status='DEACTIVATED' AND OLD.status<>'DEACTIVATED' AND NEW.version=OLD.version+1 AND isfinite(NEW.updated_at) THEN
  INSERT INTO public.invitation_issuer_authority_proofs(actor_id,entity_version,changed_at,owning_transaction)
  VALUES(NEW.id,NEW.version,NEW.updated_at,txid_current());
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER issuer_authority_capture AFTER UPDATE OF status ON users FOR EACH ROW EXECUTE FUNCTION capture_invitation_issuer_authority_transition();
CREATE FUNCTION journal_invitation_issuer_authority_source() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE proof public.invitation_issuer_authority_proofs%ROWTYPE; account public.users%ROWTYPE;
BEGIN
 IF NEW.event_type<>'USER_DEACTIVATED' THEN RETURN NEW; END IF;
 SELECT * INTO account FROM public.users WHERE id=NEW.user_id FOR SHARE;
 SELECT * INTO proof FROM public.invitation_issuer_authority_proofs WHERE actor_id=NEW.user_id AND entity_version=NEW.entity_version;
 IF account.status IS DISTINCT FROM 'DEACTIVATED' OR account.version IS DISTINCT FROM NEW.entity_version
  OR NEW.actor_id IS DISTINCT FROM NEW.user_id OR NEW.entity_id IS DISTINCT FROM NEW.user_id OR NEW.entity_type<>'User'
  OR NEW.metadata<>'{}'::jsonb OR proof.actor_id IS NULL OR proof.owning_transaction<>txid_current()
  OR proof.changed_at IS DISTINCT FROM account.updated_at OR NEW.created_at<proof.changed_at THEN
  RAISE EXCEPTION 'Invitation issuer account transition is unproven' USING ERRCODE='23514';
 END IF;
 INSERT INTO public.invitation_issuer_authority_sources(event_id,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES(NEW.event_id,NEW.actor_id,NEW.event_type,NEW.entity_type,NEW.entity_id,NEW.entity_version,NEW.correlation_id,NEW.created_at);
 INSERT INTO public.invitation_issuer_authority_jobs(id,event_id,actor_id,after_tenant)
 VALUES(gen_random_uuid(),NEW.event_id,NEW.actor_id,'00000000-0000-0000-0000-000000000000');
 RETURN NEW;
END $$;
CREATE TRIGGER issuer_authority_publication AFTER INSERT ON identity_events FOR EACH ROW EXECUTE FUNCTION journal_invitation_issuer_authority_source();
ALTER TABLE invitation_recipient_authority_sources ADD COLUMN issuer_source_event_id uuid REFERENCES invitation_issuer_authority_sources(event_id) ON DELETE RESTRICT,
 DROP CONSTRAINT invitation_authority_source_exclusive,DROP CONSTRAINT invitation_authority_source_identity,
 ADD CONSTRAINT invitation_authority_source_exclusive CHECK(num_nonnulls(metadata_event_id,work_event_id,organization_lifecycle_source_id,issuer_source_event_id)=1),
 ADD CONSTRAINT invitation_authority_source_identity CHECK(event_id=COALESCE(metadata_event_id,work_event_id,organization_lifecycle_source_id,issuer_source_event_id));
CREATE OR REPLACE VIEW invitation_recipient_authority_source_rows AS
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN organization_metadata_events e
 ON e.tenant_id=s.tenant_id AND e.event_id=s.metadata_event_id
 WHERE e.event_type IN ('ORGANIZATION_UPDATED','ORGANIZATION_MEMBER_REMOVED')
 UNION ALL
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN work_events e
 ON e.tenant_id=s.tenant_id AND e.event_id=s.work_event_id
 WHERE e.entity_type='Board' AND e.entity_id=e.board_id
 AND e.event_type IN ('BOARD_UPDATED','BOARD_VISIBILITY_CHANGED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED','BOARD_MEMBER_UPDATED','BOARD_MEMBER_REMOVED')
 UNION ALL
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN invitation_recipient_organization_lifecycle_sources e
 ON e.tenant_id=s.tenant_id AND e.event_id=s.organization_lifecycle_source_id
 UNION ALL
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN invitation_issuer_authority_sources e ON e.event_id=s.issuer_source_event_id;
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
   OR (source.entity_type='User' AND EXISTS(SELECT 1 FROM public.users WHERE id=source.entity_id AND version>=source.entity_version))
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
    -- One global account event invalidates a recipient only once, even when
    -- that issuer has invited the same address into multiple Organizations.
    IF source.entity_type='User' THEN
     INSERT INTO public.invitation_issuer_authority_effects(event_id,actor_id,email_normalized)
     VALUES(p_event,p_actor,recipient.email) ON CONFLICT DO NOTHING;
     IF NOT FOUND THEN CONTINUE; END IF;
    END IF;
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


CREATE FUNCTION claim_invitation_issuer_authority(p_worker uuid)
 RETURNS TABLE(job_id uuid,event_id uuid,actor_id uuid,worker_id uuid,lease_id uuid,correlation_id text)
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$ BEGIN
 IF p_worker IS NULL OR p_worker='00000000-0000-0000-0000-000000000000' THEN RETURN; END IF;
 RETURN QUERY WITH selected AS (
  SELECT j.id FROM public.invitation_issuer_authority_jobs j WHERE j.attempt_count<5
   AND (j.state='PENDING' OR j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp())
  ORDER BY j.created_at,j.id LIMIT 1 FOR UPDATE SKIP LOCKED
 ), claimed AS (
  UPDATE public.invitation_issuer_authority_jobs j SET state='RUNNING',attempt_count=j.attempt_count+1,
   worker_id=p_worker,lease_id=gen_random_uuid(),lease_expires_at=clock_timestamp()+interval '2 minutes'
  FROM selected x WHERE j.id=x.id RETURNING j.*
 ) SELECT c.id,c.event_id,c.actor_id,c.worker_id,c.lease_id,s.correlation_id FROM claimed c
 JOIN public.invitation_issuer_authority_sources s ON s.event_id=c.event_id AND s.actor_id=c.actor_id;
END $$;
CREATE FUNCTION deliver_invitation_issuer_authority(p_job uuid,p_event uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_limit integer)
 RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE claim public.invitation_issuer_authority_jobs%ROWTYPE; source public.invitation_issuer_authority_sources%ROWTYPE;
 tenants uuid[]; tenant uuid; next_job uuid; count_rows integer; start_id uuid:='00000000-0000-0000-0000-000000000000';
BEGIN
 IF p_limit IS DISTINCT FROM 100 THEN RETURN false; END IF;
 SELECT * INTO claim FROM public.invitation_issuer_authority_jobs WHERE id=p_job AND event_id=p_event AND actor_id=p_actor
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF claim.id IS NULL THEN RETURN false; END IF;
 SELECT * INTO source FROM public.invitation_issuer_authority_sources WHERE event_id=p_event AND actor_id=p_actor;
 IF source.event_id IS NULL THEN RETURN false; END IF;
 SELECT array_agg(tenant_id ORDER BY tenant_id) INTO tenants FROM (
  SELECT DISTINCT tenant_id FROM public.invitations WHERE created_by_user_id=p_actor AND created_at<=source.created_at
   AND tenant_id>claim.after_tenant ORDER BY tenant_id LIMIT p_limit
 ) bounded;
 count_rows:=COALESCE(cardinality(tenants),0);
 FOREACH tenant IN ARRAY COALESCE(tenants,ARRAY[]::uuid[]) LOOP
  INSERT INTO public.invitation_recipient_authority_sources(tenant_id,event_id,issuer_source_event_id) VALUES(tenant,p_event,p_event);
  next_job:=gen_random_uuid();
  INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
  VALUES(next_job,tenant,'INVITATION_RECIPIENT_AUTHORITY_PAGE','invitation-authority/'||replace(p_event::text,'-','')||'/'||replace(start_id::text,'-',''),
   p_actor,'invitation-recipient-authority',source.correlation_id,jsonb_build_object('eventId',p_event));
  INSERT INTO public.invitation_recipient_authority_pages(tenant_id,job_id,source_event_id,after_id,after_created_at)
  VALUES(tenant,next_job,p_event,start_id,'-infinity');
 END LOOP;
 IF count_rows=100 THEN
  INSERT INTO public.invitation_issuer_authority_jobs(id,event_id,actor_id,after_tenant)
  VALUES(gen_random_uuid(),p_event,p_actor,tenants[count_rows]);
 END IF;
 UPDATE public.invitation_issuer_authority_jobs SET state='SUCCEEDED',completed_at=clock_timestamp(),scanned_count=count_rows,
  worker_id=NULL,lease_id=NULL,lease_expires_at=NULL WHERE id=p_job AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease
  AND lease_expires_at>clock_timestamp();
 IF NOT FOUND THEN RAISE EXCEPTION 'Invitation issuer authority lease expired' USING ERRCODE='23514'; END IF;
 RETURN true;
END $$;
CREATE FUNCTION protect_invitation_issuer_job_identity() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$ BEGIN
 IF TG_OP='DELETE' OR NEW.id IS DISTINCT FROM OLD.id OR NEW.event_id IS DISTINCT FROM OLD.event_id
  OR NEW.actor_id IS DISTINCT FROM OLD.actor_id OR NEW.after_tenant IS DISTINCT FROM OLD.after_tenant
  OR NEW.created_at IS DISTINCT FROM OLD.created_at OR OLD.state='SUCCEEDED' THEN
  RAISE EXCEPTION 'Invitation issuer job history is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER issuer_job_identity BEFORE UPDATE OR DELETE ON invitation_issuer_authority_jobs FOR EACH ROW EXECUTE FUNCTION protect_invitation_issuer_job_identity();
REVOKE ALL ON invitation_issuer_authority_proofs,invitation_issuer_authority_sources,invitation_issuer_authority_jobs,invitation_issuer_authority_effects FROM PUBLIC;
REVOKE ALL ON FUNCTION capture_invitation_issuer_authority_transition(),journal_invitation_issuer_authority_source(),
 claim_invitation_issuer_authority(uuid),deliver_invitation_issuer_authority(uuid,uuid,uuid,uuid,uuid,integer),protect_invitation_issuer_job_identity() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('109_invitation_issuer_account_authority');
COMMIT;
