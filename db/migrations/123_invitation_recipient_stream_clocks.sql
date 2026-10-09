BEGIN;
LOCK TABLE invitation_recipient_streams,invitation_recipient_events IN ACCESS EXCLUSIVE MODE;
-- The retained journal is complete: one positive unique sequence per append.
-- Refuse missing/gapped counter history instead of inventing upgrade clocks.
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_recipient_streams s LEFT JOIN
  (SELECT email_normalized,count(*) AS records,max(sequence) AS last_sequence FROM invitation_recipient_events GROUP BY email_normalized) e USING(email_normalized)
  WHERE e.records IS DISTINCT FROM s.last_sequence OR e.last_sequence IS DISTINCT FROM s.last_sequence) THEN
  RAISE EXCEPTION 'Invitation recipient stream history is incomplete' USING ERRCODE='23514';
 END IF;
END $$;
ALTER TABLE invitation_recipient_streams ADD COLUMN created_at timestamptz, ADD COLUMN updated_at timestamptz;
UPDATE invitation_recipient_streams s SET created_at=first.created_at,updated_at=latest.created_at
 FROM invitation_recipient_events first,
 (SELECT email_normalized,max(created_at) AS created_at FROM invitation_recipient_events GROUP BY email_normalized) latest
 WHERE first.email_normalized=s.email_normalized AND first.sequence=1 AND latest.email_normalized=s.email_normalized;
ALTER TABLE invitation_recipient_streams
 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL,
 -- Existing allocators insert the counter before its journal entry. These
 -- provisional defaults are replaced in that same transaction; the deferred
 -- guard rejects any commit without the matching retained source clocks.
 ALTER COLUMN created_at SET DEFAULT clock_timestamp(),
 ALTER COLUMN updated_at SET DEFAULT clock_timestamp(),
 ADD CONSTRAINT invitation_recipient_stream_clock_order CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at);
CREATE INDEX invitation_recipient_event_clock_source ON invitation_recipient_events(email_normalized,created_at DESC);
CREATE FUNCTION refresh_invitation_recipient_stream_clocks() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 UPDATE public.invitation_recipient_streams s SET
  created_at=(SELECT created_at FROM public.invitation_recipient_events WHERE email_normalized=NEW.email_normalized AND sequence=1),
  updated_at=(SELECT created_at FROM public.invitation_recipient_events WHERE email_normalized=NEW.email_normalized ORDER BY created_at DESC LIMIT 1)
 WHERE s.email_normalized=NEW.email_normalized;
 RETURN NEW;
END $$;
CREATE TRIGGER invitation_recipient_stream_clock_publication AFTER INSERT ON invitation_recipient_events
 FOR EACH ROW EXECUTE FUNCTION refresh_invitation_recipient_stream_clocks();
CREATE FUNCTION enforce_invitation_recipient_stream_clocks() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE source_created timestamptz; source_updated timestamptz; source_sequence bigint;
 stream public.invitation_recipient_streams%ROWTYPE;
BEGIN
 -- Query the final row, not the provisional NEW queued by an earlier allocator.
 SELECT * INTO stream FROM public.invitation_recipient_streams WHERE email_normalized=NEW.email_normalized;
 SELECT created_at INTO source_created FROM public.invitation_recipient_events WHERE email_normalized=NEW.email_normalized AND sequence=1;
 SELECT created_at INTO source_updated FROM public.invitation_recipient_events WHERE email_normalized=NEW.email_normalized ORDER BY created_at DESC LIMIT 1;
 SELECT sequence INTO source_sequence FROM public.invitation_recipient_events WHERE email_normalized=NEW.email_normalized ORDER BY sequence DESC LIMIT 1;
 IF stream.email_normalized IS NULL OR source_created IS NULL OR source_updated IS NULL
  OR stream.last_sequence IS DISTINCT FROM source_sequence
  OR stream.created_at IS DISTINCT FROM source_created OR stream.updated_at IS DISTINCT FROM source_updated THEN
  RAISE EXCEPTION 'Invitation recipient stream clocks require retained journal history' USING ERRCODE='23514';
 END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER invitation_recipient_stream_clock_guard AFTER INSERT OR UPDATE ON invitation_recipient_streams
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_invitation_recipient_stream_clocks();
REVOKE ALL ON FUNCTION refresh_invitation_recipient_stream_clocks(),enforce_invitation_recipient_stream_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('123_invitation_recipient_stream_clocks');
COMMIT;
