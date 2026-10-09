DO $$ BEGIN
 IF (SELECT count(*) FROM work_replay_clock_upgrade_fixture)<>2 OR EXISTS(
  SELECT 1 FROM work_replay_clock_upgrade_fixture f LEFT JOIN work_command_replays r USING(key_id)
  WHERE r.key_id IS NULL OR r.updated_at IS NOT NULL OR (to_jsonb(r)-'updated_at') IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'Legacy receipt payload or unknown completion clock changed';
 END IF;
END $$;
BEGIN;
UPDATE work_command_replays SET result_json=result_json,updated_at=clock_timestamp()
 WHERE tenant_id='12800000-0000-0000-0000-000000000101';
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM work_command_replays WHERE tenant_id='12800000-0000-0000-0000-000000000101' AND updated_at IS NOT NULL) THEN
  RAISE EXCEPTION 'No-op manufactured legacy clock provenance';
 END IF;
END $$;
UPDATE work_command_replays SET result_json='{"Succeeded":true}'::jsonb
 WHERE key_id='12800000-0000-0000-0000-000000000104';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM work_command_replays WHERE key_id='12800000-0000-0000-0000-000000000104'
  AND updated_at>=created_at AND isfinite(updated_at)) THEN
  RAISE EXCEPTION 'Actual legacy receipt update did not record its clock';
 END IF;
END $$;
ROLLBACK;
