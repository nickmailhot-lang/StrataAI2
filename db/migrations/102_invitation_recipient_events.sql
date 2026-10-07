BEGIN;
LOCK TABLE invitations IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE audit_events IN SHARE ROW EXCLUSIVE MODE;
-- This is an explicitly cross-Organization recipient routing source. It does
-- not grant Internal membership, Board access or Portal access. No backfill:
-- legacy audit/row state cannot prove a new invitation transition.
CREATE TABLE invitation_recipient_proofs (
 invitation_id uuid NOT NULL,tenant_id uuid NOT NULL,entity_version bigint NOT NULL CHECK(entity_version>0),
 event_type text NOT NULL CHECK(event_type IN ('INVITATION_CREATED','INVITATION_ACCEPTED','INVITATION_REVOKED')),
 email_normalized text NOT NULL CHECK(length(email_normalized) BETWEEN 3 AND 320 AND email_normalized=upper(email_normalized)),
 surface text NOT NULL CHECK(surface IN ('INTERNAL','PORTAL')),target_role text NOT NULL,
 board_id uuid,board_role text,issuer_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 accepted_actor_id uuid REFERENCES users(id) ON DELETE RESTRICT,
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 PRIMARY KEY(invitation_id,tenant_id,entity_version),
 FOREIGN KEY(invitation_id,tenant_id) REFERENCES invitations(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT,
 CHECK((board_id IS NULL AND board_role IS NULL) OR (board_id IS NOT NULL AND surface='INTERNAL' AND target_role='MEMBER' AND board_role IN ('ADMIN','MEMBER'))),
 CHECK((event_type='INVITATION_ACCEPTED')=(accepted_actor_id IS NOT NULL))
);
ALTER TABLE invitation_recipient_proofs ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_proofs FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_recipient_proof_tenant ON invitation_recipient_proofs
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE TABLE invitation_recipient_streams (
 email_normalized text PRIMARY KEY CHECK(length(email_normalized) BETWEEN 3 AND 320 AND email_normalized=upper(email_normalized)),
 last_sequence bigint NOT NULL CHECK(last_sequence>0)
);
CREATE TABLE invitation_recipient_events (
 event_id uuid PRIMARY KEY REFERENCES audit_events(id) ON DELETE RESTRICT,
 email_normalized text NOT NULL REFERENCES invitation_recipient_streams(email_normalized) ON DELETE RESTRICT,
 sequence bigint NOT NULL CHECK(sequence>0),tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 source_event_type text NOT NULL CHECK(source_event_type IN ('ORGANIZATION_MEMBER_INVITED','BOARD_MEMBER_INVITED','INVITATION_ACCEPTED','INVITATION_REVOKED')),
 event_type text NOT NULL CHECK(event_type IN ('INVITATION_CREATED','INVITATION_ACCEPTED','INVITATION_REVOKED')),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 entity_type text NOT NULL CHECK(entity_type='Invitation'),entity_id uuid NOT NULL,entity_version bigint NOT NULL CHECK(entity_version>0),
 correlation_id text NOT NULL CHECK(length(btrim(correlation_id)) BETWEEN 1 AND 64),
 metadata jsonb NOT NULL CHECK(metadata='{}'::jsonb),created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 UNIQUE(email_normalized,sequence),UNIQUE(entity_id,tenant_id,entity_version),
 FOREIGN KEY(entity_id,tenant_id,entity_version) REFERENCES invitation_recipient_proofs(invitation_id,tenant_id,entity_version) ON DELETE RESTRICT
);
ALTER TABLE invitation_recipient_streams ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_streams FORCE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_events FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_recipient_stream_lookup ON invitation_recipient_streams FOR SELECT USING(
 current_setting('app.route_kind',true)='INVITATION_RECIPIENT' AND email_normalized=current_setting('app.route_key',true));
CREATE POLICY invitation_recipient_event_lookup ON invitation_recipient_events FOR SELECT USING(
 current_setting('app.route_kind',true)='INVITATION_RECIPIENT' AND email_normalized=current_setting('app.route_key',true));
CREATE FUNCTION capture_invitation_recipient_transition() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE kind text; accepted_actor uuid;
BEGIN
 IF TG_OP='INSERT' THEN
  IF NEW.version<>1 OR NEW.accepted_at IS NOT NULL OR NEW.revoked_at IS NOT NULL THEN RETURN NEW; END IF;
  kind:='INVITATION_CREATED';
 ELSE
  IF (NEW.email_normalized,NEW.target_surface,NEW.target_role,NEW.target_board_id,NEW.target_board_role,NEW.created_by_user_id)
   IS DISTINCT FROM (OLD.email_normalized,OLD.target_surface,OLD.target_role,OLD.target_board_id,OLD.target_board_role,OLD.created_by_user_id)
   OR NEW.version<=OLD.version THEN RETURN NEW; END IF;
  IF OLD.accepted_at IS NULL AND OLD.revoked_at IS NULL AND NEW.accepted_at IS NOT NULL
   AND NEW.accepted_by_user_id IS NOT NULL AND NEW.revoked_at IS NULL THEN
   kind:='INVITATION_ACCEPTED'; accepted_actor:=NEW.accepted_by_user_id;
  ELSIF OLD.accepted_at IS NULL AND OLD.revoked_at IS NULL AND NEW.accepted_at IS NULL AND NEW.revoked_at IS NOT NULL THEN
   kind:='INVITATION_REVOKED';
  ELSE RETURN NEW; END IF;
 END IF;
 IF NEW.created_by_user_id IS NULL THEN RETURN NEW; END IF;
 INSERT INTO public.invitation_recipient_proofs(invitation_id,tenant_id,entity_version,event_type,email_normalized,
  surface,target_role,board_id,board_role,issuer_id,accepted_actor_id,created_at)
 VALUES(NEW.id,NEW.tenant_id,NEW.version,kind,NEW.email_normalized,NEW.target_surface,NEW.target_role,
  NEW.target_board_id,NEW.target_board_role,NEW.created_by_user_id,accepted_actor,NEW.updated_at);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION capture_invitation_recipient_transition() FROM PUBLIC;
CREATE TRIGGER invitation_recipient_transition AFTER INSERT OR UPDATE ON invitations
 FOR EACH ROW EXECUTE FUNCTION capture_invitation_recipient_transition();
CREATE FUNCTION journal_invitation_recipient_event() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE subject public.invitations%ROWTYPE; proof public.invitation_recipient_proofs%ROWTYPE; kind text; next_sequence bigint;
BEGIN
 IF NEW.event_type NOT IN ('ORGANIZATION_MEMBER_INVITED','BOARD_MEMBER_INVITED','INVITATION_ACCEPTED','INVITATION_REVOKED') THEN RETURN NEW; END IF;
 IF NEW.tenant_id IS NULL OR NEW.actor_id IS NULL OR NEW.entity_type<>'Invitation' OR NEW.safe_metadata<>'{}'::jsonb THEN
  RAISE EXCEPTION 'Invitation recipient source is invalid' USING ERRCODE='23514';
 END IF;
 SELECT * INTO subject FROM public.invitations WHERE tenant_id=NEW.tenant_id AND id=NEW.entity_id FOR SHARE;
 IF subject.id IS NULL THEN RAISE EXCEPTION 'Invitation recipient subject is unavailable' USING ERRCODE='23514'; END IF;
 kind:=CASE WHEN NEW.event_type IN ('ORGANIZATION_MEMBER_INVITED','BOARD_MEMBER_INVITED') THEN 'INVITATION_CREATED' ELSE NEW.event_type END;
 SELECT * INTO proof FROM public.invitation_recipient_proofs WHERE tenant_id=NEW.tenant_id
  AND invitation_id=subject.id AND entity_version=subject.version AND event_type=kind;
 IF proof.invitation_id IS NULL OR proof.email_normalized<>subject.email_normalized OR proof.created_at<>subject.updated_at
  OR (proof.surface,proof.target_role,proof.board_id,proof.board_role,proof.issuer_id)
   IS DISTINCT FROM (subject.target_surface,subject.target_role,subject.target_board_id,subject.target_board_role,subject.created_by_user_id) THEN
  RAISE EXCEPTION 'Invitation recipient transition is unproven' USING ERRCODE='23514';
 END IF;
 PERFORM id FROM public.organizations WHERE id=NEW.tenant_id AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=NEW.actor_id AND status='ACTIVE') THEN
  RAISE EXCEPTION 'Invitation recipient source authority is unavailable' USING ERRCODE='23514';
 END IF;
 IF kind='INVITATION_ACCEPTED' THEN
  IF proof.accepted_actor_id IS DISTINCT FROM NEW.actor_id OR subject.accepted_by_user_id IS DISTINCT FROM NEW.actor_id
   OR subject.accepted_at IS NULL OR subject.revoked_at IS NOT NULL
   OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=subject.created_by_user_id AND status='ACTIVE')
   OR NOT EXISTS(SELECT 1 FROM public.users WHERE id=NEW.actor_id AND email_normalized=proof.email_normalized) THEN
   RAISE EXCEPTION 'Invitation recipient acceptance actor is unavailable' USING ERRCODE='23514';
  END IF;
  IF subject.target_board_id IS NOT NULL THEN
   IF NOT EXISTS(SELECT 1 FROM public.board_members m JOIN public.boards b ON b.id=m.board_id AND b.tenant_id=m.tenant_id
     WHERE m.tenant_id=NEW.tenant_id AND m.board_id=subject.target_board_id AND b.lifecycle_state='ACTIVE'
      AND m.user_id=NEW.actor_id AND m.status='ACTIVE' AND m.role=subject.target_board_role)
    OR NOT EXISTS(SELECT 1 FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id AND status='ACTIVE') THEN
    RAISE EXCEPTION 'Invitation recipient Board grant is unavailable' USING ERRCODE='23514';
   END IF;
  ELSIF subject.target_surface='INTERNAL' THEN
   IF NOT EXISTS(SELECT 1 FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id AND status='ACTIVE' AND role=subject.target_role) THEN
    RAISE EXCEPTION 'Invitation recipient Internal grant is unavailable' USING ERRCODE='23514';
   END IF;
  ELSIF NOT EXISTS(SELECT 1 FROM public.portal_access WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id AND status='ACTIVE' AND relationship_type=subject.target_role) THEN
   RAISE EXCEPTION 'Invitation recipient Portal grant is unavailable' USING ERRCODE='23514';
  END IF;
 ELSE
  IF kind='INVITATION_CREATED' AND (subject.created_by_user_id<>NEW.actor_id OR subject.version<>1
   OR subject.accepted_at IS NOT NULL OR subject.revoked_at IS NOT NULL OR subject.expires_at<=clock_timestamp()
   OR (subject.target_board_id IS NULL)<>(NEW.event_type='ORGANIZATION_MEMBER_INVITED'))
   OR kind='INVITATION_REVOKED' AND (subject.revoked_at IS NULL OR subject.accepted_at IS NOT NULL) THEN
   RAISE EXCEPTION 'Invitation recipient lifecycle source is invalid' USING ERRCODE='23514';
  END IF;
  IF subject.target_board_id IS NULL THEN
   IF NOT EXISTS(SELECT 1 FROM public.organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id
    AND status='ACTIVE' AND role IN ('OWNER','ADMIN') AND (kind<>'INVITATION_CREATED' OR subject.target_surface<>'INTERNAL' OR subject.target_role<>'OWNER' OR role='OWNER')) THEN
    RAISE EXCEPTION 'Invitation recipient administrator is unavailable' USING ERRCODE='23514';
   END IF;
  ELSIF NOT EXISTS(SELECT 1 FROM public.boards b JOIN public.organization_members a ON a.tenant_id=b.tenant_id AND a.user_id=NEW.actor_id
    LEFT JOIN public.board_members m ON m.board_id=b.id AND m.tenant_id=b.tenant_id AND m.user_id=NEW.actor_id
    WHERE b.id=subject.target_board_id AND b.tenant_id=NEW.tenant_id AND b.lifecycle_state='ACTIVE' AND a.status='ACTIVE'
     AND (a.role IN ('OWNER','ADMIN') OR a.role='MEMBER' AND m.status='ACTIVE' AND m.role='ADMIN')) THEN
   RAISE EXCEPTION 'Invitation recipient Board administrator is unavailable' USING ERRCODE='23514';
  END IF;
 END IF;
 INSERT INTO public.invitation_recipient_streams(email_normalized,last_sequence) VALUES(proof.email_normalized,1)
 ON CONFLICT(email_normalized) DO UPDATE SET last_sequence=invitation_recipient_streams.last_sequence+1
 RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.invitation_recipient_events(event_id,email_normalized,sequence,tenant_id,source_event_type,event_type,
  actor_id,entity_type,entity_id,entity_version,correlation_id,metadata,created_at)
 VALUES(NEW.id,proof.email_normalized,next_sequence,NEW.tenant_id,NEW.event_type,kind,NEW.actor_id,'Invitation',
  subject.id,subject.version,NEW.correlation_id,'{}'::jsonb,proof.created_at);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_invitation_recipient_event() FROM PUBLIC;
CREATE TRIGGER audit_invitation_recipient_event AFTER INSERT ON audit_events
 FOR EACH ROW EXECUTE FUNCTION journal_invitation_recipient_event();
CREATE FUNCTION protect_invitation_recipient_history() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN RAISE EXCEPTION 'Invitation recipient history is immutable' USING ERRCODE='23514'; END $$;
REVOKE ALL ON FUNCTION protect_invitation_recipient_history() FROM PUBLIC;
CREATE TRIGGER invitation_recipient_event_immutable BEFORE UPDATE OR DELETE ON invitation_recipient_events
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_recipient_history();
CREATE TRIGGER invitation_recipient_proof_immutable BEFORE UPDATE OR DELETE ON invitation_recipient_proofs
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_recipient_history();
INSERT INTO schema_migrations(version) VALUES('102_invitation_recipient_events');
COMMIT;
