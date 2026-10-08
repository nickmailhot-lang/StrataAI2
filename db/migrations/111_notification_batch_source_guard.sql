BEGIN;
-- Validate a publication statement as a set. The notification/source FKs still
-- enforce tenant, Board and typed event identity, and this invoker check retains
-- the historical Card/Reminder identity rule without planning it per recipient.
CREATE FUNCTION enforce_notification_insert_sources() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF EXISTS(SELECT FROM inserted_notifications n
   WHERE NOT EXISTS(SELECT FROM public.work_events e
     WHERE e.tenant_id=n.tenant_id AND e.board_id=n.board_id
       AND e.event_id=n.event_id AND e.event_type=n.source_event_type
       AND (e.entity_type='Card' AND e.entity_id=n.card_id
         OR e.entity_type='Reminder' AND n.notification_type='REMINDER_FIRED'
           AND EXISTS(SELECT FROM public.card_reminders r WHERE r.tenant_id=n.tenant_id
             AND r.id=e.entity_id AND r.card_id=n.card_id))))
 THEN RAISE EXCEPTION 'Notification source Card is inconsistent' USING ERRCODE='23503'; END IF;
 RETURN NULL;
END $$;
REVOKE ALL ON FUNCTION enforce_notification_insert_sources() FROM PUBLIC;
DROP TRIGGER notification_card_source ON card_assignment_notifications;
-- Updates retain the existing immutable historical-attribution validation.
CREATE TRIGGER notification_card_source AFTER UPDATE ON card_assignment_notifications
 FOR EACH ROW EXECUTE FUNCTION enforce_notification_card_source();
CREATE TRIGGER notification_card_source_insert AFTER INSERT ON card_assignment_notifications
 REFERENCING NEW TABLE AS inserted_notifications
 FOR EACH STATEMENT EXECUTE FUNCTION enforce_notification_insert_sources();
-- Statement rejection rolls back all rows and their row-trigger journal effects
-- together. Journal serialization and every existing constraint remain intact.
INSERT INTO schema_migrations(version) VALUES('111_notification_batch_source_guard');
COMMIT;
