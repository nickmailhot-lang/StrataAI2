-- Fixture role: no Organization data or password hashes. Production operators
-- provision their own runtime credentials with this same capability boundary.
CREATE ROLE strataai_identity_mail_ci LOGIN PASSWORD 'identity-ci-password';
GRANT EXECUTE ON FUNCTION public.runtime_database_role_is_safe() TO strataai_identity_mail_ci;
GRANT USAGE ON SCHEMA public TO strataai_identity_mail_ci;
GRANT SELECT,UPDATE ON identity_delivery_jobs TO strataai_identity_mail_ci;
GRANT SELECT(id,email,status,email_verified) ON users TO strataai_identity_mail_ci;
GRANT SELECT(id,user_id,token_hash,used_at,revoked_at,expires_at)
    ON password_reset_tokens,email_verification_tokens TO strataai_identity_mail_ci;
