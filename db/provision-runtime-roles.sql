\set ON_ERROR_STOP on
\getenv api_password STRATAAI_API_DB_PASSWORD
\getenv worker_password STRATAAI_WORKER_DB_PASSWORD
SELECT length(:'api_password') >= 20 AND length(:'worker_password') >= 20
    AND :'api_password' <> :'worker_password' AS passwords_valid \gset
\if :passwords_valid
\else
\echo 'Distinct runtime passwords of at least 20 characters are required.'
\quit 3
\endif
BEGIN;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN CREATE ROLE strataai_api_runtime LOGIN; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN CREATE ROLE strataai_worker_runtime LOGIN; END IF;
END $$;
ALTER ROLE strataai_api_runtime NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD :'api_password';
ALTER ROLE strataai_worker_runtime NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD :'worker_password';
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM strataai_api_runtime,strataai_worker_runtime;
GRANT USAGE ON SCHEMA public TO strataai_api_runtime,strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.runtime_database_role_is_safe() TO strataai_api_runtime,strataai_worker_runtime;
GRANT SELECT,INSERT,UPDATE ON users,sessions,password_reset_tokens,email_verification_tokens TO strataai_api_runtime;
GRANT INSERT ON audit_events,identity_delivery_jobs TO strataai_api_runtime;
GRANT SELECT,INSERT ON background_jobs TO strataai_api_runtime;
GRANT SELECT,UPDATE ON background_jobs TO strataai_worker_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON organizations,boards,board_lists,cards,organization_members,invitations,portal_access,
    user_organization_access,invitation_routes,board_members,user_board_preferences,board_routes,list_routes,card_routes TO strataai_api_runtime;
COMMIT;
