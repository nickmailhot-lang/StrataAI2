BEGIN;
-- Global account-owned acknowledgments. No handle/profile/credential text.
CREATE TABLE identity_handle_claim_replays (
 user_id uuid NOT NULL REFERENCES user_mention_handles(user_id) ON DELETE RESTRICT,
 key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'),
 fingerprint text COLLATE "C" NOT NULL CHECK(length(fingerprint)=64 AND fingerprint ~ '^[0-9a-f]{64}$'),
 user_version bigint NOT NULL CHECK(user_version>0),
 handle_version bigint NOT NULL CHECK(handle_version>0),
 changed boolean NOT NULL,
 created_at timestamptz NOT NULL DEFAULT statement_timestamp(),
 updated_at timestamptz NOT NULL DEFAULT statement_timestamp(),
 expires_at timestamptz NOT NULL DEFAULT statement_timestamp()+interval '24 hours',
 PRIMARY KEY(user_id,key_id),
 CHECK(isfinite(created_at) AND isfinite(updated_at) AND isfinite(expires_at)
  AND updated_at=created_at AND expires_at=created_at+interval '24 hours')
);
CREATE INDEX ix_identity_handle_claim_replay_expiry ON identity_handle_claim_replays(expires_at,user_id,key_id);
ALTER TABLE identity_handle_claim_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_handle_claim_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY identity_handle_claim_subject_read ON identity_handle_claim_replays FOR SELECT
 USING(user_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE POLICY identity_handle_claim_subject_insert ON identity_handle_claim_replays FOR INSERT
 WITH CHECK(user_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
-- Cleanup gets only expired key metadata and cannot spoof a subject to read
-- live acknowledgments or private fingerprints/revisions.
CREATE POLICY identity_handle_claim_read_limit ON identity_handle_claim_replays AS RESTRICTIVE FOR SELECT
 USING(has_column_privilege(current_user,'public.identity_handle_claim_replays','fingerprint','SELECT')
  OR (current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp()));
CREATE POLICY identity_handle_claim_cleanup_read ON identity_handle_claim_replays FOR SELECT
 USING(NOT has_column_privilege(current_user,'public.identity_handle_claim_replays','fingerprint','SELECT')
  AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE POLICY identity_handle_claim_cleanup_delete ON identity_handle_claim_replays FOR DELETE
 USING(NOT has_column_privilege(current_user,'public.identity_handle_claim_replays','fingerprint','SELECT')
  AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_RETRY_CLEANUP' AND expires_at<=clock_timestamp());
CREATE FUNCTION enforce_identity_handle_claim_replay_immutable() RETURNS trigger LANGUAGE plpgsql
 SET search_path=pg_catalog AS $$
BEGIN
 IF NEW IS DISTINCT FROM OLD THEN
  RAISE EXCEPTION 'Handle claim acknowledgment is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION enforce_identity_handle_claim_replay_immutable() FROM PUBLIC;
CREATE TRIGGER identity_handle_claim_replay_immutable BEFORE UPDATE ON identity_handle_claim_replays
 FOR EACH ROW EXECUTE FUNCTION enforce_identity_handle_claim_replay_immutable();
CREATE FUNCTION purge_expired_identity_handle_claim_replays() RETURNS integer LANGUAGE sql SECURITY INVOKER
 SET search_path=pg_catalog AS $$
 WITH candidates AS MATERIALIZED (
  SELECT user_id,key_id FROM public.identity_handle_claim_replays
  WHERE expires_at<=statement_timestamp() ORDER BY expires_at,user_id,key_id LIMIT 100
 ), removed AS (
  DELETE FROM public.identity_handle_claim_replays r USING candidates c
  WHERE r.user_id=c.user_id AND r.key_id=c.key_id AND r.expires_at<=statement_timestamp()
  RETURNING 1
 ) SELECT count(*)::integer FROM removed;
$$;
REVOKE ALL ON FUNCTION purge_expired_identity_handle_claim_replays() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('057_identity_handle_claim_replays');
COMMIT;
