BEGIN;
-- The existing immutable Work journal is the activity source, not analytics.
-- Earlier records have no event-time caption: do not rewrite them from a
-- mutable current profile and pretend that name was true when the event arose.
ALTER TABLE work_events ADD COLUMN activity_actor_label text;
UPDATE work_events SET activity_actor_label='Member '||actor_id::text;
ALTER TABLE work_events ALTER COLUMN activity_actor_label SET NOT NULL;
ALTER TABLE work_events ADD CONSTRAINT work_events_activity_label_check
 CHECK(length(activity_actor_label) BETWEEN 1 AND 160 AND activity_actor_label !~ '[[:cntrl:]]');
ALTER TABLE work_events ADD CONSTRAINT work_events_activity_time_check CHECK(isfinite(created_at));

CREATE FUNCTION capture_activity_actor_label() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE caption text;
BEGIN
 -- Only a participant in this exact Organization can supply a readable label.
 -- Runtime INSERT cannot choose or forge its own historical account caption.
 SELECT u.display_name INTO caption FROM public.users u
 JOIN public.organization_members m ON m.user_id=u.id AND m.tenant_id=NEW.tenant_id
 WHERE u.id=NEW.actor_id;
 IF caption IS NULL OR length(caption) NOT BETWEEN 1 AND 160 OR caption ~ '[[:cntrl:]]' THEN
  caption='Member '||NEW.actor_id::text;
 END IF;
 NEW.activity_actor_label=caption;
 RETURN NEW;
END $$;
CREATE TRIGGER work_event_activity_actor BEFORE INSERT ON work_events
 FOR EACH ROW EXECUTE FUNCTION capture_activity_actor_label();

CREATE FUNCTION enforce_activity_event_immutable() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 -- Worker ready_at is delivery state, not historical activity. No other
 -- envelope field, identity, metadata, time or attribution can be rewritten.
 IF (to_jsonb(NEW)-'ready_at') IS DISTINCT FROM (to_jsonb(OLD)-'ready_at') THEN
  RAISE EXCEPTION 'Activity source is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER work_event_activity_immutable BEFORE UPDATE ON work_events
 FOR EACH ROW EXECUTE FUNCTION enforce_activity_event_immutable();
REVOKE ALL ON FUNCTION capture_activity_actor_label(),enforce_activity_event_immutable() FROM PUBLIC;

CREATE INDEX ix_work_events_board_activity ON work_events(tenant_id,board_id,created_at DESC,event_id DESC);
CREATE INDEX ix_work_events_card_activity ON work_events(tenant_id,board_id,entity_id,created_at DESC,event_id DESC)
 WHERE entity_type='Card';
INSERT INTO schema_migrations(version) VALUES('062_activity_attribution');
COMMIT;
