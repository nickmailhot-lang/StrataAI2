DO $$
DECLARE source text; original_time timestamptz; event uuid; state timestamptz;
BEGIN
 IF (SELECT count(*) FROM organization_event_clock_upgrade_fixture)<>(SELECT count(*) FROM organization_metadata_events)+(SELECT count(*) FROM organization_lifecycle_events)
 OR EXISTS(SELECT 1 FROM organization_event_clock_upgrade_fixture f LEFT JOIN
  (SELECT 'organization_metadata_events'::text AS source,event_id,to_jsonb(e)-'updated_at' AS original,updated_at,COALESCE(ready_at,created_at) AS expected FROM organization_metadata_events e
   UNION ALL SELECT 'organization_lifecycle_events',event_id,to_jsonb(e)-'updated_at',updated_at,COALESCE(ready_at,created_at) FROM organization_lifecycle_events e) e USING(source,event_id)
  WHERE e.event_id IS NULL OR e.original IS DISTINCT FROM f.original OR e.updated_at IS DISTINCT FROM e.expected) THEN
  RAISE EXCEPTION 'Organization event clock upgrade changed historical data';
 END IF;
END $$;
DROP TABLE organization_event_clock_upgrade_fixture;
BEGIN;
DO $$
DECLARE source text; event uuid; creation timestamptz; publication timestamptz; result_time timestamptz;
BEGIN
 FOREACH source IN ARRAY ARRAY['organization_metadata_events'] LOOP
  EXECUTE format('SELECT event_id,created_at FROM %I WHERE ready_at IS NULL ORDER BY event_id LIMIT 1',source) INTO STRICT event,creation;
  EXECUTE format('UPDATE %I SET ready_at=created_at+interval ''2 seconds'' WHERE event_id=$1',source) USING event;
  EXECUTE format('SELECT updated_at,ready_at FROM %I WHERE event_id=$1',source) INTO result_time,publication USING event;
  IF result_time IS DISTINCT FROM creation+interval '2 seconds' OR result_time IS DISTINCT FROM publication THEN
   RAISE EXCEPTION 'First delivery did not derive event update clock';
  END IF;
  EXECUTE format('UPDATE %I SET ready_at=ready_at WHERE event_id=$1',source) USING event;
  BEGIN
   EXECUTE format('UPDATE %I SET updated_at=created_at WHERE event_id=$1',source) USING event;
   RAISE EXCEPTION 'Generated event clock replacement was admitted';
  EXCEPTION WHEN generated_always THEN NULL; END;
  BEGIN
   EXECUTE format('UPDATE %I SET ready_at=ready_at+interval ''1 second'' WHERE event_id=$1',source) USING event;
   RAISE EXCEPTION 'Historical delivery replacement was admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
  BEGIN
   EXECUTE format('UPDATE %I SET correlation_id=''forged-history'' WHERE event_id=$1',source) USING event;
   RAISE EXCEPTION 'Event payload replacement was admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
  BEGIN
   EXECUTE format('DELETE FROM %I WHERE event_id=$1',source) USING event;
   RAISE EXCEPTION 'Event history deletion was admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
  EXECUTE format('SELECT updated_at,ready_at FROM %I WHERE event_id=$1',source) INTO result_time,publication USING event;
  IF result_time IS DISTINCT FROM creation+interval '2 seconds' OR result_time IS DISTINCT FROM publication THEN
   RAISE EXCEPTION 'Replay or refused tamper changed retained event clock';
  END IF;
 END LOOP;
END $$;
ROLLBACK;
