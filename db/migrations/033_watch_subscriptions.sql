BEGIN;
-- Entity identities, rather than a Card's current List/Board, keep a direct
-- watch stable across movement. Composite keys still enforce tenant isolation.
ALTER TABLE cards ADD CONSTRAINT uq_cards_tenant_identity UNIQUE(id,tenant_id);
ALTER TABLE board_lists ADD CONSTRAINT uq_lists_tenant_identity UNIQUE(id,tenant_id);
CREATE TABLE watch_subscriptions (
    tenant_id uuid NOT NULL,
    id uuid NOT NULL,
    user_id uuid NOT NULL,
    entity_type text NOT NULL CHECK(entity_type IN ('CARD','LIST','BOARD')),
    entity_id uuid NOT NULL,
    board_id uuid,
    list_id uuid,
    card_id uuid,
    watching boolean NOT NULL,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL CHECK(updated_at>=created_at),
    version bigint NOT NULL CHECK(version>0),
    PRIMARY KEY(tenant_id,id),
    UNIQUE(tenant_id,user_id,entity_type,entity_id),
    CHECK ((entity_type='BOARD' AND board_id IS NOT NULL AND board_id=entity_id AND list_id IS NULL AND card_id IS NULL)
        OR (entity_type='LIST' AND list_id IS NOT NULL AND list_id=entity_id AND board_id IS NULL AND card_id IS NULL)
        OR (entity_type='CARD' AND card_id IS NOT NULL AND card_id=entity_id AND board_id IS NULL AND list_id IS NULL)),
    FOREIGN KEY(tenant_id,user_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT,
    FOREIGN KEY(board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT,
    FOREIGN KEY(list_id,tenant_id) REFERENCES board_lists(id,tenant_id) ON DELETE RESTRICT,
    FOREIGN KEY(card_id,tenant_id) REFERENCES cards(id,tenant_id) ON DELETE RESTRICT
);
CREATE INDEX ix_watch_subscriptions_active_entity ON watch_subscriptions(tenant_id,entity_type,entity_id,user_id) WHERE watching;
ALTER TABLE watch_subscriptions ENABLE ROW LEVEL SECURITY;
ALTER TABLE watch_subscriptions FORCE ROW LEVEL SECURITY;
CREATE POLICY watch_subscriptions_tenant_isolation ON watch_subscriptions
    USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
    WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
INSERT INTO schema_migrations(version) VALUES('033_watch_subscriptions');
COMMIT;
