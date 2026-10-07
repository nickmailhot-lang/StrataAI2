BEGIN;
LOCK TABLE organizations,audit_events,organization_lifecycle_events,invitation_recipient_authority_sources IN SHARE ROW EXCLUSIVE MODE;
-- Future actual transitions only. No actor or event is inferred for legacy state.
CREATE TABLE invitation_recipient_organization_lifecycle_proofs (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 entity_version bigint NOT NULL CHECK(entity_version>=2),
 status text NOT NULL CHECK(status IN ('DELETING','DELETED')),
 changed_at timestamptz NOT NULL CHECK(isfinite(changed_at)),
 owning_transaction bigint NOT NULL CHECK(owning_transaction>0),
 PRIMARY KEY(tenant_id,entity_version)
);
ALTER TABLE invitation_recipient_organization_lifecycle_proofs ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_organization_lifecycle_proofs FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_organization_lifecycle_proof_tenant ON invitation_recipient_organization_lifecycle_proofs
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE TRIGGER invitation_organization_lifecycle_proof_history BEFORE UPDATE OR DELETE ON invitation_recipient_organization_lifecycle_proofs
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
CREATE FUNCTION capture_invitation_organization_lifecycle_transition() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF NEW.version=OLD.version+1 AND isfinite(NEW.updated_at)
  AND ((OLD.status='ACTIVE' AND NEW.status='DELETING') OR (OLD.status='DELETING' AND NEW.status='DELETED')) THEN
  INSERT INTO public.invitation_recipient_organization_lifecycle_proofs(tenant_id,entity_version,status,changed_at,owning_transaction)
  VALUES(NEW.id,NEW.version,NEW.status,NEW.updated_at,txid_current());
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION capture_invitation_organization_lifecycle_transition() FROM PUBLIC;
CREATE TRIGGER invitation_organization_lifecycle_capture AFTER UPDATE OF status ON organizations
 FOR EACH ROW EXECUTE FUNCTION capture_invitation_organization_lifecycle_transition();
CREATE TABLE invitation_recipient_organization_lifecycle_sources (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 event_id uuid NOT NULL,
 request_audit_id uuid REFERENCES audit_events(id) ON DELETE RESTRICT,
 terminal_event_id uuid REFERENCES organization_lifecycle_events(event_id) ON DELETE RESTRICT,
 event_type text NOT NULL CHECK(event_type IN ('ORGANIZATION_DELETION_REQUESTED','ORGANIZATION_DELETED')),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 entity_type text NOT NULL CHECK(entity_type='Organization'),entity_id uuid NOT NULL CHECK(entity_id=tenant_id),
 entity_version bigint NOT NULL,correlation_id text NOT NULL CHECK(correlation_id ~ '^[A-Za-z0-9._-]{1,64}$'),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 PRIMARY KEY(tenant_id,event_id),UNIQUE(tenant_id,entity_version),
 FOREIGN KEY(tenant_id,entity_version) REFERENCES invitation_recipient_organization_lifecycle_proofs(tenant_id,entity_version) ON DELETE RESTRICT,
 CHECK((event_type='ORGANIZATION_DELETION_REQUESTED' AND request_audit_id=event_id AND terminal_event_id IS NULL)
  OR (event_type='ORGANIZATION_DELETED' AND terminal_event_id=event_id AND request_audit_id IS NULL)),
 CHECK(num_nonnulls(request_audit_id,terminal_event_id)=1)
);
ALTER TABLE invitation_recipient_organization_lifecycle_sources ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_organization_lifecycle_sources FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_organization_lifecycle_source_tenant ON invitation_recipient_organization_lifecycle_sources
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE TRIGGER invitation_organization_lifecycle_source_history BEFORE UPDATE OR DELETE ON invitation_recipient_organization_lifecycle_sources
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
ALTER TABLE invitation_recipient_authority_sources
 ADD COLUMN organization_lifecycle_source_id uuid,
 DROP CONSTRAINT invitation_recipient_authority_sources_check,
 DROP CONSTRAINT invitation_recipient_authority_sources_check1,
 ADD CONSTRAINT invitation_authority_source_exclusive CHECK(num_nonnulls(metadata_event_id,work_event_id,organization_lifecycle_source_id)=1),
 ADD CONSTRAINT invitation_authority_source_identity CHECK(
  (metadata_event_id=event_id AND work_event_id IS NULL AND organization_lifecycle_source_id IS NULL)
  OR (work_event_id=event_id AND metadata_event_id IS NULL AND organization_lifecycle_source_id IS NULL)
  OR (organization_lifecycle_source_id=event_id AND metadata_event_id IS NULL AND work_event_id IS NULL)),
 ADD CONSTRAINT invitation_authority_organization_lifecycle_source FOREIGN KEY(tenant_id,organization_lifecycle_source_id)
 REFERENCES invitation_recipient_organization_lifecycle_sources(tenant_id,event_id) ON DELETE RESTRICT;
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
 ON e.tenant_id=s.tenant_id AND e.event_id=s.organization_lifecycle_source_id;
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
 ELSIF TG_TABLE_NAME='invitation_recipient_organization_lifecycle_sources' THEN
  INSERT INTO public.invitation_recipient_authority_sources(tenant_id,event_id,organization_lifecycle_source_id)
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

