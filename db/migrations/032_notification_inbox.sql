BEGIN;
CREATE INDEX ix_card_assignment_notifications_inbox
    ON card_assignment_notifications(tenant_id,recipient_id,created_at DESC,id DESC);
INSERT INTO schema_migrations(version) VALUES ('032_notification_inbox');
COMMIT;
