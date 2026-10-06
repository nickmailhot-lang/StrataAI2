BEGIN;
LOCK TABLE audit_events IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE organization_metadata_events IN SHARE ROW EXCLUSIVE MODE;
ALTER TABLE invitations ADD COLUMN version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 ADD COLUMN updated_at timestamptz;
UPDATE invitations SET updated_at=GREATEST(created_at,accepted_at,revoked_at);
ALTER TABLE invitations ALTER COLUMN updated_at SET NOT NULL,
 ADD CONSTRAINT invitation_revision_time CHECK(isfinite(updated_at) AND updated_at>=created_at),
 ADD CONSTRAINT invitation_subject_scope UNIQUE(id,tenant_id);
CREATE FUNCTION advance_invitation_revision() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  IF NEW.version<>1 THEN RAISE EXCEPTION 'Invitation creation revision is invalid' USING ERRCODE='23514'; END IF;
  NEW.updated_at:=NEW.created_at;
 ELSIF (to_jsonb(NEW)-'version'-'updated_at') IS DISTINCT FROM (to_jsonb(OLD)-'version'-'updated_at') THEN
  NEW.version:=OLD.version+1; NEW.updated_at:=GREATEST(clock_timestamp(),OLD.updated_at);
 ELSE
  NEW.version:=OLD.version; NEW.updated_at:=OLD.updated_at;
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION advance_invitation_revision() FROM PUBLIC;
CREATE TRIGGER invitation_revision BEFORE INSERT OR UPDATE ON invitations
 FOR EACH ROW EXECUTE FUNCTION advance_invitation_revision();

-- Only new persisted invitations have creation proofs. Legacy rows receive a
-- storage revision baseline, never reconstructed invitation events or proofs.
CREATE TABLE organization_invitation_creations (
 invitation_id uuid NOT NULL,tenant_id uuid NOT NULL,entity_version bigint NOT NULL CHECK(entity_version=1),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 PRIMARY KEY(invitation_id,tenant_id,entity_version),
 FOREIGN KEY(invitation_id,tenant_id) REFERENCES invitations(id,tenant_id) ON DELETE CASCADE
);
ALTER TABLE organization_invitation_creations ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_invitation_creations FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_invitation_creation_tenant ON organization_invitation_creations
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION capture_organization_invitation_creation() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF NEW.target_surface='INTERNAL' AND NEW.target_board_id IS NULL THEN
  INSERT INTO public.organization_invitation_creations(invitation_id,tenant_id,entity_version,created_at)
   VALUES(NEW.id,NEW.tenant_id,NEW.version,NEW.created_at);
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION capture_organization_invitation_creation() FROM PUBLIC;
CREATE TRIGGER organization_invitation_creation AFTER INSERT ON invitations
 FOR EACH ROW EXECUTE FUNCTION capture_organization_invitation_creation();
