BEGIN;
-- The journal's private references must keep their original personal audience
-- and target even when current intent/state changes or is cancelled.
CREATE FUNCTION enforce_private_activity_identity() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 IF TG_TABLE_NAME='watch_subscriptions' THEN
  IF ROW(NEW.tenant_id,NEW.id,NEW.user_id,NEW.entity_type,NEW.entity_id,NEW.board_id,NEW.list_id,NEW.card_id,NEW.created_at)
   IS DISTINCT FROM ROW(OLD.tenant_id,OLD.id,OLD.user_id,OLD.entity_type,OLD.entity_id,OLD.board_id,OLD.list_id,OLD.card_id,OLD.created_at) THEN
   RAISE EXCEPTION 'Private activity identity is immutable' USING ERRCODE='23514';
  END IF;
 ELSE
  IF ROW(NEW.tenant_id,NEW.id,NEW.user_id,NEW.card_id,NEW.created_at)
   IS DISTINCT FROM ROW(OLD.tenant_id,OLD.id,OLD.user_id,OLD.card_id,OLD.created_at) THEN
   RAISE EXCEPTION 'Private activity identity is immutable' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER watch_activity_identity_immutable AFTER UPDATE ON watch_subscriptions
 FOR EACH ROW EXECUTE FUNCTION enforce_private_activity_identity();
CREATE TRIGGER reminder_activity_identity_immutable AFTER UPDATE ON card_reminders
 FOR EACH ROW EXECUTE FUNCTION enforce_private_activity_identity();
REVOKE ALL ON FUNCTION enforce_private_activity_identity() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('064_private_activity_identity');
COMMIT;
