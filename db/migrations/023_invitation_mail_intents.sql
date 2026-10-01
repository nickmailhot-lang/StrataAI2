BEGIN;

ALTER TABLE background_jobs ADD CONSTRAINT background_jobs_mail_scope_unique UNIQUE(id,tenant_id,actor_id);
CREATE TABLE invitation_mail_intents (
    job_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    invitation_id uuid NOT NULL,
    issuer_id uuid NOT NULL,
    recipient_email text NOT NULL CHECK (length(recipient_email) BETWEEN 1 AND 320),
    target_surface text NOT NULL CHECK (target_surface IN ('INTERNAL','PORTAL')),
    target_role text NOT NULL,
    expires_at timestamptz NOT NULL,
    key_id text NOT NULL CHECK (key_id ~ '^[A-Za-z0-9_-]{1,32}$'),
    sender_address text NOT NULL CHECK (length(sender_address) BETWEEN 1 AND 320),
    public_origin text NOT NULL CHECK (length(public_origin) BETWEEN 1 AND 2048 AND public_origin LIKE 'https://%'),
    provider_account text NOT NULL CHECK (length(provider_account) BETWEEN 1 AND 80),
    template_version integer NOT NULL CHECK (template_version = 1),
    state text NOT NULL DEFAULT 'PENDING' CHECK (state IN ('PENDING','SENT','CANCELLED','FAILED')),
    provider_receipt_id uuid,
    finished_at timestamptz,
    safe_error_code text CHECK (safe_error_code ~ '^[a-z][a-z0-9_]{0,79}$'),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    UNIQUE(tenant_id,invitation_id),
    FOREIGN KEY(job_id,tenant_id,issuer_id) REFERENCES background_jobs(id,tenant_id,actor_id) ON DELETE RESTRICT,
    FOREIGN KEY(invitation_id,tenant_id,issuer_id) REFERENCES invitations(id,tenant_id,created_by_user_id) ON DELETE RESTRICT,
    CHECK ((state = 'SENT') = (provider_receipt_id IS NOT NULL)),
    CHECK (provider_receipt_id IS NULL OR provider_receipt_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CHECK ((state <> 'PENDING') = (finished_at IS NOT NULL)),
    CHECK ((state IN ('FAILED','CANCELLED')) = (safe_error_code IS NOT NULL)),
    CHECK ((target_surface='INTERNAL' AND target_role IN ('OWNER','ADMIN','MEMBER'))
        OR (target_surface='PORTAL' AND target_role IN ('OWNER','CO_OWNER','TENANT','OCCUPANT','AUTHORIZED_REPRESENTATIVE','OTHER')))
);
ALTER TABLE invitation_mail_intents ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_mail_intents FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_mail_tenant_isolation ON invitation_mail_intents
    USING (tenant_id = NULLIF(current_setting('app.tenant_id',true),'')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id',true),'')::uuid);

-- These privileged capabilities expose no global identity fields. Their owner
-- may bypass RLS, so explicit tenant, actor, metadata and live lease predicates
-- are mandatory independently of the table policies. PUBLIC has no execution.
CREATE FUNCTION load_invitation_mail(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,p_require_verified boolean)
RETURNS TABLE(job_id uuid,tenant_id uuid,invitation_id uuid,issuer_id uuid,recipient_email text,
 target_surface text,target_role text,expires_at timestamptz,key_id text,sender_address text,
 public_origin text,provider_account text,template_version integer,state text,canonical_token_hash text,is_usable boolean)
LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT m.job_id,m.tenant_id,m.invitation_id,m.issuer_id,m.recipient_email,
 m.target_surface,m.target_role,m.expires_at,m.key_id,m.sender_address,m.public_origin,
 m.provider_account,m.template_version,m.state,i.token_hash::text,
 COALESCE(o.status='ACTIVE' AND u.status='ACTIVE' AND (NOT p_require_verified OR u.email_verified)
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

CREATE FUNCTION finish_invitation_mail(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_state text,p_error text,p_receipt uuid) RETURNS boolean
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF p_state IS NULL OR p_state NOT IN ('SENT','CANCELLED','FAILED') THEN RETURN false; END IF;
 -- Lock both rows before the final database-clock check. A wait for either
 -- lock must not turn an expired/replaced lease into a successful receipt.
 IF p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN RETURN false; END IF;
 PERFORM 1 FROM public.invitation_mail_intents m
 WHERE (m.job_id,m.tenant_id,m.issuer_id)=(p_job,p_tenant,p_actor) AND m.state='PENDING' FOR UPDATE;
 IF NOT FOUND THEN RETURN false; END IF;
 PERFORM 1 FROM public.background_jobs j WHERE (j.id,j.tenant_id,j.actor_id)=(p_job,p_tenant,p_actor) FOR UPDATE;
 IF NOT FOUND THEN RETURN false; END IF;
 UPDATE public.invitation_mail_intents m SET state=p_state,safe_error_code=p_error,
 provider_receipt_id=p_receipt,finished_at=clock_timestamp(),version=m.version+1
 WHERE p_tenant=NULLIF(current_setting('app.tenant_id',true),'')::uuid
 AND (m.job_id,m.tenant_id,m.issuer_id)=(p_job,p_tenant,p_actor) AND m.state='PENDING'
 AND EXISTS(SELECT 1 FROM public.background_jobs j
 WHERE (j.id,j.tenant_id,j.actor_id)=(m.job_id,m.tenant_id,m.issuer_id)
 AND j.job_type='INVITATION_EMAIL' AND j.service_identity='invitation-email-delivery'
 AND j.safe_metadata=jsonb_build_object('invitationId',m.invitation_id)
 AND j.state='RUNNING' AND j.worker_id=p_worker AND j.lease_id=p_lease
 AND j.lease_expires_at>clock_timestamp());
 RETURN FOUND;
END;
$$;
REVOKE ALL ON FUNCTION finish_invitation_mail(uuid,uuid,uuid,uuid,uuid,text,text,uuid) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES ('023_invitation_mail_intents');
COMMIT;
