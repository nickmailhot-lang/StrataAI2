BEGIN;

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS schema_migrations (
    version text PRIMARY KEY,
    applied_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE organizations (
    id uuid PRIMARY KEY,
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0)
);

CREATE TABLE boards (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    UNIQUE (id, tenant_id)
);

CREATE TABLE board_lists (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    board_id uuid NOT NULL,
    name text NOT NULL CHECK (length(btrim(name)) > 0),
    rank text NOT NULL CHECK (length(rank) > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    UNIQUE (id, board_id, tenant_id),
    FOREIGN KEY (board_id, tenant_id)
        REFERENCES boards(id, tenant_id)
        ON DELETE RESTRICT
);

CREATE TABLE cards (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    board_id uuid NOT NULL,
    list_id uuid NOT NULL,
    title text NOT NULL CHECK (length(btrim(title)) > 0),
    rank text NOT NULL CHECK (length(rank) > 0),
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    FOREIGN KEY (board_id, tenant_id)
        REFERENCES boards(id, tenant_id)
        ON DELETE RESTRICT,
    FOREIGN KEY (list_id, board_id, tenant_id)
        REFERENCES board_lists(id, board_id, tenant_id)
        ON DELETE RESTRICT
);

CREATE INDEX ix_boards_tenant_id ON boards(tenant_id);
CREATE INDEX ix_board_lists_tenant_board_rank
    ON board_lists(tenant_id, board_id, rank);
CREATE INDEX ix_cards_tenant_board_list_rank
    ON cards(tenant_id, board_id, list_id, rank);

ALTER TABLE organizations ENABLE ROW LEVEL SECURITY;
ALTER TABLE organizations FORCE ROW LEVEL SECURITY;
ALTER TABLE boards ENABLE ROW LEVEL SECURITY;
ALTER TABLE boards FORCE ROW LEVEL SECURITY;
ALTER TABLE board_lists ENABLE ROW LEVEL SECURITY;
ALTER TABLE board_lists FORCE ROW LEVEL SECURITY;
ALTER TABLE cards ENABLE ROW LEVEL SECURITY;
ALTER TABLE cards FORCE ROW LEVEL SECURITY;

CREATE POLICY organizations_tenant_isolation ON organizations
    USING (
        id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

CREATE POLICY boards_tenant_isolation ON boards
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

CREATE POLICY board_lists_tenant_isolation ON board_lists
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

CREATE POLICY cards_tenant_isolation ON cards
    USING (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    )
    WITH CHECK (
        tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
    );

INSERT INTO schema_migrations(version)
VALUES ('001_foundation')
ON CONFLICT (version) DO NOTHING;

COMMIT;
