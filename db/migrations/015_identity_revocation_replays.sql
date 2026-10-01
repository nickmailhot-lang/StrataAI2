BEGIN;
-- A receipt acknowledges a completed revocation; it contains no credential or profile.
ALTER TABLE sessions ADD CONSTRAINT sessions_id_user_unique UNIQUE(id,user_id);
CREATE TABLE identity_revocation_replays (
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    key_id uuid NOT NULL CHECK (key_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    session_id uuid NOT NULL,
    operation text NOT NULL CHECK(operation IN ('LOGOUT','DEACTIVATE')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    PRIMARY KEY(user_id,key_id),
    FOREIGN KEY(session_id,user_id) REFERENCES sessions(id,user_id) ON DELETE RESTRICT,
    CHECK(expires_at>created_at)
);
CREATE INDEX identity_revocation_replay_expiry ON identity_revocation_replays(expires_at);
ALTER TABLE identity_revocation_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_revocation_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY identity_revocation_subject ON identity_revocation_replays
    USING(user_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
    WITH CHECK(user_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE POLICY identity_revocation_read_limit ON identity_revocation_replays AS RESTRICTIVE FOR SELECT
    USING(has_column_privilege(current_user,'public.identity_revocation_replays','session_id','SELECT')
        OR (current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp()));
CREATE POLICY identity_revocation_cleanup_read ON identity_revocation_replays FOR SELECT
    USING(NOT has_column_privilege(current_user,'public.identity_revocation_replays','session_id','SELECT')
        AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE POLICY identity_revocation_delete_limit ON identity_revocation_replays AS RESTRICTIVE FOR DELETE
    USING(expires_at<=clock_timestamp() AND
        (has_column_privilege(current_user,'public.identity_revocation_replays','session_id','SELECT')
         OR current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP'));
CREATE POLICY identity_revocation_cleanup_delete ON identity_revocation_replays FOR DELETE
    USING(NOT has_column_privilege(current_user,'public.identity_revocation_replays','session_id','SELECT')
        AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE FUNCTION public.purge_expired_identity_revocation_replays()
RETURNS integer LANGUAGE sql SECURITY INVOKER SET search_path=pg_catalog AS $$
    WITH candidates AS MATERIALIZED (
        SELECT user_id,key_id FROM public.identity_revocation_replays
        WHERE expires_at<=clock_timestamp()
        ORDER BY expires_at,user_id,key_id LIMIT 100
    ), removed AS (
        DELETE FROM public.identity_revocation_replays r USING candidates c
        WHERE r.user_id=c.user_id AND r.key_id=c.key_id AND r.expires_at<=clock_timestamp()
        RETURNING 1
    ) SELECT count(*)::integer FROM removed;
$$;
REVOKE ALL ON FUNCTION public.purge_expired_identity_revocation_replays() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES ('015_identity_revocation_replays') ON CONFLICT(version) DO NOTHING;
COMMIT;
