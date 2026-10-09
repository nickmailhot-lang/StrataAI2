BEGIN;
LOCK TABLE work_events IN ACCESS EXCLUSIVE MODE;
-- Readiness may have been reset. Neither created_at nor ready_at proves the
-- last legacy mutation time; preserve unknown provenance instead of backfill.
ALTER TABLE work_events ADD COLUMN updated_at timestamptz;
ALTER TABLE work_events ADD CONSTRAINT work_event_update_clock
 CHECK(updated_at IS NULL OR (isfinite(updated_at) AND updated_at>=created_at));
CREATE FUNCTION capture_work_event_update_clock() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  NEW.updated_at:=NEW.created_at;
 ELSIF NEW.ready_at IS NOT DISTINCT FROM OLD.ready_at THEN
  NEW.updated_at:=OLD.updated_at;
 ELSE
  NEW.updated_at:=GREATEST(clock_timestamp(),NEW.created_at,OLD.updated_at);
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER work_event_update_clock BEFORE INSERT OR UPDATE ON work_events
 FOR EACH ROW EXECUTE FUNCTION capture_work_event_update_clock();
-- Keep the existing AFTER trigger: generated references must be materialized
-- before checking immutable history. Only readiness and its managed clock vary.
CREATE OR REPLACE FUNCTION enforce_activity_event_immutable() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 IF (to_jsonb(NEW)-'ready_at'-'updated_at') IS DISTINCT FROM
    (to_jsonb(OLD)-'ready_at'-'updated_at') THEN
  RAISE EXCEPTION 'Activity source is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION capture_work_event_update_clock(),enforce_activity_event_immutable() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('130_work_event_update_clocks');
COMMIT;
