BEGIN;
-- Maintenance sees expired keys and expiry timestamps, never cached profile content.
-- Restrictive policies prevent a maintenance caller spoofing app.identity_subject.
CREATE POLICY identity_retry_read_limit ON identity_profile_replays AS RESTRICTIVE FOR SELECT
    USING (has_column_privilege(current_user,'public.identity_profile_replays','result_json','SELECT')
        OR (current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp()));
CREATE POLICY identity_retry_cleanup_read ON identity_profile_replays FOR SELECT
    USING (NOT has_column_privilege(current_user,'public.identity_profile_replays','result_json','SELECT')
        AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE POLICY identity_retry_delete_limit ON identity_profile_replays AS RESTRICTIVE FOR DELETE
    USING (NOT has_column_privilege(current_user,'public.identity_profile_replays','result_json','SELECT')
        AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE POLICY identity_retry_cleanup_delete ON identity_profile_replays FOR DELETE
    USING (NOT has_column_privilege(current_user,'public.identity_profile_replays','result_json','SELECT')
        AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());

CREATE FUNCTION public.purge_expired_identity_profile_replays()
RETURNS integer LANGUAGE sql SECURITY INVOKER SET search_path=pg_catalog AS $$
    WITH candidates AS MATERIALIZED (
        SELECT user_id,key_id FROM public.identity_profile_replays
        WHERE expires_at<=clock_timestamp()
        ORDER BY expires_at,user_id,key_id LIMIT 100
    ), removed AS (
        DELETE FROM public.identity_profile_replays r USING candidates c
        WHERE r.user_id=c.user_id AND r.key_id=c.key_id AND r.expires_at<=clock_timestamp()
        RETURNING 1
    ) SELECT count(*)::integer FROM removed;
$$;
REVOKE ALL ON FUNCTION public.purge_expired_identity_profile_replays() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES ('014_identity_retry_retention') ON CONFLICT(version) DO NOTHING;
COMMIT;
