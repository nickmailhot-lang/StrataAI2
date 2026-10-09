BEGIN;
LOCK TABLE organization_metadata_event_streams,organization_metadata_events IN ACCESS EXCLUSIVE MODE;
-- The retained journal is complete: one positive unique sequence per append.
-- Refuse missing/gapped counter history instead of inventing upgrade clocks.
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM organization_metadata_event_streams s LEFT JOIN
  (SELECT tenant_id,count(*) AS records,max(sequence) AS last_sequence FROM organization_metadata_events GROUP BY tenant_id) e USING(tenant_id)
  WHERE e.records IS DISTINCT FROM s.last_sequence OR e.last_sequence IS DISTINCT FROM s.last_sequence) THEN
  RAISE EXCEPTION 'Organization metadata stream history is incomplete' USING ERRCODE='23514';
 END IF;
END $$;
ALTER TABLE organization_metadata_event_streams ADD COLUMN created_at timestamptz, ADD COLUMN updated_at timestamptz;
UPDATE organization_metadata_event_streams s SET created_at=first.created_at,updated_at=latest.created_at
 FROM organization_metadata_events first,
 (SELECT tenant_id,max(created_at) AS created_at FROM organization_metadata_events GROUP BY tenant_id) latest
 WHERE first.tenant_id=s.tenant_id AND first.sequence=1 AND latest.tenant_id=s.tenant_id;
ALTER TABLE organization_metadata_event_streams
 ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL,
 -- Existing allocators insert the counter before its journal entry. These
 -- provisional defaults are replaced in that same transaction; the deferred
 -- guard rejects any commit without the matching retained source clocks.
 ALTER COLUMN created_at SET DEFAULT clock_timestamp(),
 ALTER COLUMN updated_at SET DEFAULT clock_timestamp(),
 ADD CONSTRAINT organization_metadata_stream_clock_order CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at);
CREATE INDEX organization_metadata_event_clock_source ON organization_metadata_events(tenant_id,created_at DESC);
CREATE FUNCTION refresh_organization_metadata_stream_clocks() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 UPDATE public.organization_metadata_event_streams s SET
  created_at=(SELECT created_at FROM public.organization_metadata_events WHERE tenant_id=NEW.tenant_id AND sequence=1),
  updated_at=(SELECT created_at FROM public.organization_metadata_events WHERE tenant_id=NEW.tenant_id ORDER BY created_at DESC LIMIT 1)
 WHERE s.tenant_id=NEW.tenant_id;
 RETURN NEW;
END $$;
CREATE TRIGGER organization_metadata_stream_clock_publication AFTER INSERT ON organization_metadata_events
 FOR EACH ROW EXECUTE FUNCTION refresh_organization_metadata_stream_clocks();
CREATE FUNCTION enforce_organization_metadata_stream_clocks() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE source_created timestamptz; source_updated timestamptz; source_sequence bigint;
 stream public.organization_metadata_event_streams%ROWTYPE;
BEGIN
 -- Query the final row, not the provisional NEW queued by an earlier allocator.
 SELECT * INTO stream FROM public.organization_metadata_event_streams WHERE tenant_id=NEW.tenant_id;
 SELECT created_at INTO source_created FROM public.organization_metadata_events WHERE tenant_id=NEW.tenant_id AND sequence=1;
 SELECT created_at INTO source_updated FROM public.organization_metadata_events WHERE tenant_id=NEW.tenant_id ORDER BY created_at DESC LIMIT 1;
 SELECT sequence INTO source_sequence FROM public.organization_metadata_events WHERE tenant_id=NEW.tenant_id ORDER BY sequence DESC LIMIT 1;
 IF stream.tenant_id IS NULL OR source_created IS NULL OR source_updated IS NULL
  OR stream.last_sequence IS DISTINCT FROM source_sequence
  OR stream.created_at IS DISTINCT FROM source_created OR stream.updated_at IS DISTINCT FROM source_updated THEN
  RAISE EXCEPTION 'Organization metadata stream clocks require retained journal history' USING ERRCODE='23514';
 END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER organization_metadata_stream_clock_guard AFTER INSERT OR UPDATE ON organization_metadata_event_streams
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_organization_metadata_stream_clocks();
REVOKE ALL ON FUNCTION refresh_organization_metadata_stream_clocks(),enforce_organization_metadata_stream_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('122_organization_metadata_stream_clocks');
COMMIT;
