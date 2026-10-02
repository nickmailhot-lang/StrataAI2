BEGIN;
ALTER TABLE cards ADD COLUMN start_at timestamptz, ADD COLUMN due_at timestamptz,
 ADD COLUMN due_timezone text, ADD COLUMN due_has_time boolean NOT NULL DEFAULT false,
 ADD COLUMN due_complete boolean NOT NULL DEFAULT false;
ALTER TABLE cards ADD CONSTRAINT card_date_context_check CHECK (
 ((start_at IS NOT NULL OR due_at IS NOT NULL) AND due_timezone IS NOT NULL
   AND length(due_timezone) BETWEEN 1 AND 100 AND due_timezone ~ '^[A-Za-z0-9_+./-]+$')
 OR (start_at IS NULL AND due_at IS NULL AND due_timezone IS NULL));
ALTER TABLE cards ADD CONSTRAINT card_due_flags_check CHECK (due_at IS NOT NULL OR (NOT due_has_time AND NOT due_complete));
ALTER TABLE cards ADD CONSTRAINT card_date_order_check CHECK (start_at IS NULL OR due_at IS NULL OR start_at <= due_at);
ALTER TABLE card_assignment_notifications DROP CONSTRAINT card_assignment_notifications_notification_type_check;
ALTER TABLE card_assignment_notifications ADD CONSTRAINT card_assignment_notifications_notification_type_check
 CHECK(notification_type IN ('CARD_ASSIGNED','CARD_CREATED','CARD_UPDATED','CARD_MOVED','CARD_ARCHIVED','CARD_RESTORED',
  'CARD_MEMBER_ADDED','CARD_MEMBER_REMOVED','LABEL_ADDED','LABEL_REMOVED','CARD_DATE_CHANGED','CARD_DUE_COMPLETED','CARD_DUE_REOPENED'));
INSERT INTO schema_migrations(version) VALUES('036_card_dates');
COMMIT;
