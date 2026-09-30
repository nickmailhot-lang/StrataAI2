BEGIN;

ALTER TABLE boards
    ADD COLUMN description text,
    ADD COLUMN visibility text NOT NULL DEFAULT 'PRIVATE'
        CHECK (visibility IN ('PRIVATE', 'ORGANIZATION', 'PUBLIC')),
    ADD COLUMN background_type text NOT NULL DEFAULT 'COLOR'
        CHECK (background_type IN ('COLOR', 'IMAGE')),
    ADD COLUMN background_value text,
    ADD COLUMN lifecycle_state text NOT NULL DEFAULT 'ACTIVE'
        CHECK (lifecycle_state IN ('ACTIVE', 'ARCHIVED', 'DELETED')),
    ADD COLUMN archived_at timestamptz,
    ADD COLUMN deleted_at timestamptz;

ALTER TABLE board_lists
    ADD COLUMN lifecycle_state text NOT NULL DEFAULT 'ACTIVE'
        CHECK (lifecycle_state IN ('ACTIVE', 'ARCHIVED', 'DELETED')),
    ADD COLUMN archived_at timestamptz,
    ADD COLUMN deleted_at timestamptz;

ALTER TABLE cards
    ADD COLUMN description text,
    ADD COLUMN lifecycle_state text NOT NULL DEFAULT 'ACTIVE'
        CHECK (lifecycle_state IN ('ACTIVE', 'ARCHIVED', 'DELETED')),
    ADD COLUMN archived_at timestamptz,
    ADD COLUMN deleted_at timestamptz;

CREATE TABLE board_members (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    board_id uuid NOT NULL,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    role text NOT NULL CHECK (role IN ('ADMIN', 'MEMBER')),
    status text NOT NULL DEFAULT 'ACTIVE'
        CHECK (status IN ('ACTIVE', 'REMOVED')),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    UNIQUE (board_id, user_id),
    FOREIGN KEY (board_id, tenant_id)
        REFERENCES boards(id, tenant_id)
        ON DELETE RESTRICT
);

CREATE INDEX ix_board_members_user
    ON board_members(user_id, tenant_id, board_id)
    WHERE status = 'ACTIVE';

CREATE TABLE user_board_preferences (
    tenant_id uuid NOT NULL,
    board_id uuid NOT NULL,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    starred boolean NOT NULL DEFAULT false,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (board_id, user_id),
    FOREIGN KEY (board_id, tenant_id)
        REFERENCES boards(id, tenant_id)
        ON DELETE CASCADE
);

CREATE TABLE board_routes (
    board_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    visibility text NOT NULL,
    lifecycle_state text NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE TABLE list_routes (
    list_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    board_id uuid NOT NULL,
    lifecycle_state text NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE TABLE card_routes (
    card_id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    board_id uuid NOT NULL,
    list_id uuid NOT NULL,
    lifecycle_state text NOT NULL,
    updated_at timestamptz NOT NULL
);

CREATE OR REPLACE FUNCTION sync_board_route()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM board_routes WHERE board_id = OLD.id;
        RETURN OLD;
    END IF;

    INSERT INTO board_routes(
        board_id, tenant_id, visibility, lifecycle_state, updated_at)
    VALUES (
        NEW.id, NEW.tenant_id, NEW.visibility, NEW.lifecycle_state, NEW.updated_at)
    ON CONFLICT (board_id)
    DO UPDATE SET
        tenant_id = EXCLUDED.tenant_id,
        visibility = EXCLUDED.visibility,
        lifecycle_state = EXCLUDED.lifecycle_state,
        updated_at = EXCLUDED.updated_at;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION sync_list_route()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM list_routes WHERE list_id = OLD.id;
        RETURN OLD;
    END IF;

    INSERT INTO list_routes(
        list_id, tenant_id, board_id, lifecycle_state, updated_at)
    VALUES (
        NEW.id, NEW.tenant_id, NEW.board_id, NEW.lifecycle_state, NEW.updated_at)
    ON CONFLICT (list_id)
    DO UPDATE SET
        tenant_id = EXCLUDED.tenant_id,
        board_id = EXCLUDED.board_id,
        lifecycle_state = EXCLUDED.lifecycle_state,
        updated_at = EXCLUDED.updated_at;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION sync_card_route()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_OP = 'DELETE' THEN
        DELETE FROM card_routes WHERE card_id = OLD.id;
        RETURN OLD;
    END IF;

    INSERT INTO card_routes(
        card_id, tenant_id, board_id, list_id, lifecycle_state, updated_at)
    VALUES (
        NEW.id, NEW.tenant_id, NEW.board_id, NEW.list_id,
        NEW.lifecycle_state, NEW.updated_at)
    ON CONFLICT (card_id)
    DO UPDATE SET
        tenant_id = EXCLUDED.tenant_id,
        board_id = EXCLUDED.board_id,
        list_id = EXCLUDED.list_id,
        lifecycle_state = EXCLUDED.lifecycle_state,
        updated_at = EXCLUDED.updated_at;

    RETURN NEW;
END;
$$;

CREATE TRIGGER boards_sync_route
AFTER INSERT OR UPDATE OR DELETE ON boards
FOR EACH ROW EXECUTE FUNCTION sync_board_route();

CREATE TRIGGER board_lists_sync_route
AFTER INSERT OR UPDATE OR DELETE ON board_lists
FOR EACH ROW EXECUTE FUNCTION sync_list_route();

CREATE TRIGGER cards_sync_route
AFTER INSERT OR UPDATE OR DELETE ON cards
FOR EACH ROW EXECUTE FUNCTION sync_card_route();

INSERT INTO board_routes(
    board_id, tenant_id, visibility, lifecycle_state, updated_at)
SELECT id, tenant_id, visibility, lifecycle_state, updated_at
FROM boards
ON CONFLICT (board_id) DO NOTHING;

INSERT INTO list_routes(
    list_id, tenant_id, board_id, lifecycle_state, updated_at)
SELECT id, tenant_id, board_id, lifecycle_state, updated_at
FROM board_lists
ON CONFLICT (list_id) DO NOTHING;

INSERT INTO card_routes(
    card_id, tenant_id, board_id, list_id, lifecycle_state, updated_at)
SELECT id, tenant_id, board_id, list_id, lifecycle_state, updated_at
FROM cards
ON CONFLICT (card_id) DO NOTHING;

ALTER TABLE board_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE board_members FORCE ROW LEVEL SECURITY;
ALTER TABLE user_board_preferences ENABLE ROW LEVEL SECURITY;
ALTER TABLE user_board_preferences FORCE ROW LEVEL SECURITY;

CREATE POLICY board_members_tenant_isolation ON board_members
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

CREATE POLICY user_board_preferences_tenant_isolation ON user_board_preferences
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

CREATE INDEX ix_boards_tenant_lifecycle
    ON boards(tenant_id, lifecycle_state, name);

CREATE INDEX ix_board_lists_active_rank
    ON board_lists(tenant_id, board_id, rank)
    WHERE lifecycle_state = 'ACTIVE';

CREATE INDEX ix_cards_active_rank
    ON cards(tenant_id, board_id, list_id, rank)
    WHERE lifecycle_state = 'ACTIVE';

INSERT INTO schema_migrations(version)
VALUES ('006_work_management')
ON CONFLICT (version) DO NOTHING;

COMMIT;
