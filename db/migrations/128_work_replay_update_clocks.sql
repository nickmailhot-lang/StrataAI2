BEGIN;
LOCK TABLE work_command_replays IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM work_command_replays WHERE NOT isfinite(created_at)) THEN
  RAISE EXCEPTION 'Work receipt creation clock is not finite' USING ERRCODE='23514';
 END IF;
END $$;
-- Legacy completion times were not recorded. NULL means unknown, never an
-- invented migration/expiry/source time. Preserve every original receipt.
ALTER TABLE work_command_replays ADD COLUMN updated_at timestamptz;
ALTER TABLE work_command_replays ADD CONSTRAINT work_replay_update_clock
 CHECK(updated_at IS NULL OR (isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at));
CREATE FUNCTION capture_work_replay_update_clock() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 IF NOT isfinite(NEW.created_at) THEN
  RAISE EXCEPTION 'Work receipt creation clock is not finite' USING ERRCODE='23514';
 END IF;
 IF TG_OP='INSERT' THEN
  NEW.updated_at:=NEW.created_at;
 ELSIF (to_jsonb(NEW)-'updated_at') IS NOT DISTINCT FROM (to_jsonb(OLD)-'updated_at') THEN
  -- Neither exact replay/no-op nor a caller-supplied clock restamps history.
  NEW.updated_at:=OLD.updated_at;
 ELSE
  NEW.updated_at:=GREATEST(clock_timestamp(),NEW.created_at,OLD.updated_at);
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER work_replay_update_clock BEFORE INSERT OR UPDATE ON work_command_replays
 FOR EACH ROW EXECUTE FUNCTION capture_work_replay_update_clock();
REVOKE ALL ON FUNCTION capture_work_replay_update_clock() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('128_work_replay_update_clocks');
COMMIT;
