BEGIN;
LOCK TABLE audit_events IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE organization_metadata_events IN SHARE ROW EXCLUSIVE MODE;
CREATE TABLE organization_membership_removals (
 membership_id uuid NOT NULL, tenant_id uuid NOT NULL, entity_version bigint NOT NULL CHECK(entity_version>1),
 previous_role text NOT NULL CHECK(previous_role IN ('OWNER','ADMIN','MEMBER')),
 removed_at timestamptz NOT NULL CHECK(isfinite(removed_at)),
 PRIMARY KEY(membership_id,tenant_id,entity_version),
 FOREIGN KEY(membership_id,tenant_id) REFERENCES organization_members(id,tenant_id) ON DELETE CASCADE
);
ALTER TABLE organization_membership_removals ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_membership_removals FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_membership_removal_tenant ON organization_membership_removals
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION capture_organization_membership_removal() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF OLD.status='ACTIVE' AND NEW.status='REMOVED' AND NEW.version>OLD.version THEN
  INSERT INTO public.organization_membership_removals(membership_id,tenant_id,entity_version,previous_role,removed_at)
   VALUES(NEW.id,NEW.tenant_id,NEW.version,OLD.role,NEW.updated_at);
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION capture_organization_membership_removal() FROM PUBLIC;
CREATE TRIGGER organization_membership_removal AFTER UPDATE ON organization_members
 FOR EACH ROW EXECUTE FUNCTION capture_organization_membership_removal();
ALTER TABLE organization_metadata_events
 DROP CONSTRAINT organization_metadata_event_subject,
 DROP CONSTRAINT organization_metadata_membership_activation,
 ADD CONSTRAINT organization_metadata_event_subject CHECK(
  (event_type='ORGANIZATION_CREATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version=1)
  OR (event_type='ORGANIZATION_UPDATED' AND entity_type='Organization' AND entity_id=tenant_id AND entity_version>1)
  OR (event_type='ORGANIZATION_MEMBER_ADDED' AND entity_type='OrganizationMembership')
  OR (event_type='ORGANIZATION_MEMBER_REMOVED' AND entity_type='OrganizationMembership' AND entity_version>1)),
 ADD COLUMN activation_subject_id uuid GENERATED ALWAYS AS
  (CASE WHEN event_type='ORGANIZATION_MEMBER_ADDED' THEN entity_id END) STORED,
 ADD COLUMN removal_subject_id uuid GENERATED ALWAYS AS
  (CASE WHEN event_type='ORGANIZATION_MEMBER_REMOVED' THEN entity_id END) STORED,
 ADD CONSTRAINT organization_metadata_membership_activation FOREIGN KEY(activation_subject_id,tenant_id,entity_version)
  REFERENCES organization_membership_activations(membership_id,tenant_id,entity_version) ON DELETE RESTRICT,
 ADD CONSTRAINT organization_metadata_membership_removal FOREIGN KEY(removal_subject_id,tenant_id,entity_version)
  REFERENCES organization_membership_removals(membership_id,tenant_id,entity_version) ON DELETE RESTRICT;

-- No removal history is reconstructed. The original administrative-removal or
-- departure audit is the identity of the shared membership-removal fact.
CREATE FUNCTION journal_organization_member_removal() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE subject public.organization_members%ROWTYPE; proof public.organization_membership_removals%ROWTYPE;
 caller public.organization_members%ROWTYPE; next_sequence bigint;
BEGIN
 IF NEW.event_type NOT IN ('ORGANIZATION_MEMBER_REMOVED','ORGANIZATION_MEMBER_LEFT') THEN RETURN NEW; END IF;
 IF NEW.tenant_id IS NULL OR NEW.actor_id IS NULL OR NEW.entity_type<>'User' OR NEW.safe_metadata<>'{}'::jsonb
  OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=NEW.actor_id AND status='ACTIVE') THEN
  RAISE EXCEPTION 'Organization removal source is invalid' USING ERRCODE='23514';
 END IF;
 PERFORM id FROM public.organizations WHERE id=NEW.tenant_id AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Organization removal parent is unavailable' USING ERRCODE='23514'; END IF;
 SELECT * INTO subject FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.entity_id FOR SHARE;
 SELECT * INTO proof FROM public.organization_membership_removals WHERE tenant_id=NEW.tenant_id
  AND membership_id=subject.id AND entity_version=subject.version AND removed_at=subject.updated_at;
 IF subject.id IS NULL OR subject.status<>'REMOVED' OR proof.membership_id IS NULL
  OR NEW.event_type='ORGANIZATION_MEMBER_LEFT' AND NEW.actor_id IS DISTINCT FROM subject.user_id THEN
  RAISE EXCEPTION 'Organization removal transition proof is unavailable' USING ERRCODE='23514';
 END IF;
 IF NEW.actor_id IS DISTINCT FROM subject.user_id THEN
  SELECT * INTO caller FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id FOR SHARE;
  IF caller.id IS NULL OR caller.status<>'ACTIVE' OR caller.role NOT IN ('OWNER','ADMIN')
   OR proof.previous_role='OWNER' AND caller.role<>'OWNER' THEN
   RAISE EXCEPTION 'Organization removal authority is unavailable' USING ERRCODE='23514';
  END IF;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM public.organization_members m JOIN public.users u ON u.id=m.user_id
  WHERE m.tenant_id=NEW.tenant_id AND m.status='ACTIVE' AND m.role='OWNER' AND u.status='ACTIVE') THEN
  RAISE EXCEPTION 'Organization removal owner continuity is unavailable' USING ERRCODE='23514';
 END IF;
 INSERT INTO public.organization_metadata_event_streams(tenant_id,last_sequence) VALUES(NEW.tenant_id,1)
 ON CONFLICT(tenant_id) DO UPDATE SET last_sequence=organization_metadata_event_streams.last_sequence+1
 RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.organization_metadata_events(tenant_id,sequence,event_id,event_type,actor_id,entity_type,entity_id,
  entity_version,correlation_id,metadata,created_at)
 VALUES(NEW.tenant_id,next_sequence,NEW.id,'ORGANIZATION_MEMBER_REMOVED',NEW.actor_id,'OrganizationMembership',subject.id,
  subject.version,NEW.correlation_id,'{}'::jsonb,subject.updated_at);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_organization_member_removal() FROM PUBLIC;
CREATE TRIGGER organization_member_removal_journal AFTER INSERT ON audit_events
 FOR EACH ROW EXECUTE FUNCTION journal_organization_member_removal();
INSERT INTO schema_migrations(version) VALUES('098_organization_member_removal_events');
COMMIT;
