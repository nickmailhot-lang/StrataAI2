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
ALTER TABLE work_events ADD CONSTRAINT uq_work_events_notification_card_source
 UNIQUE(tenant_id,board_id,event_id,entity_id,event_type);
ALTER TABLE card_assignment_notifications ADD CONSTRAINT notification_card_event_source_fk
 FOREIGN KEY(tenant_id,board_id,event_id,card_id,source_event_type)
 REFERENCES work_events(tenant_id,board_id,event_id,entity_id,event_type) ON DELETE RESTRICT;
INSERT INTO schema_migrations(version) VALUES('066_notification_historical_card');
COMMIT;
