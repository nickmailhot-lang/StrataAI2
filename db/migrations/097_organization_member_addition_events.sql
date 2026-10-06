BEGIN;
LOCK TABLE audit_events IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE organization_metadata_events IN SHARE ROW EXCLUSIVE MODE;
ALTER TABLE organization_members ADD CONSTRAINT organization_members_event_subject_unique UNIQUE(id,tenant_id);
CREATE TABLE organization_membership_activations (
 membership_id uuid NOT NULL, tenant_id uuid NOT NULL, entity_version bigint NOT NULL CHECK(entity_version>0),
 activated_at timestamptz NOT NULL CHECK(isfinite(activated_at)),
 PRIMARY KEY(membership_id,tenant_id,entity_version),
 FOREIGN KEY(membership_id,tenant_id) REFERENCES organization_members(id,tenant_id) ON DELETE CASCADE
);
ALTER TABLE organization_membership_activations ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_membership_activations FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_membership_activation_tenant ON organization_membership_activations
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION capture_organization_membership_activation() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF NEW.status='ACTIVE' AND (TG_OP='INSERT' OR OLD.status<>'ACTIVE') THEN
  INSERT INTO public.organization_membership_activations(membership_id,tenant_id,entity_version,activated_at)
   VALUES(NEW.id,NEW.tenant_id,NEW.version,NEW.updated_at);
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION capture_organization_membership_activation() FROM PUBLIC;
CREATE TRIGGER organization_membership_activation AFTER INSERT OR UPDATE ON organization_members
 FOR EACH ROW EXECUTE FUNCTION capture_organization_membership_activation();
-- PostgreSQL names a CHECK that references multiple columns at table scope,
-- even when it was written inline with entity_id. Identify the two original
-- rules structurally and require both; do not guess their generated names.
DO $$
DECLARE rule record; removed integer := 0;
BEGIN
 FOR rule IN SELECT conname FROM pg_constraint
  WHERE conrelid='public.organization_metadata_events'::regclass AND contype='c'
   AND (position('entity_id = tenant_id' in pg_get_constraintdef(oid))>0
    OR position('ORGANIZATION_CREATED' in pg_get_constraintdef(oid))>0
      AND position('ORGANIZATION_UPDATED' in pg_get_constraintdef(oid))>0
      AND position('entity_version' in pg_get_constraintdef(oid))>0)
 LOOP
  EXECUTE format('ALTER TABLE public.organization_metadata_events DROP CONSTRAINT %I',rule.conname);
  removed := removed+1;
 END LOOP;
 IF removed<>2 THEN RAISE EXCEPTION 'Original Organization subject constraints are unavailable'; END IF;
