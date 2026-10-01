BEGIN;
-- Account changes have a global subject, not a fabricated Organization/Board.
CREATE TABLE identity_event_streams (
    user_id uuid PRIMARY KEY REFERENCES users(id) ON DELETE RESTRICT,
    last_sequence bigint NOT NULL DEFAULT 0 CHECK (last_sequence >= 0),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp()
);
CREATE TABLE identity_events (
    event_id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES identity_event_streams(user_id) ON DELETE RESTRICT,
    sequence bigint NOT NULL CHECK (sequence > 0),
    actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    event_type text NOT NULL CHECK (event_type IN (
        'USER_REGISTERED','USER_PROFILE_UPDATED','USER_DEACTIVATED','SESSION_REVOKED','EMAIL_VERIFIED')),
    organization_id uuid CHECK (organization_id IS NULL),
    board_id uuid CHECK (board_id IS NULL),
    entity_type text NOT NULL DEFAULT 'User' CHECK (entity_type = 'User'),
    entity_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT CHECK (entity_id = user_id),
    entity_version bigint NOT NULL CHECK (entity_version > 0),
    metadata jsonb NOT NULL DEFAULT '{}'::jsonb CHECK (metadata = '{}'::jsonb),
    correlation_id text NOT NULL CHECK (length(btrim(correlation_id)) BETWEEN 1 AND 120),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (user_id, sequence)
);
ALTER TABLE identity_event_streams ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_event_streams FORCE ROW LEVEL SECURITY;
ALTER TABLE identity_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_events FORCE ROW LEVEL SECURITY;
CREATE POLICY identity_stream_subject ON identity_event_streams
    USING (user_id = NULLIF(current_setting('app.identity_subject',true),'')::uuid)
    WITH CHECK (user_id = NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE POLICY identity_event_subject ON identity_events
    USING (user_id = NULLIF(current_setting('app.identity_subject',true),'')::uuid)
    WITH CHECK (user_id = NULLIF(current_setting('app.identity_subject',true),'')::uuid);
INSERT INTO schema_migrations(version) VALUES ('012_identity_events') ON CONFLICT(version) DO NOTHING;
COMMIT;
