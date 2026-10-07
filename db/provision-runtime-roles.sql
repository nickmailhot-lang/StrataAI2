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
ALTER ROLE strataai_api_runtime LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD :'api_password';
ALTER ROLE strataai_worker_runtime LOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD :'worker_password';
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM strataai_api_runtime,strataai_worker_runtime;
GRANT USAGE ON SCHEMA public TO strataai_api_runtime,strataai_worker_runtime;
GRANT SELECT ON schema_migrations TO strataai_api_runtime,strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION append_or_replay_navigation_interaction(uuid,text,uuid,uuid,text,uuid,uuid,uuid,bigint,timestamptz) TO strataai_api_runtime;
GRANT SELECT ON navigation_interaction_events TO strataai_api_runtime;
GRANT EXECUTE ON FUNCTION append_navigation_interaction(uuid,uuid,text,uuid,uuid,uuid,bigint,timestamptz) TO strataai_api_runtime;
GRANT SELECT ON search_interaction_streams,search_interaction_events TO strataai_api_runtime;
GRANT EXECUTE ON FUNCTION append_search_interaction(uuid,uuid,text,uuid,uuid,timestamptz) TO strataai_api_runtime;
GRANT EXECUTE ON FUNCTION append_or_replay_board_filter_interaction(uuid,text,uuid,uuid,uuid,uuid,timestamptz) TO strataai_api_runtime;
GRANT SELECT ON attachment_previews,attachment_preview_publications TO strataai_api_runtime;
GRANT SELECT,INSERT ON board_background_images TO strataai_api_runtime;
REVOKE UPDATE,DELETE ON board_background_images FROM strataai_api_runtime;
GRANT EXECUTE ON FUNCTION public.runtime_database_role_is_safe() TO strataai_api_runtime,strataai_worker_runtime;
GRANT SELECT,INSERT,UPDATE ON users,sessions,password_reset_tokens,email_verification_tokens TO strataai_api_runtime;
GRANT INSERT ON audit_events,identity_delivery_jobs TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON work_command_replays TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON board_labels TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON label_routes TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON card_labels TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON card_members TO strataai_api_runtime;
GRANT SELECT,INSERT ON card_assignment_notifications TO strataai_api_runtime;
GRANT SELECT ON notification_events,notification_event_streams TO strataai_api_runtime;
GRANT SELECT ON board_star_events TO strataai_api_runtime;
GRANT SELECT ON organization_board_events,organization_board_event_streams TO strataai_api_runtime;
GRANT SELECT ON organization_metadata_events,organization_metadata_event_streams TO strataai_api_runtime;
-- Recipient routing exposes only invalidation identity/order, never domain
-- references, email content, tokens, names or target grants.
GRANT SELECT(email_normalized,last_sequence) ON invitation_recipient_streams TO strataai_api_runtime;
GRANT SELECT(email_normalized,sequence,event_id,event_type,created_at) ON invitation_recipient_events TO strataai_api_runtime;
GRANT SELECT(email_normalized,revision) ON invitation_recipient_authority_revisions TO strataai_api_runtime;
GRANT EXECUTE ON FUNCTION public.deliver_invitation_recipient_authority(uuid,uuid,uuid,uuid,uuid,uuid,integer) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.discover_invitation_recipient_authority_scopes(uuid,integer) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.claim_invitation_recipient_authority_job(uuid) TO strataai_worker_runtime;
GRANT SELECT ON organization_board_directory_epochs TO strataai_api_runtime;
GRANT UPDATE(read_at) ON card_assignment_notifications TO strataai_api_runtime;
GRANT SELECT,INSERT ON watch_subscriptions TO strataai_api_runtime;
GRANT SELECT,INSERT ON card_reminders TO strataai_api_runtime;
GRANT UPDATE(interval_code,enabled,due_at,trigger_at,status,generation,updated_at,version) ON card_reminders TO strataai_api_runtime;
GRANT UPDATE(watching,updated_at,version) ON watch_subscriptions TO strataai_api_runtime;
GRANT SELECT,INSERT ON invitation_creation_replays TO strataai_api_runtime;
GRANT SELECT,INSERT ON organization_metadata_replays TO strataai_api_runtime;
GRANT SELECT,INSERT ON organization_creation_replays TO strataai_api_runtime;
GRANT SELECT,INSERT ON organization_deletion_replays TO strataai_api_runtime;
GRANT SELECT,INSERT ON organization_deletion_requests,organization_deletion_progress TO strataai_api_runtime;
GRANT SELECT(tenant_id,request_id,actor_id,accepted_version) ON organization_deletion_requests TO strataai_worker_runtime;
GRANT SELECT ON organization_deletion_progress TO strataai_worker_runtime;
-- Only the graph-proving terminal capability is enabled; no direct graph writes.
GRANT EXECUTE ON FUNCTION public.finish_organization_deletion(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.deliver_organization_lifecycle_event(uuid,uuid,uuid,uuid,uuid,uuid) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.deliver_organization_metadata_event(uuid,uuid,uuid,uuid,uuid,uuid) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.discover_organization_deletion_scopes(uuid,integer) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.discover_organization_metadata_scopes(uuid,integer) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.claim_organization_metadata_job(uuid) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.load_organization_deletion_page(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,integer) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.apply_organization_deletion_page(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,integer) TO strataai_worker_runtime;
GRANT SELECT ON organization_deletion_steps TO strataai_worker_runtime;
GRANT SELECT ON organization_lifecycle_events TO strataai_api_runtime;
GRANT SELECT(tenant_id,event_id,actor_id,entity_version,created_at,ready_at) ON organization_lifecycle_events TO strataai_worker_runtime;
GRANT SELECT,INSERT ON organization_departure_replays TO strataai_api_runtime;
GRANT SELECT,INSERT ON organization_removal_replays TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON work_event_streams TO strataai_api_runtime;
GRANT SELECT,INSERT ON work_events TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON identity_event_streams TO strataai_api_runtime;
GRANT SELECT,INSERT ON identity_events TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON identity_profile_replays TO strataai_api_runtime;
GRANT SELECT,INSERT ON identity_handle_claim_replays TO strataai_api_runtime;
GRANT SELECT(user_id,key_id,expires_at),DELETE ON identity_handle_claim_replays TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.purge_expired_identity_handle_claim_replays() TO strataai_worker_runtime;
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
GRANT EXECUTE ON FUNCTION public.deliver_card_reminder(uuid,uuid,uuid,uuid,uuid,uuid,bigint,boolean) TO strataai_worker_runtime;
GRANT SELECT,INSERT,UPDATE,DELETE ON organizations,boards,board_lists,cards,organization_members,invitations,portal_access,
    user_organization_access,invitation_routes,board_members,user_board_preferences,board_routes,list_routes,card_routes TO strataai_api_runtime;
REVOKE DELETE ON user_board_preferences FROM strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON checklists,checklist_items TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON attachments TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON card_comments TO strataai_api_runtime;
GRANT SELECT,INSERT ON comment_mention_snapshots,comment_mention_recipients TO strataai_api_runtime;
GRANT SELECT,INSERT ON mass_mention_reservations TO strataai_api_runtime;
GRANT SELECT ON user_mention_handles TO strataai_api_runtime;
GRANT UPDATE(handle,updated_at,version) ON user_mention_handles TO strataai_api_runtime;
GRANT SELECT,INSERT,UPDATE ON attachment_upload_intents TO strataai_api_runtime;
GRANT EXECUTE ON FUNCTION public.load_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint),
 public.finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.load_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint),
 public.declare_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,bigint,text,integer,integer) TO strataai_worker_runtime;
REVOKE ALL ON FUNCTION public.load_attachment_preview_source(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) FROM strataai_api_runtime,strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.finish_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,integer,integer) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,boolean) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.enqueue_attachment_preview_backfill(uuid,integer) TO strataai_worker_runtime;
GRANT EXECUTE ON FUNCTION public.recover_attachment_scan_page(uuid,integer) TO strataai_worker_runtime;
GRANT SELECT,INSERT ON invitation_mail_intents TO strataai_api_runtime;
GRANT EXECUTE ON FUNCTION public.load_invitation_mail(uuid,uuid,uuid,uuid,uuid,boolean), public.finish_invitation_mail(uuid,uuid,uuid,uuid,uuid,text,text,uuid) TO strataai_worker_runtime;
COMMIT;
