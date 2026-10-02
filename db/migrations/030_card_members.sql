BEGIN;

-- Membership rows survive departures so attribution and existing references
-- stay stable. Commands enforce current eligibility, version and lifecycle.
ALTER TABLE board_members ADD CONSTRAINT uq_board_members_scope_user UNIQUE (board_id, tenant_id, user_id);
CREATE TABLE card_members (
    tenant_id uuid NOT NULL,
    board_id uuid NOT NULL,
    card_id uuid NOT NULL,
    user_id uuid NOT NULL,
    assigned_by uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    PRIMARY KEY (tenant_id, card_id, user_id),
    FOREIGN KEY (card_id, board_id, tenant_id) REFERENCES cards(id, board_id, tenant_id) ON DELETE RESTRICT,
    FOREIGN KEY (board_id, tenant_id, user_id) REFERENCES board_members(board_id, tenant_id, user_id) ON DELETE RESTRICT,
    FOREIGN KEY (tenant_id, user_id) REFERENCES organization_members(tenant_id, user_id) ON DELETE RESTRICT,
    FOREIGN KEY (tenant_id, assigned_by) REFERENCES organization_members(tenant_id, user_id) ON DELETE RESTRICT
);
CREATE INDEX ix_card_members_board_user ON card_members(tenant_id, board_id, user_id, card_id);
ALTER TABLE card_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE card_members FORCE ROW LEVEL SECURITY;
CREATE POLICY card_members_tenant_isolation ON card_members
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

INSERT INTO schema_migrations(version) VALUES ('030_card_members');
COMMIT;
