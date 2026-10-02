BEGIN;

-- Composite keys enforce the same Board and Organization for Card labels.
ALTER TABLE cards ADD CONSTRAINT uq_cards_board_tenant UNIQUE (id, board_id, tenant_id);

CREATE TABLE board_labels (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL,
    board_id uuid NOT NULL,
    name text NOT NULL DEFAULT '' CHECK (length(name) <= 160),
    color text NOT NULL CHECK (color IN ('green','yellow','orange','red','purple','blue','sky','lime','pink','black')),
    rank text NOT NULL CHECK (rank ~ '^[0-9]{30}$'),
    status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','DELETED')),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    deleted_at timestamptz,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    UNIQUE (id, board_id, tenant_id),
    FOREIGN KEY (board_id, tenant_id) REFERENCES boards(id, tenant_id) ON DELETE RESTRICT,
    CHECK ((status = 'DELETED') = (deleted_at IS NOT NULL))
);
CREATE INDEX ix_board_labels_active_rank ON board_labels(tenant_id, board_id, rank, id) WHERE status='ACTIVE';

CREATE TABLE card_labels (
    tenant_id uuid NOT NULL,
    board_id uuid NOT NULL,
    card_id uuid NOT NULL,
    label_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    PRIMARY KEY (tenant_id, card_id, label_id),
    FOREIGN KEY (card_id, board_id, tenant_id) REFERENCES cards(id, board_id, tenant_id) ON DELETE RESTRICT,
    FOREIGN KEY (label_id, board_id, tenant_id) REFERENCES board_labels(id, board_id, tenant_id) ON DELETE RESTRICT
);
CREATE INDEX ix_card_labels_board_label ON card_labels(tenant_id, board_id, label_id, card_id);

ALTER TABLE board_labels ENABLE ROW LEVEL SECURITY;
ALTER TABLE board_labels FORCE ROW LEVEL SECURITY;
CREATE POLICY board_labels_tenant_isolation ON board_labels
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
ALTER TABLE card_labels ENABLE ROW LEVEL SECURITY;
ALTER TABLE card_labels FORCE ROW LEVEL SECURITY;
CREATE POLICY card_labels_tenant_isolation ON card_labels
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

INSERT INTO schema_migrations(version) VALUES ('028_board_labels');
COMMIT;
