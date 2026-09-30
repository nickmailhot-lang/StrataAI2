BEGIN;

CREATE TABLE users (
    id uuid PRIMARY KEY,
    email text NOT NULL,
    email_normalized text NOT NULL UNIQUE,
    display_name text NOT NULL CHECK (length(btrim(display_name)) > 0),
    avatar_url text,
    locale text NOT NULL DEFAULT 'en-CA',
    timezone text NOT NULL DEFAULT 'America/Vancouver',
    status text NOT NULL CHECK (
        status IN ('PENDING_VERIFICATION', 'ACTIVE', 'SUSPENDED', 'DEACTIVATED')
    ),
    email_verified boolean NOT NULL DEFAULT false,
    password_hash text NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0)
);

CREATE TABLE sessions (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    token_hash char(64) NOT NULL UNIQUE,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    revoked_at timestamptz,
    last_seen_at timestamptz
);

CREATE INDEX ix_sessions_user_active
    ON sessions(user_id, expires_at DESC)
    WHERE revoked_at IS NULL;

CREATE TABLE password_reset_tokens (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    token_hash char(64) NOT NULL UNIQUE,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    used_at timestamptz,
    revoked_at timestamptz
);

CREATE INDEX ix_password_reset_user_active
    ON password_reset_tokens(user_id, expires_at DESC)
    WHERE used_at IS NULL AND revoked_at IS NULL;

CREATE TABLE email_verification_tokens (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    token_hash char(64) NOT NULL UNIQUE,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    used_at timestamptz,
    revoked_at timestamptz
);

CREATE INDEX ix_email_verification_user_active
    ON email_verification_tokens(user_id, expires_at DESC)
    WHERE used_at IS NULL AND revoked_at IS NULL;

ALTER TABLE organizations
    ADD COLUMN description text,
    ADD COLUMN logo_url text,
    ADD COLUMN owner_user_id uuid REFERENCES users(id) ON DELETE RESTRICT,
    ADD COLUMN status text NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('ACTIVE', 'ARCHIVED', 'DELETING'));

CREATE TABLE organization_members (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    role text NOT NULL CHECK (role IN ('OWNER', 'ADMIN', 'MEMBER')),
    status text NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('ACTIVE', 'SUSPENDED', 'REMOVED')),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    UNIQUE (tenant_id, user_id)
);

CREATE INDEX ix_organization_members_user
    ON organization_members(user_id, tenant_id);

CREATE TABLE invitations (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    invited_email text NOT NULL,
    email_normalized text NOT NULL,
    token_hash char(64) NOT NULL UNIQUE,
    target_surface text NOT NULL CHECK (target_surface IN ('INTERNAL', 'PORTAL')),
    target_role text NOT NULL,
    created_by_user_id uuid REFERENCES users(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    accepted_at timestamptz,
    revoked_at timestamptz
);

CREATE INDEX ix_invitations_tenant_email
    ON invitations(tenant_id, email_normalized, expires_at DESC);

CREATE TABLE portal_access (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    status text NOT NULL CHECK (
        status IN ('INVITED', 'ACTIVE', 'SUSPENDED', 'REVOKED', 'EXPIRED')
    ),
    relationship_type text NOT NULL CHECK (
        relationship_type IN (
            'OWNER',
            'CO_OWNER',
            'TENANT',
            'OCCUPANT',
            'AUTHORIZED_REPRESENTATIVE',
            'OTHER'
        )
    ),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    UNIQUE (tenant_id, user_id, relationship_type)
);

ALTER TABLE organization_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_members FORCE ROW LEVEL SECURITY;
ALTER TABLE invitations ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitations FORCE ROW LEVEL SECURITY;
ALTER TABLE portal_access ENABLE ROW LEVEL SECURITY;
ALTER TABLE portal_access FORCE ROW LEVEL SECURITY;

CREATE POLICY organization_members_tenant_isolation ON organization_members
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

CREATE POLICY invitations_tenant_isolation ON invitations
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

CREATE POLICY portal_access_tenant_isolation ON portal_access
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

INSERT INTO schema_migrations(version)
VALUES ('003_identity')
ON CONFLICT (version) DO NOTHING;

COMMIT;
