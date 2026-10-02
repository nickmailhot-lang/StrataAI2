BEGIN;

-- Recipient data stays separate from content-free Board replay envelopes.
ALTER TABLE work_events ADD CONSTRAINT uq_work_events_board_event UNIQUE (tenant_id,board_id,event_id);
CREATE TABLE card_assignment_notifications (
    tenant_id uuid NOT NULL,
    id uuid NOT NULL,
    board_id uuid NOT NULL,
    card_id uuid NOT NULL,
    event_id uuid NOT NULL,
    recipient_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    notification_type text NOT NULL DEFAULT 'CARD_ASSIGNED' CHECK (notification_type='CARD_ASSIGNED'),
    card_version bigint NOT NULL CHECK (card_version > 0),
    created_at timestamptz NOT NULL,
    read_at timestamptz CHECK (read_at IS NULL OR read_at >= created_at),
    PRIMARY KEY (tenant_id,id),
    UNIQUE (tenant_id,event_id,recipient_id),
    CHECK (actor_id <> recipient_id),
    FOREIGN KEY (tenant_id,board_id,event_id) REFERENCES work_events(tenant_id,board_id,event_id) ON DELETE RESTRICT,
    FOREIGN KEY (card_id,board_id,tenant_id) REFERENCES cards(id,board_id,tenant_id) ON DELETE RESTRICT,
    FOREIGN KEY (tenant_id,recipient_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT,
    FOREIGN KEY (tenant_id,actor_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT
);
CREATE INDEX ix_card_assignment_notifications_recipient ON card_assignment_notifications(tenant_id,recipient_id,id);
ALTER TABLE card_assignment_notifications ENABLE ROW LEVEL SECURITY;
ALTER TABLE card_assignment_notifications FORCE ROW LEVEL SECURITY;
CREATE POLICY card_assignment_notifications_tenant_isolation ON card_assignment_notifications
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

INSERT INTO schema_migrations(version) VALUES ('031_card_assignment_notifications');
COMMIT;
