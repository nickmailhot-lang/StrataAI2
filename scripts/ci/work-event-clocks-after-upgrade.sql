DO $$ BEGIN
 IF (SELECT count(*) FROM work_event_clock_upgrade_fixture)<>3 OR EXISTS(
  SELECT 1 FROM work_event_clock_upgrade_fixture f LEFT JOIN work_events e USING(event_id)
  WHERE e.event_id IS NULL OR e.updated_at IS NOT NULL OR (to_jsonb(e)-'updated_at') IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'Legacy activity history or unknown update clock changed';
 END IF;
END $$;
BEGIN;
UPDATE work_events SET updated_at='infinity',ready_at=ready_at WHERE correlation_id='clock-upgrade';
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM work_events WHERE correlation_id='clock-upgrade' AND updated_at IS NOT NULL) THEN
  RAISE EXCEPTION 'No-op manufactured legacy activity update time';
 END IF;
END $$;
UPDATE work_events SET ready_at=NULL WHERE correlation_id='clock-upgrade' AND sequence=2;
UPDATE work_events SET ready_at=clock_timestamp() WHERE correlation_id='clock-upgrade' AND sequence=3;
DO $$ BEGIN
 IF (SELECT count(*) FROM work_events WHERE correlation_id='clock-upgrade' AND sequence IN (2,3)
  AND isfinite(updated_at) AND updated_at>=created_at)<>2 THEN
  RAISE EXCEPTION 'Legacy publish/reset mutation clock missing';
 END IF;
 IF EXISTS(SELECT 1 FROM work_events e JOIN work_event_clock_upgrade_fixture f USING(event_id)
  WHERE (to_jsonb(e)-'ready_at'-'updated_at') IS DISTINCT FROM (f.body-'ready_at')) THEN
  RAISE EXCEPTION 'Legacy readiness mutation rewrote historical activity';
 END IF;
END $$;
ROLLBACK;
