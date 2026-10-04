BEGIN;
-- Stored generated references are materialized after BEFORE triggers. Compare
-- the complete historical row only once those derived fields are available.
-- An AFTER exception still atomically rolls back the entire attempted update.
DROP TRIGGER work_event_activity_immutable ON work_events;
CREATE TRIGGER work_event_activity_immutable AFTER UPDATE ON work_events
 FOR EACH ROW EXECUTE FUNCTION enforce_activity_event_immutable();
INSERT INTO schema_migrations(version) VALUES('063_activity_generated_source_guard');
COMMIT;
