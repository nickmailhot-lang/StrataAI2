BEGIN;
ALTER TABLE card_assignment_notifications DROP CONSTRAINT card_assignment_notifications_notification_type_check;
ALTER TABLE card_assignment_notifications ADD CONSTRAINT card_assignment_notifications_notification_type_check
 CHECK(notification_type IN ('CARD_ASSIGNED','CARD_CREATED','CARD_COPIED','CARD_UPDATED','CARD_MOVED','CARD_ARCHIVED','CARD_RESTORED',
  'CARD_MEMBER_ADDED','CARD_MEMBER_REMOVED','LABEL_ADDED','LABEL_REMOVED','CARD_DATE_CHANGED','CARD_DUE_COMPLETED','CARD_DUE_REOPENED','REMINDER_FIRED','MENTION_CREATED'));
INSERT INTO schema_migrations(version) VALUES ('067_card_copy_notifications');
COMMIT;
