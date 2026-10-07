BEGIN;
LOCK TABLE invitations IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE audit_events IN SHARE ROW EXCLUSIVE MODE;
-- Acceptance preserves an existing active Board Admin for a Member target.
-- The source must prove that actual canonical grant without requiring a downgrade.
CREATE OR REPLACE FUNCTION journal_invitation_recipient_event() RETURNS trigger
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
      AND m.user_id=NEW.actor_id AND m.status='ACTIVE'
      AND (m.role=subject.target_board_role OR subject.target_board_role='MEMBER' AND m.role='ADMIN'))
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
INSERT INTO schema_migrations(version) VALUES('104_invitation_recipient_retained_board_admin');
COMMIT;
