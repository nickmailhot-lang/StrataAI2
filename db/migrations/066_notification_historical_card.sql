BEGIN;
-- A notification's Board identifies its historical source. The stable Card
-- can subsequently move; never rewrite or delete that source attribution.
DO $$ DECLARE existing_name name;
BEGIN
 SELECT conname INTO STRICT existing_name FROM pg_constraint
 WHERE conrelid='public.card_assignment_notifications'::regclass
   AND confrelid='public.cards'::regclass AND contype='f'
   AND pg_get_constraintdef(oid) LIKE 'FOREIGN KEY (card_id, board_id, tenant_id)%';
 EXECUTE format('ALTER TABLE public.card_assignment_notifications DROP CONSTRAINT %I',existing_name);
END $$;
ALTER TABLE card_assignment_notifications ADD CONSTRAINT notification_stable_card_fk
 FOREIGN KEY(card_id,tenant_id) REFERENCES cards(id,tenant_id) ON DELETE RESTRICT;
-- Removing the current-Board constraint must not permit rebinding a historical
-- event to another same-tenant Card. Preserve the source Board and event type,
-- and additionally enforce the actual source entity identity.
-- Reminder events identify a Reminder, whose Card reference is immutable.
-- Validate both typed source forms; do not pretend Reminder IDs are Card IDs.
DO $$ BEGIN
 IF EXISTS(SELECT FROM card_assignment_notifications n
   JOIN work_events e ON e.tenant_id=n.tenant_id AND e.board_id=n.board_id
     AND e.event_id=n.event_id AND e.event_type=n.source_event_type
   WHERE NOT (e.entity_type='Card' AND e.entity_id=n.card_id
     OR e.entity_type='Reminder' AND n.notification_type='REMINDER_FIRED'
       AND EXISTS(SELECT FROM card_reminders r WHERE r.tenant_id=n.tenant_id
         AND r.id=e.entity_id AND r.card_id=n.card_id)))
 THEN RAISE EXCEPTION 'Notification source Card is inconsistent' USING ERRCODE='23503'; END IF;
END $$;
CREATE FUNCTION enforce_notification_card_source() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='UPDATE' THEN
  IF (NEW.tenant_id,NEW.board_id,NEW.event_id,NEW.card_id,NEW.notification_type)
    IS NOT DISTINCT FROM (OLD.tenant_id,OLD.board_id,OLD.event_id,OLD.card_id,OLD.notification_type)
  THEN RETURN NEW; END IF;
 END IF;
 IF NOT EXISTS(SELECT FROM public.work_events e
   WHERE e.tenant_id=NEW.tenant_id AND e.board_id=NEW.board_id
     AND e.event_id=NEW.event_id AND e.event_type=NEW.source_event_type
     AND (e.entity_type='Card' AND e.entity_id=NEW.card_id
       OR e.entity_type='Reminder' AND NEW.notification_type='REMINDER_FIRED'
         AND EXISTS(SELECT FROM public.card_reminders r WHERE r.tenant_id=NEW.tenant_id
           AND r.id=e.entity_id AND r.card_id=NEW.card_id)))
 THEN RAISE EXCEPTION 'Notification source Card is inconsistent' USING ERRCODE='23503'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER notification_card_source AFTER INSERT OR UPDATE ON card_assignment_notifications
 FOR EACH ROW EXECUTE FUNCTION enforce_notification_card_source();
REVOKE ALL ON FUNCTION enforce_notification_card_source() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('066_notification_historical_card');
COMMIT;
