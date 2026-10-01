BEGIN;

-- Board access remains on the INTERNAL surface, with Organization MEMBER
-- enrollment separate from the explicit Board ADMIN/MEMBER grant.
ALTER TABLE invitations ADD COLUMN target_board_id uuid, ADD COLUMN target_board_role text,
    ADD CONSTRAINT invitation_board_target_shape CHECK (
        (target_board_id IS NULL AND target_board_role IS NULL) OR
        (target_board_id IS NOT NULL AND target_board_role IS NOT NULL
         AND target_board_id <> '00000000-0000-0000-0000-000000000000'::uuid
         AND target_surface='INTERNAL' AND target_role='MEMBER'
         AND target_board_role IN ('ADMIN','MEMBER'))),
    ADD CONSTRAINT invitation_board_target_tenant FOREIGN KEY(target_board_id,tenant_id)
        REFERENCES boards(id,tenant_id) ON DELETE RESTRICT;
CREATE INDEX ix_invitations_board_pending ON invitations(tenant_id,target_board_id,id)
    WHERE target_board_id IS NOT NULL AND accepted_at IS NULL AND revoked_at IS NULL;

ALTER TABLE invitation_routes ADD COLUMN target_board_id uuid, ADD COLUMN target_board_role text;
CREATE OR REPLACE FUNCTION sync_invitation_route()
RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF TG_OP='DELETE' THEN
        DELETE FROM invitation_routes WHERE invitation_id=OLD.id;
        RETURN OLD;
    END IF;
    INSERT INTO invitation_routes(token_hash,invitation_id,tenant_id,email_normalized,target_surface,target_role,
        expires_at,accepted_at,revoked_at,accepted_by_user_id,organization_name,target_board_id,target_board_role)
    VALUES(NEW.token_hash,NEW.id,NEW.tenant_id,NEW.email_normalized,NEW.target_surface,NEW.target_role,
        NEW.expires_at,NEW.accepted_at,NEW.revoked_at,NEW.accepted_by_user_id,
        (SELECT name FROM organizations WHERE id=NEW.tenant_id),NEW.target_board_id,NEW.target_board_role)
    ON CONFLICT(invitation_id) DO UPDATE SET token_hash=EXCLUDED.token_hash,email_normalized=EXCLUDED.email_normalized,
        target_surface=EXCLUDED.target_surface,target_role=EXCLUDED.target_role,expires_at=EXCLUDED.expires_at,
        accepted_at=EXCLUDED.accepted_at,revoked_at=EXCLUDED.revoked_at,accepted_by_user_id=EXCLUDED.accepted_by_user_id,
        target_board_id=EXCLUDED.target_board_id,target_board_role=EXCLUDED.target_board_role;
    RETURN NEW;
END;
$$;
CREATE OR REPLACE FUNCTION protect_invitation_acceptance() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    IF OLD.accepted_by_user_id IS NOT NULL AND
       (NEW.accepted_by_user_id,NEW.accepted_at,NEW.tenant_id,NEW.email_normalized,NEW.target_surface,NEW.target_role,
        NEW.created_by_user_id,NEW.target_board_id,NEW.target_board_role)
       IS DISTINCT FROM
       (OLD.accepted_by_user_id,OLD.accepted_at,OLD.tenant_id,OLD.email_normalized,OLD.target_surface,OLD.target_role,
        OLD.created_by_user_id,OLD.target_board_id,OLD.target_board_role) THEN
        RAISE EXCEPTION 'Completed invitation acceptance is immutable' USING ERRCODE='23514';
    END IF;
    RETURN NEW;
END;
$$;

-- Existing mail envelopes do not yet describe a Board target. Never treat one
-- as an ordinary Organization invitation during the staged consumer upgrade.
CREATE OR REPLACE FUNCTION load_invitation_mail(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_require_verified boolean)
RETURNS TABLE(job_id uuid,tenant_id uuid,invitation_id uuid,issuer_id uuid,recipient_email text,
 target_surface text,target_role text,expires_at timestamptz,key_id text,sender_address text,
 public_origin text,provider_account text,template_version integer,state text,canonical_token_hash text,is_usable boolean)
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT m.job_id,m.tenant_id,m.invitation_id,m.issuer_id,m.recipient_email,
 m.target_surface,m.target_role,m.expires_at,m.key_id,m.sender_address,m.public_origin,
 m.provider_account,m.template_version,m.state,i.token_hash::text,
 COALESCE(i.target_board_id IS NULL AND o.status='ACTIVE' AND u.status='ACTIVE' AND (NOT p_require_verified OR u.email_verified)
 AND a.status='ACTIVE' AND a.role IN ('OWNER','ADMIN')
 AND (m.target_surface<>'INTERNAL' OR m.target_role<>'OWNER' OR a.role='OWNER')
 AND i.accepted_at IS NULL AND i.revoked_at IS NULL AND i.expires_at>clock_timestamp()
 AND i.expires_at=m.expires_at AND i.invited_email=m.recipient_email
 AND i.email_normalized=upper(btrim(m.recipient_email))
 AND i.target_surface=m.target_surface AND i.target_role=m.target_role,false)
 FROM public.invitation_mail_intents m
 JOIN public.background_jobs j ON (j.id,j.tenant_id,j.actor_id)=(m.job_id,m.tenant_id,m.issuer_id)
 JOIN public.invitations i ON (i.id,i.tenant_id,i.created_by_user_id)=(m.invitation_id,m.tenant_id,m.issuer_id)
 JOIN public.organizations o ON o.id=m.tenant_id
 LEFT JOIN public.organization_members a ON a.tenant_id=m.tenant_id AND a.user_id=m.issuer_id
 LEFT JOIN public.users u ON u.id=m.issuer_id
 WHERE p_tenant=NULLIF(current_setting('app.tenant_id',true),'')::uuid
 AND (m.job_id,m.tenant_id,m.issuer_id)=(p_job,p_tenant,p_actor)
 AND j.job_type='INVITATION_EMAIL' AND j.service_identity='invitation-email-delivery'
 AND j.safe_metadata=jsonb_build_object('invitationId',m.invitation_id)
 AND j.state='RUNNING' AND j.worker_id=p_worker AND j.lease_id=p_lease
 AND j.lease_expires_at>clock_timestamp();
$$;
REVOKE ALL ON FUNCTION load_invitation_mail(uuid,uuid,uuid,uuid,uuid,boolean) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES ('025_board_invitation_targets');
COMMIT;
