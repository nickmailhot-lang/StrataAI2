BEGIN;
LOCK TABLE audit_events IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE organization_metadata_events IN SHARE ROW EXCLUSIVE MODE;
-- Capture future actual transitions only; legacy revoked rows are not evidence
-- of a new command or its actor and must never acquire reconstructed events.
CREATE TABLE organization_invitation_revocations (
 invitation_id uuid NOT NULL,tenant_id uuid NOT NULL,entity_version bigint NOT NULL CHECK(entity_version>1),
 revoked_at timestamptz NOT NULL CHECK(isfinite(revoked_at)),
 updated_at timestamptz NOT NULL CHECK(isfinite(updated_at)),
 PRIMARY KEY(invitation_id,tenant_id,entity_version),
 FOREIGN KEY(invitation_id,tenant_id) REFERENCES invitations(id,tenant_id) ON DELETE CASCADE
);
ALTER TABLE organization_invitation_revocations ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_invitation_revocations FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_invitation_revocation_tenant ON organization_invitation_revocations
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION capture_organization_invitation_revocation() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF OLD.target_surface='INTERNAL' AND NEW.target_surface='INTERNAL'
  AND OLD.target_board_id IS NULL AND NEW.target_board_id IS NULL
  AND OLD.accepted_at IS NULL AND NEW.accepted_at IS NULL
  AND OLD.revoked_at IS NULL AND NEW.revoked_at IS NOT NULL AND NEW.version>OLD.version THEN
  INSERT INTO public.organization_invitation_revocations(invitation_id,tenant_id,entity_version,revoked_at,updated_at)
   VALUES(NEW.id,NEW.tenant_id,NEW.version,NEW.revoked_at,NEW.updated_at);
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION capture_organization_invitation_revocation() FROM PUBLIC;
CREATE TRIGGER organization_invitation_revocation AFTER UPDATE ON invitations
 FOR EACH ROW EXECUTE FUNCTION capture_organization_invitation_revocation();
ALTER TABLE organization_metadata_events DROP CONSTRAINT organization_metadata_event_subject,
 ADD CONSTRAINT organization_metadata_event_subject CHECK(
  (event_type='ORGANIZATION_CREATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version=1)
  OR (event_type='ORGANIZATION_UPDATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version>1)
  OR (event_type='ORGANIZATION_MEMBER_ADDED' AND entity_type='OrganizationMembership')
  OR (event_type='ORGANIZATION_MEMBER_REMOVED' AND entity_type='OrganizationMembership' AND entity_version>1)
  OR (event_type='ORGANIZATION_MEMBER_INVITED' AND entity_type='Invitation' AND entity_version=1)
  OR (event_type='INVITATION_REVOKED' AND entity_type='Invitation' AND entity_version>1)),
 ADD COLUMN revoked_invitation_subject_id uuid GENERATED ALWAYS AS
  (CASE WHEN event_type='INVITATION_REVOKED' THEN entity_id END) STORED,
 ADD CONSTRAINT organization_metadata_invitation_revocation FOREIGN KEY(revoked_invitation_subject_id,tenant_id,entity_version)
  REFERENCES organization_invitation_revocations(invitation_id,tenant_id,entity_version) ON DELETE RESTRICT;
CREATE FUNCTION journal_organization_invitation_revocation() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE subject public.invitations%ROWTYPE; caller public.organization_members%ROWTYPE; next_sequence bigint;
BEGIN
 IF NEW.event_type<>'INVITATION_REVOKED' THEN RETURN NEW; END IF;
 IF NEW.tenant_id IS NULL OR NEW.actor_id IS NULL OR NEW.entity_type<>'Invitation' OR NEW.safe_metadata<>'{}'::jsonb THEN
  RAISE EXCEPTION 'Organization invitation revocation source is invalid' USING ERRCODE='23514';
 END IF;
 SELECT * INTO subject FROM public.invitations WHERE tenant_id=NEW.tenant_id AND id=NEW.entity_id FOR SHARE;
 IF subject.id IS NULL THEN RAISE EXCEPTION 'Invitation revocation subject is unavailable' USING ERRCODE='23514'; END IF;
 -- Board and Portal audits retain their own authorization and event surfaces.
 IF subject.target_surface<>'INTERNAL' OR subject.target_board_id IS NOT NULL THEN RETURN NEW; END IF;
 PERFORM id FROM public.organizations WHERE id=NEW.tenant_id AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Invitation revocation parent is unavailable' USING ERRCODE='23514'; END IF;
 SELECT * INTO caller FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id FOR SHARE;
 IF subject.accepted_at IS NOT NULL OR subject.revoked_at IS NULL OR subject.version<=1
  OR caller.id IS NULL OR caller.status<>'ACTIVE' OR caller.role NOT IN ('OWNER','ADMIN')
  OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=NEW.actor_id AND status='ACTIVE')
  OR NOT EXISTS(SELECT 1 FROM public.organization_invitation_revocations WHERE tenant_id=NEW.tenant_id
   AND invitation_id=subject.id AND entity_version=subject.version
   AND revoked_at=subject.revoked_at AND updated_at=subject.updated_at) THEN
  RAISE EXCEPTION 'Invitation revocation transition or authority is unavailable' USING ERRCODE='23514';
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
REVOKE ALL ON FUNCTION journal_organization_invitation_revocation() FROM PUBLIC;
CREATE TRIGGER organization_invitation_revocation_journal AFTER INSERT ON audit_events
 FOR EACH ROW EXECUTE FUNCTION journal_organization_invitation_revocation();
INSERT INTO schema_migrations(version) VALUES('100_organization_invitation_revocation_events');
COMMIT;
