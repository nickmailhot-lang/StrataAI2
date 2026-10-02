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
GRANT SELECT ON schema_migrations TO strataai_api_runtime,strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.runtime_database_role_is_safe() TO strataai_api_runtime,strataai_worker_runtime;
GRANT SELECT,INSERT,UPDATE ON users,sessions,password_reset_tokens,email_verification_tokens TO strataai_api_runtime;
GRANT INSERT ON audit_events,identity_delivery_jobs TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON work_command_replays TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON board_labels TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON label_routes TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON card_labels TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON card_members TO strataai_api_runtime;
GRANT SELECT,INSERT ON invitation_creation_replays TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON work_event_streams TO strataai_api_runtime;
GRANT SELECT,INSERT ON work_events TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON identity_event_streams TO strataai_api_runtime;
GRANT SELECT,INSERT ON identity_events TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON identity_profile_replays TO strataai_api_runtime;
GRANT SELECT(user_id,key_id,expires_at),DELETE ON identity_profile_replays TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.purge_expired_identity_profile_replays() TO strataai_worker_runtime;
GRANT SELECT,INSERT,DELETE ON identity_revocation_replays TO strataai_api_runtime;
GRANT SELECT(user_id,key_id,expires_at),DELETE ON identity_revocation_replays TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.purge_expired_identity_revocation_replays() TO strataai_worker_runtime;
GRANT SELECT,INSERT ON identity_login_replays TO strataai_api_runtime;
GRANT SELECT(user_id,key_id,expires_at),DELETE ON identity_login_replays TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.purge_expired_identity_login_replays() TO strataai_worker_runtime;
GRANT SELECT,INSERT ON identity_registration_replays TO strataai_api_runtime;
GRANT SELECT(user_id,key_id,expires_at),DELETE ON identity_registration_replays TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.purge_expired_identity_registration_replays() TO strataai_worker_runtime;
GRANT SELECT,INSERT ON identity_recovery_request_replays TO strataai_api_runtime;
GRANT SELECT(user_id,key_id,operation,expires_at),DELETE ON identity_recovery_request_replays TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.purge_expired_identity_recovery_request_replays() TO strataai_worker_runtime;
GRANT SELECT,INSERT ON identity_token_consumption_replays TO strataai_api_runtime;
GRANT SELECT(user_id,key_id,operation,expires_at),DELETE ON identity_token_consumption_replays TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.purge_expired_identity_token_consumption_replays() TO strataai_worker_runtime;
GRANT SELECT(tenant_id,event_id,board_id,actor_id,ready_at),UPDATE(ready_at) ON work_events TO strataai_worker_runtime;
GRANT SELECT,INSERT ON background_jobs TO strataai_api_runtime;
GRANT SELECT,UPDATE ON background_jobs TO strataai_worker_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON organizations,boards,board_lists,cards,organization_members,invitations,portal_access,
    user_organization_access,invitation_routes,board_members,user_board_preferences,board_routes,list_routes,card_routes TO strataai_api_runtime;
GRANT SELECT,INSERT ON invitation_mail_intents TO strataai_api_runtime;
GRANT EXECUTE ON FUNCTION public.load_invitation_mail(uuid,uuid,uuid,uuid,uuid,boolean), public.finish_invitation_mail(uuid,uuid,uuid,uuid,uuid,text,text,uuid) TO strataai_worker_runtime;
COMMIT;