END $$;
ALTER TABLE organization_metadata_events
 DROP CONSTRAINT organization_metadata_events_event_type_check,
 DROP CONSTRAINT organization_metadata_events_entity_type_check,
 DROP CONSTRAINT organization_metadata_events_tenant_id_entity_version_key,
 ADD CONSTRAINT organization_metadata_event_subject CHECK(
  (event_type='ORGANIZATION_CREATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version=1)
  OR (event_type='ORGANIZATION_UPDATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version>1)
  OR (event_type='ORGANIZATION_MEMBER_ADDED' AND entity_type='OrganizationMembership')),
 ADD CONSTRAINT organization_metadata_subject_revision UNIQUE(tenant_id,entity_type,entity_id,entity_version),
 ADD COLUMN membership_subject_id uuid GENERATED ALWAYS AS
  (CASE WHEN entity_type='OrganizationMembership' THEN entity_id END) STORED,
 ADD CONSTRAINT organization_metadata_membership_subject FOREIGN KEY(membership_subject_id,tenant_id)
  REFERENCES organization_members(id,tenant_id) ON DELETE RESTRICT,
 ADD CONSTRAINT organization_metadata_membership_activation FOREIGN KEY(membership_subject_id,tenant_id,entity_version)
  REFERENCES organization_membership_activations(membership_id,tenant_id,entity_version) ON DELETE RESTRICT;

-- Only future real acceptances are projected. Earlier member audits lack the
-- immutable version and timestamp; no historical facts are reconstructed.
CREATE FUNCTION journal_organization_member_addition() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE subject public.organization_members%ROWTYPE; next_sequence bigint;
BEGIN
 IF NEW.event_type<>'ORGANIZATION_MEMBER_ADDED' THEN RETURN NEW; END IF;
 IF NEW.tenant_id IS NULL OR NEW.actor_id IS NULL OR NEW.entity_type<>'OrganizationMembership'
  OR NEW.safe_metadata<>'{}'::jsonb THEN
  RAISE EXCEPTION 'Organization membership source is invalid' USING ERRCODE='23514';
 END IF;
 PERFORM id FROM public.organizations WHERE id=NEW.tenant_id AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Organization membership parent is unavailable' USING ERRCODE='23514'; END IF;
 SELECT * INTO subject FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND id=NEW.entity_id FOR SHARE;
 IF subject.id IS NULL OR subject.status<>'ACTIVE' OR subject.user_id IS DISTINCT FROM NEW.actor_id
  OR NOT EXISTS(SELECT 1 FROM public.organization_membership_activations
    WHERE tenant_id=subject.tenant_id AND membership_id=subject.id AND entity_version=subject.version AND activated_at=subject.updated_at)
  OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=NEW.actor_id AND status='ACTIVE' AND email_verified)
  OR NOT EXISTS(SELECT 1 FROM public.invitations i JOIN public.audit_events a
    ON a.tenant_id=i.tenant_id AND a.entity_type='Invitation' AND a.entity_id=i.id
    AND a.event_type='INVITATION_ACCEPTED' AND a.actor_id=NEW.actor_id AND a.correlation_id=NEW.correlation_id
    JOIN public.organization_members issuer ON issuer.tenant_id=i.tenant_id AND issuer.user_id=i.created_by_user_id
    JOIN public.users issuer_account ON issuer_account.id=issuer.user_id
    WHERE i.tenant_id=NEW.tenant_id AND i.accepted_by_user_id=NEW.actor_id AND i.accepted_at=subject.updated_at
    AND i.target_surface='INTERNAL' AND i.target_board_id IS NULL AND i.target_role=subject.role
    AND issuer.status='ACTIVE' AND issuer.role IN ('OWNER','ADMIN') AND issuer_account.status='ACTIVE'
    AND (i.target_role<>'OWNER' OR issuer.role='OWNER')) THEN
  RAISE EXCEPTION 'Organization membership acceptance proof is unavailable' USING ERRCODE='23514';
 END IF;
 INSERT INTO public.organization_metadata_event_streams(tenant_id,last_sequence) VALUES(NEW.tenant_id,1)
 ON CONFLICT(tenant_id) DO UPDATE SET last_sequence=organization_metadata_event_streams.last_sequence+1
 RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.organization_metadata_events(tenant_id,sequence,event_id,event_type,actor_id,entity_type,entity_id,
  entity_version,correlation_id,metadata,created_at)
 VALUES(NEW.tenant_id,next_sequence,NEW.id,NEW.event_type,NEW.actor_id,'OrganizationMembership',subject.id,
  subject.version,NEW.correlation_id,'{}'::jsonb,subject.updated_at);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_organization_member_addition() FROM PUBLIC;
CREATE TRIGGER organization_member_addition_journal AFTER INSERT ON audit_events
 FOR EACH ROW EXECUTE FUNCTION journal_organization_member_addition();

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
     WHERE tenant_id=p_tenant AND id=source.entity_id AND version>=source.entity_version))) THEN RETURN false; END IF;
 -- Historical acceptance remains deliverable after removal, without granting
 -- the former member access. Current reader admission is checked separately.
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
INSERT INTO schema_migrations(version) VALUES('097_organization_member_addition_events');
COMMIT;