CREATE TRIGGER invitation_lifecycle_authority_publication AFTER INSERT ON invitation_recipient_organization_lifecycle_sources
 FOR EACH ROW EXECUTE FUNCTION publish_invitation_recipient_authority();
CREATE FUNCTION journal_invitation_organization_lifecycle_source() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE parent public.organizations%ROWTYPE; proof public.invitation_recipient_organization_lifecycle_proofs%ROWTYPE;
BEGIN
 IF TG_TABLE_NAME='audit_events' AND NEW.event_type<>'ORGANIZATION_DELETION_REQUESTED' THEN RETURN NEW; END IF;
 SELECT * INTO parent FROM public.organizations WHERE id=NEW.tenant_id FOR SHARE;
 SELECT * INTO proof FROM public.invitation_recipient_organization_lifecycle_proofs
 WHERE tenant_id=NEW.tenant_id AND entity_version=parent.version;
 IF parent.id IS NULL OR proof.tenant_id IS NULL OR proof.owning_transaction<>txid_current()
  OR proof.changed_at IS DISTINCT FROM parent.updated_at OR proof.status IS DISTINCT FROM parent.status
  OR NEW.entity_type<>'Organization' OR NEW.entity_id IS DISTINCT FROM NEW.tenant_id OR NEW.actor_id IS NULL THEN
  RAISE EXCEPTION 'Invitation Organization lifecycle transition is unproven' USING ERRCODE='23514';
 END IF;
 IF TG_TABLE_NAME='audit_events' THEN
  IF parent.status<>'DELETING' OR NEW.safe_metadata<>'{}'::jsonb
   OR NOT EXISTS(SELECT 1 FROM public.organization_members m JOIN public.users u ON u.id=m.user_id
    WHERE m.tenant_id=NEW.tenant_id AND m.user_id=NEW.actor_id AND m.role='OWNER' AND m.status='ACTIVE' AND u.status='ACTIVE') THEN
   RAISE EXCEPTION 'Invitation deletion request authority is unavailable' USING ERRCODE='23514';
  END IF;
  INSERT INTO public.invitation_recipient_organization_lifecycle_sources(tenant_id,event_id,request_audit_id,event_type,
   actor_id,entity_type,entity_id,entity_version,correlation_id,created_at)
  VALUES(NEW.tenant_id,NEW.id,NEW.id,NEW.event_type,NEW.actor_id,NEW.entity_type,NEW.entity_id,parent.version,NEW.correlation_id,proof.changed_at);
 ELSE
  -- The existing leased terminal capability owns actor/request/graph admission.
  -- A historical accepted actor can be deactivated before actual completion.
  IF NEW.event_type<>'ORGANIZATION_DELETED' OR parent.status<>'DELETED' OR NEW.metadata<>'{}'::jsonb
   OR NEW.entity_version<>parent.version OR NEW.created_at IS DISTINCT FROM proof.changed_at
   OR parent.deleted_by IS DISTINCT FROM NEW.actor_id THEN
   RAISE EXCEPTION 'Invitation terminal source is unavailable' USING ERRCODE='23514';
  END IF;
  INSERT INTO public.invitation_recipient_organization_lifecycle_sources(tenant_id,event_id,terminal_event_id,event_type,
   actor_id,entity_type,entity_id,entity_version,correlation_id,created_at)
  VALUES(NEW.tenant_id,NEW.event_id,NEW.event_id,NEW.event_type,NEW.actor_id,NEW.entity_type,NEW.entity_id,NEW.entity_version,NEW.correlation_id,NEW.created_at);
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_invitation_organization_lifecycle_source() FROM PUBLIC;
CREATE TRIGGER invitation_deletion_request_source AFTER INSERT ON audit_events
 FOR EACH ROW EXECUTE FUNCTION journal_invitation_organization_lifecycle_source();
CREATE TRIGGER invitation_deletion_terminal_source AFTER INSERT ON organization_lifecycle_events
 FOR EACH ROW EXECUTE FUNCTION journal_invitation_organization_lifecycle_source();
REVOKE ALL ON invitation_recipient_organization_lifecycle_proofs,invitation_recipient_organization_lifecycle_sources FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('108_invitation_recipient_organization_lifecycle');
COMMIT;