ALTER TABLE organization_metadata_events DROP CONSTRAINT organization_metadata_event_subject,
 ADD CONSTRAINT organization_metadata_event_subject CHECK(
  (event_type='ORGANIZATION_CREATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version=1)
  OR (event_type='ORGANIZATION_UPDATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version>1)
  OR (event_type='ORGANIZATION_MEMBER_ADDED' AND entity_type='OrganizationMembership')
  OR (event_type='ORGANIZATION_MEMBER_REMOVED' AND entity_type='OrganizationMembership' AND entity_version>1)
  OR (event_type='ORGANIZATION_MEMBER_INVITED' AND entity_type='Invitation' AND entity_version=1)),
 ADD COLUMN invitation_subject_id uuid GENERATED ALWAYS AS
  (CASE WHEN event_type='ORGANIZATION_MEMBER_INVITED' THEN entity_id END) STORED,
 ADD CONSTRAINT organization_metadata_invitation_creation FOREIGN KEY(invitation_subject_id,tenant_id,entity_version)
  REFERENCES organization_invitation_creations(invitation_id,tenant_id,entity_version) ON DELETE RESTRICT;
CREATE FUNCTION journal_organization_member_invitation() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE subject public.invitations%ROWTYPE; issuer public.organization_members%ROWTYPE; next_sequence bigint;
BEGIN
 IF NEW.event_type<>'ORGANIZATION_MEMBER_INVITED' THEN RETURN NEW; END IF;
 IF NEW.tenant_id IS NULL OR NEW.actor_id IS NULL OR NEW.entity_type<>'Invitation' OR NEW.safe_metadata<>'{}'::jsonb THEN
  RAISE EXCEPTION 'Organization invitation source is invalid' USING ERRCODE='23514';
 END IF;
 SELECT * INTO subject FROM public.invitations WHERE tenant_id=NEW.tenant_id AND id=NEW.entity_id FOR SHARE;
 IF subject.id IS NULL THEN RAISE EXCEPTION 'Organization invitation subject is unavailable' USING ERRCODE='23514'; END IF;
 -- Portal and Board invitations keep their existing audit and their own surface.
 IF subject.target_surface<>'INTERNAL' OR subject.target_board_id IS NOT NULL THEN RETURN NEW; END IF;
 PERFORM id FROM public.organizations WHERE id=NEW.tenant_id AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Organization invitation parent is unavailable' USING ERRCODE='23514'; END IF;
 SELECT * INTO issuer FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id FOR SHARE;
 IF subject.created_by_user_id IS DISTINCT FROM NEW.actor_id OR subject.version<>1
  OR subject.accepted_at IS NOT NULL OR subject.revoked_at IS NOT NULL OR subject.expires_at<=clock_timestamp()
  OR issuer.id IS NULL OR issuer.status<>'ACTIVE' OR issuer.role NOT IN ('OWNER','ADMIN')
  OR subject.target_role='OWNER' AND issuer.role<>'OWNER'
  OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=NEW.actor_id AND status='ACTIVE')
  OR NOT EXISTS(SELECT 1 FROM public.organization_invitation_creations WHERE tenant_id=NEW.tenant_id
   AND invitation_id=subject.id AND entity_version=subject.version AND created_at=subject.created_at) THEN
  RAISE EXCEPTION 'Organization invitation creation proof is unavailable' USING ERRCODE='23514';
 END IF;
 INSERT INTO public.organization_metadata_event_streams(tenant_id,last_sequence) VALUES(NEW.tenant_id,1)
 ON CONFLICT(tenant_id) DO UPDATE SET last_sequence=organization_metadata_event_streams.last_sequence+1
 RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.organization_metadata_events(tenant_id,sequence,event_id,event_type,actor_id,entity_type,entity_id,
  entity_version,correlation_id,metadata,created_at)
 VALUES(NEW.tenant_id,next_sequence,NEW.id,NEW.event_type,NEW.actor_id,'Invitation',subject.id,
  subject.version,NEW.correlation_id,'{}'::jsonb,subject.created_at);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_organization_member_invitation() FROM PUBLIC;
CREATE TRIGGER organization_member_invitation_journal AFTER INSERT ON audit_events
 FOR EACH ROW EXECUTE FUNCTION journal_organization_member_invitation();

CREATE OR REPLACE FUNCTION deliver_organization_metadata_event(p_tenant uuid,p_job uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_event uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE source public.organization_metadata_events%ROWTYPE; claim public.background_jobs%ROWTYPE;
BEGIN
 IF p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN RETURN false; END IF;
 SELECT * INTO claim FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='ORGANIZATION_METADATA_EVENT_READY' AND service_identity='organization-metadata-delivery'
  AND idempotency_key='organization-metadata-event/'||replace(p_event::text,'-','')
  AND safe_metadata=jsonb_build_object('eventId',p_event)
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF claim.id IS NULL THEN RETURN false; END IF;
 SELECT * INTO source FROM public.organization_metadata_events WHERE tenant_id=p_tenant AND event_id=p_event FOR UPDATE;
 IF source.event_id IS NULL OR source.actor_id IS DISTINCT FROM p_actor OR source.correlation_id IS DISTINCT FROM claim.correlation_id
  OR NOT EXISTS(SELECT 1 FROM public.organizations WHERE id=p_tenant)
  OR NOT ((source.entity_type='Organization' AND EXISTS(SELECT 1 FROM public.organizations
     WHERE id=p_tenant AND version>=source.entity_version))
   OR (source.entity_type='OrganizationMembership' AND EXISTS(SELECT 1 FROM public.organization_members
     WHERE tenant_id=p_tenant AND id=source.entity_id AND version>=source.entity_version))
   OR (source.entity_type='Invitation' AND EXISTS(SELECT 1 FROM public.invitations
     WHERE tenant_id=p_tenant AND id=source.entity_id AND version>=source.entity_version))) THEN RETURN false; END IF;
 IF source.ready_at IS NULL THEN
  UPDATE public.organization_metadata_events SET ready_at=GREATEST(clock_timestamp(),created_at)
   WHERE tenant_id=p_tenant AND event_id=p_event;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp()) THEN
  RAISE EXCEPTION 'Organization metadata delivery lease expired' USING ERRCODE='23514';
 END IF;
 RETURN true;
END $$;
INSERT INTO schema_migrations(version) VALUES('099_organization_member_invitation_events');
COMMIT;
