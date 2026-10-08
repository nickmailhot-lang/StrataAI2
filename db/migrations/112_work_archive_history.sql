BEGIN;
-- LIFE-FR-002/010: restoring work retains the latest known archive clock.
-- Historical null clocks remain unknown; do not invent backfilled evidence.
CREATE FUNCTION preserve_work_archive_history() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
BEGIN
 IF OLD.archived_at IS NOT NULL AND NEW.archived_at IS DISTINCT FROM OLD.archived_at
  AND NOT (OLD.lifecycle_state='ACTIVE' AND NEW.lifecycle_state='ARCHIVED'
   AND NEW.archived_at IS NOT NULL AND NEW.archived_at=NEW.updated_at
   AND NEW.archived_at>=OLD.archived_at) THEN
  RAISE EXCEPTION 'Work archive history must be retained' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION preserve_work_archive_history() FROM PUBLIC;
CREATE TRIGGER boards_archive_history BEFORE UPDATE ON boards
 FOR EACH ROW EXECUTE FUNCTION preserve_work_archive_history();
CREATE TRIGGER board_lists_archive_history BEFORE UPDATE ON board_lists
 FOR EACH ROW EXECUTE FUNCTION preserve_work_archive_history();
CREATE TRIGGER cards_archive_history BEFORE UPDATE ON cards
 FOR EACH ROW EXECUTE FUNCTION preserve_work_archive_history();
INSERT INTO schema_migrations(version) VALUES('112_work_archive_history');
COMMIT;
