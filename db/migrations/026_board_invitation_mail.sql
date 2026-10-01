BEGIN;
ALTER TABLE invitation_mail_intents ADD COLUMN target_board_id uuid, ADD COLUMN target_board_role text,
 ADD CONSTRAINT invitation_mail_board_shape CHECK (
  (target_board_id IS NULL AND target_board_role IS NULL) OR
  (target_board_id IS NOT NULL AND target_board_role IS NOT NULL
   AND target_board_id <> '00000000-0000-0000-0000-000000000000'::uuid
   AND target_surface='INTERNAL' AND target_role='MEMBER' AND target_board_role IN ('ADMIN','MEMBER'))),
 ADD CONSTRAINT invitation_mail_board_tenant FOREIGN KEY(target_board_id,tenant_id)
 REFERENCES boards(id,tenant_id) ON DELETE RESTRICT;

-- Preserve the existing narrow return shape and execution grants. A generic
-- proof link carries no Board title/role; canonical target admission occurs
-- here without granting the Worker direct access to identities/memberships.
CREATE OR REPLACE FUNCTION load_invitation_mail(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_require_verified boolean)
RETURNS TABLE(job_id uuid,tenant_id uuid,invitation_id uuid,issuer_id uuid,recipient_email text,
 target_surface text,target_role text,expires_at timestamptz,key_id text,sender_address text,
 public_origin text,provider_account text,template_version integer,state text,canonical_token_hash text,is_usable boolean)
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT m.job_id,m.tenant_id,m.invitation_id,m.issuer_id,m.recipient_email,
 m.target_surface,m.target_role,m.expires_at,m.key_id,m.sender_address,m.public_origin,
 m.provider_account,m.template_version,m.state,i.token_hash::text,
 COALESCE(o.status='ACTIVE' AND u.status='ACTIVE' AND (NOT p_require_verified OR u.email_verified)
 AND a.status='ACTIVE'
 AND (i.target_board_id,i.target_board_role) IS NOT DISTINCT FROM (m.target_board_id,m.target_board_role)
 AND CASE WHEN i.target_board_id IS NULL THEN
   a.role IN ('OWNER','ADMIN') AND (m.target_surface<>'INTERNAL' OR m.target_role<>'OWNER' OR a.role='OWNER')
 ELSE
   b.lifecycle_state='ACTIVE' AND i.target_surface='INTERNAL' AND i.target_role='MEMBER'
   AND i.target_board_role IN ('ADMIN','MEMBER')
   AND (a.role IN ('OWNER','ADMIN') OR (a.role='MEMBER' AND bm.status='ACTIVE' AND bm.role='ADMIN'
     AND EXISTS(SELECT 1 FROM public.users recipient
       JOIN public.organization_members member ON member.user_id=recipient.id AND member.tenant_id=i.tenant_id
       WHERE recipient.email_normalized=i.email_normalized AND recipient.status='ACTIVE'
        AND (NOT p_require_verified OR recipient.email_verified) AND member.status='ACTIVE')))
 END
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
 LEFT JOIN public.boards b ON b.id=i.target_board_id AND b.tenant_id=i.tenant_id
 LEFT JOIN public.board_members bm ON bm.board_id=i.target_board_id AND bm.tenant_id=i.tenant_id AND bm.user_id=i.created_by_user_id
 WHERE p_tenant=NULLIF(current_setting('app.tenant_id',true),'')::uuid
 AND (m.job_id,m.tenant_id,m.issuer_id)=(p_job,p_tenant,p_actor)
 AND j.job_type='INVITATION_EMAIL' AND j.service_identity='invitation-email-delivery'
 AND j.safe_metadata=jsonb_build_object('invitationId',m.invitation_id)
 AND j.state='RUNNING' AND j.worker_id=p_worker AND j.lease_id=p_lease
 AND j.lease_expires_at>clock_timestamp();
$$;
REVOKE ALL ON FUNCTION load_invitation_mail(uuid,uuid,uuid,uuid,uuid,boolean) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES ('026_board_invitation_mail');
COMMIT;
