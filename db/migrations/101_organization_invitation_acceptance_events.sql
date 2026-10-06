BEGIN;
LOCK TABLE audit_events IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE organization_metadata_events IN SHARE ROW EXCLUSIVE MODE;
-- Future actual acceptance only. Legacy accepted rows cannot identify a new
-- command and never acquire reconstructed acceptance proofs or source events.
CREATE TABLE organization_invitation_acceptances (
 invitation_id uuid NOT NULL,tenant_id uuid NOT NULL,entity_version bigint NOT NULL CHECK(entity_version>1),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 accepted_at timestamptz NOT NULL CHECK(isfinite(accepted_at)),
 updated_at timestamptz NOT NULL CHECK(isfinite(updated_at)),
 PRIMARY KEY(invitation_id,tenant_id,entity_version),
 FOREIGN KEY(invitation_id,tenant_id) REFERENCES invitations(id,tenant_id) ON DELETE CASCADE
);
ALTER TABLE organization_invitation_acceptances ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_invitation_acceptances FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_invitation_acceptance_tenant ON organization_invitation_acceptances
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION capture_organization_invitation_acceptance() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF OLD.target_surface='INTERNAL' AND NEW.target_surface='INTERNAL'
  AND OLD.target_board_id IS NULL AND NEW.target_board_id IS NULL
  AND OLD.accepted_at IS NULL AND NEW.accepted_at IS NOT NULL AND NEW.accepted_by_user_id IS NOT NULL
  AND OLD.revoked_at IS NULL AND NEW.revoked_at IS NULL AND NEW.version>OLD.version
  AND OLD.target_role=NEW.target_role AND OLD.created_by_user_id IS NOT DISTINCT FROM NEW.created_by_user_id
  AND OLD.email_normalized=NEW.email_normalized THEN
  INSERT INTO public.organization_invitation_acceptances(invitation_id,tenant_id,entity_version,actor_id,accepted_at,updated_at)
   VALUES(NEW.id,NEW.tenant_id,NEW.version,NEW.accepted_by_user_id,NEW.accepted_at,NEW.updated_at);
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION capture_organization_invitation_acceptance() FROM PUBLIC;
CREATE TRIGGER organization_invitation_acceptance AFTER UPDATE ON invitations
 FOR EACH ROW EXECUTE FUNCTION capture_organization_invitation_acceptance();
ALTER TABLE organization_metadata_events DROP CONSTRAINT organization_metadata_event_subject,
 ADD CONSTRAINT organization_metadata_event_subject CHECK(
  (event_type='ORGANIZATION_CREATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version=1)
  OR (event_type='ORGANIZATION_UPDATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version>1)
  OR (event_type='ORGANIZATION_MEMBER_ADDED' AND entity_type='OrganizationMembership')
  OR (event_type='ORGANIZATION_MEMBER_REMOVED' AND entity_type='OrganizationMembership' AND entity_version>1)
  OR (event_type='ORGANIZATION_MEMBER_INVITED' AND entity_type='Invitation' AND entity_version=1)
  OR (event_type IN ('INVITATION_REVOKED','INVITATION_ACCEPTED') AND entity_type='Invitation' AND entity_version>1)),
 ADD COLUMN accepted_invitation_subject_id uuid GENERATED ALWAYS AS
  (CASE WHEN event_type='INVITATION_ACCEPTED' THEN entity_id END) STORED,
 ADD CONSTRAINT organization_metadata_invitation_acceptance FOREIGN KEY(accepted_invitation_subject_id,tenant_id,entity_version)
  REFERENCES organization_invitation_acceptances(invitation_id,tenant_id,entity_version) ON DELETE RESTRICT;
CREATE FUNCTION journal_organization_invitation_acceptance() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE subject public.invitations%ROWTYPE; next_sequence bigint;
BEGIN
 IF NEW.event_type<>'INVITATION_ACCEPTED' THEN RETURN NEW; END IF;
 IF NEW.tenant_id IS NULL OR NEW.actor_id IS NULL OR NEW.entity_type<>'Invitation' OR NEW.safe_metadata<>'{}'::jsonb THEN
  RAISE EXCEPTION 'Invitation acceptance source is invalid' USING ERRCODE='23514';
 END IF;
 SELECT * INTO subject FROM public.invitations WHERE tenant_id=NEW.tenant_id AND id=NEW.entity_id FOR SHARE;
 IF subject.id IS NULL THEN RAISE EXCEPTION 'Invitation acceptance subject is unavailable' USING ERRCODE='23514'; END IF;
 -- Portal and Board acceptance keep their own authorization/event surfaces.
 IF subject.target_surface<>'INTERNAL' OR subject.target_board_id IS NOT NULL THEN RETURN NEW; END IF;
 PERFORM id FROM public.organizations WHERE id=NEW.tenant_id AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Invitation acceptance parent is unavailable' USING ERRCODE='23514'; END IF;
 -- The owning command checks issuer grant authority before membership changes.
 -- Rechecking the post-grant role would reject a valid Admin self-downgrade.
 IF subject.accepted_by_user_id IS DISTINCT FROM NEW.actor_id OR subject.accepted_at IS NULL
  OR subject.revoked_at IS NOT NULL OR subject.version<=1 OR subject.expires_at<=clock_timestamp()
  OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=subject.created_by_user_id AND status='ACTIVE')
  OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=NEW.actor_id AND status='ACTIVE' AND email_normalized=subject.email_normalized)
  OR NOT EXISTS(SELECT 1 FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id
   AND status='ACTIVE' AND role=subject.target_role)
  OR NOT EXISTS(SELECT 1 FROM public.organization_invitation_acceptances WHERE tenant_id=NEW.tenant_id
   AND invitation_id=subject.id AND entity_version=subject.version AND actor_id=NEW.actor_id
   AND accepted_at=subject.accepted_at AND updated_at=subject.updated_at) THEN
  RAISE EXCEPTION 'Invitation acceptance transition or authority is unavailable' USING ERRCODE='23514';
 END IF;
 INSERT INTO public.organization_metadata_event_streams(tenant_id,last_sequence) VALUES(NEW.tenant_id,1)
 ON CONFLICT(tenant_id) DO UPDATE SET last_sequence=organization_metadata_event_streams.last_sequence+1
 RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.organization_metadata_events(tenant_id,sequence,event_id,event_type,actor_id,entity_type,entity_id,
  entity_version,correlation_id,metadata,created_at)
 VALUES(NEW.tenant_id,next_sequence,NEW.id,NEW.event_type,NEW.actor_id,'Invitation',subject.id,
  subject.version,NEW.correlation_id,'{}'::jsonb,subject.updated_at);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_organization_invitation_acceptance() FROM PUBLIC;
CREATE TRIGGER organization_invitation_acceptance_journal AFTER INSERT ON audit_events
 FOR EACH ROW EXECUTE FUNCTION journal_organization_invitation_acceptance();
INSERT INTO schema_migrations(version) VALUES('101_organization_invitation_acceptance_events');
COMMIT;
