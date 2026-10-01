BEGIN;
CREATE TABLE identity_login_replays (
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
    session_id uuid NOT NULL,
    key_version text NOT NULL CHECK(key_version ~ '^[A-Za-z0-9_-]{1,32}$'),
    fingerprint text NOT NULL CHECK(fingerprint ~ '^[0-9a-f]{64}$'),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    PRIMARY KEY(user_id,key_id),
    FOREIGN KEY(session_id,user_id) REFERENCES sessions(id,user_id) ON DELETE RESTRICT,
    CHECK(expires_at>created_at AND expires_at<=created_at+interval '24 hours')
);
CREATE INDEX identity_login_replay_expiry ON identity_login_replays(expires_at);
ALTER TABLE identity_login_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_login_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY identity_login_subject ON identity_login_replays
    USING(user_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
    WITH CHECK(user_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE POLICY identity_login_read_limit ON identity_login_replays AS RESTRICTIVE FOR SELECT
    USING(has_column_privilege(current_user,'public.identity_login_replays','fingerprint','SELECT')
      OR (current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp()));
CREATE POLICY identity_login_cleanup_read ON identity_login_replays FOR SELECT
    USING(NOT has_column_privilege(current_user,'public.identity_login_replays','fingerprint','SELECT')
      AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE POLICY identity_login_delete_limit ON identity_login_replays AS RESTRICTIVE FOR DELETE
    USING(NOT has_column_privilege(current_user,'public.identity_login_replays','fingerprint','SELECT')
      AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE POLICY identity_login_cleanup_delete ON identity_login_replays FOR DELETE
    USING(NOT has_column_privilege(current_user,'public.identity_login_replays','fingerprint','SELECT')
      AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE FUNCTION public.purge_expired_identity_login_replays()
RETURNS integer LANGUAGE sql SECURITY INVOKER SET search_path=pg_catalog AS $$
    WITH candidates AS MATERIALIZED (
      SELECT user_id,key_id FROM public.identity_login_replays WHERE expires_at<=clock_timestamp()
      ORDER BY expires_at,user_id,key_id LIMIT 100
    ), removed AS (
      DELETE FROM public.identity_login_replays r USING candidates c
      WHERE r.user_id=c.user_id AND r.key_id=c.key_id AND r.expires_at<=clock_timestamp() RETURNING 1
    ) SELECT count(*)::integer FROM removed;
$$;
REVOKE ALL ON FUNCTION public.purge_expired_identity_login_replays() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('017_identity_login_replays') ON CONFLICT(version) DO NOTHING;
COMMIT;
