BEGIN;
-- Global authenticated profile acknowledgments only. No credential/session/token outcomes.
CREATE TABLE identity_profile_replays (
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    key_id uuid NOT NULL CHECK (key_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    fingerprint text NOT NULL CHECK (fingerprint ~ '^[0-9a-f]{64}$'),
    result_json jsonb NOT NULL CHECK (jsonb_typeof(result_json)='object'
        AND jsonb_typeof(result_json->'Id')='string' AND result_json->>'Id'=user_id::text
        AND jsonb_typeof(result_json->'Version')='number' AND (result_json->>'Version')::bigint>0
        AND result_json ?& ARRAY['Id','Email','DisplayName','AvatarUrl','Locale','Timezone','Status','EmailVerified','CreatedAt','UpdatedAt','Version']
        AND result_json - ARRAY['Id','Email','DisplayName','AvatarUrl','Locale','Timezone','Status','EmailVerified','CreatedAt','UpdatedAt','Version'] = '{}'::jsonb),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL DEFAULT clock_timestamp()+interval '24 hours',
    PRIMARY KEY(user_id,key_id),
    CHECK (expires_at>created_at)
);
CREATE INDEX identity_profile_replay_expiry ON identity_profile_replays(expires_at);
ALTER TABLE identity_profile_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_profile_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY identity_profile_replay_subject ON identity_profile_replays
    USING (user_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
    WITH CHECK (user_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
INSERT INTO schema_migrations(version) VALUES ('013_identity_profile_replays') ON CONFLICT(version) DO NOTHING;
COMMIT;
