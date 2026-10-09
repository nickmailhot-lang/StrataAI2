BEGIN;
LOCK TABLE notification_event_streams,notification_events IN ACCESS EXCLUSIVE MODE;
-- Preserve source history rather than naming migration time as creation.
-- Positive unique sequences plus count=max=counter prove contiguous history.
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM notification_event_streams s LEFT JOIN
  (SELECT tenant_id,recipient_id,count(*) AS records,max(sequence) AS last_sequence
   FROM notification_events GROUP BY tenant_id,recipient_id) e USING(tenant_id,recipient_id)
  WHERE e.records IS DISTINCT FROM s.last_sequence OR e.last_sequence IS DISTINCT FROM s.last_sequence)
 OR EXISTS(SELECT 1 FROM notification_events e LEFT JOIN notification_event_streams s USING(tenant_id,recipient_id)
  WHERE s.tenant_id IS NULL OR NOT isfinite(e.created_at)) THEN
  RAISE EXCEPTION 'Notification stream history is incomplete or invalid' USING ERRCODE='23514';
 END IF;
END $$;
ALTER TABLE notification_event_streams ADD COLUMN created_at timestamptz, ADD COLUMN updated_at timestamptz;
UPDATE notification_event_streams s SET created_at=first.created_at,updated_at=latest.created_at
 FROM notification_events first,
 (SELECT tenant_id,recipient_id,max(created_at) AS created_at FROM notification_events GROUP BY tenant_id,recipient_id) latest
 WHERE first.tenant_id=s.tenant_id AND first.recipient_id=s.recipient_id AND first.sequence=1
  AND latest.tenant_id=s.tenant_id AND latest.recipient_id=s.recipient_id;
ALTER TABLE notification_event_streams
 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL,
 -- The existing counter allocator runs before its owning journal insert.
 -- These provisional values must be replaced before the transaction commits.
 ALTER COLUMN created_at SET DEFAULT clock_timestamp(),
 ALTER COLUMN updated_at SET DEFAULT clock_timestamp(),
 ADD CONSTRAINT notification_stream_clock_order CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at);
CREATE INDEX notification_event_clock_source ON notification_events(tenant_id,recipient_id,created_at DESC);
CREATE FUNCTION refresh_notification_stream_clocks() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 UPDATE public.notification_event_streams s SET
  created_at=(SELECT created_at FROM public.notification_events
   WHERE tenant_id=NEW.tenant_id AND recipient_id=NEW.recipient_id AND sequence=1),
  updated_at=(SELECT created_at FROM public.notification_events
   WHERE tenant_id=NEW.tenant_id AND recipient_id=NEW.recipient_id ORDER BY created_at DESC LIMIT 1)
 WHERE s.tenant_id=NEW.tenant_id AND s.recipient_id=NEW.recipient_id;
 RETURN NEW;
END $$;
CREATE TRIGGER notification_stream_clock_publication AFTER INSERT ON notification_events
 FOR EACH ROW EXECUTE FUNCTION refresh_notification_stream_clocks();
CREATE FUNCTION enforce_notification_stream_clocks() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE source_created timestamptz; source_updated timestamptz; source_sequence bigint;
 stream public.notification_event_streams%ROWTYPE;
BEGIN
 -- Inspect the final row, including publication after an earlier allocator.
 SELECT * INTO stream FROM public.notification_event_streams
  WHERE tenant_id=NEW.tenant_id AND recipient_id=NEW.recipient_id;
 SELECT created_at INTO source_created FROM public.notification_events
  WHERE tenant_id=NEW.tenant_id AND recipient_id=NEW.recipient_id AND sequence=1;
 SELECT created_at INTO source_updated FROM public.notification_events
  WHERE tenant_id=NEW.tenant_id AND recipient_id=NEW.recipient_id ORDER BY created_at DESC LIMIT 1;
 SELECT sequence INTO source_sequence FROM public.notification_events
  WHERE tenant_id=NEW.tenant_id AND recipient_id=NEW.recipient_id ORDER BY sequence DESC LIMIT 1;
 IF stream.tenant_id IS NULL OR source_created IS NULL OR source_updated IS NULL
  OR stream.last_sequence IS DISTINCT FROM source_sequence
  OR stream.created_at IS DISTINCT FROM source_created OR stream.updated_at IS DISTINCT FROM source_updated THEN
  RAISE EXCEPTION 'Notification stream clocks require retained journal history' USING ERRCODE='23514';
 END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER notification_stream_clock_guard AFTER INSERT OR UPDATE ON notification_event_streams
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_notification_stream_clocks();
REVOKE ALL ON FUNCTION refresh_notification_stream_clocks(),enforce_notification_stream_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('126_notification_stream_clocks');
COMMIT;
