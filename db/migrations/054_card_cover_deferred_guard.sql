BEGIN;
-- Deferred constraint triggers execute at the transaction boundary, after a
-- private publishing function's SECURITY DEFINER scope has ended. Give only
-- this trigger its owner's guard capability; never grant Worker Card reads.
-- The trigger accepts no caller arguments, uses the already-admitted immutable
-- NEW tenant/Card/File identity, returns no metadata and has a fixed search path.
ALTER FUNCTION enforce_selected_cover_source() SECURITY DEFINER;
REVOKE ALL ON FUNCTION enforce_selected_cover_source() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('054_card_cover_deferred_guard');
COMMIT;
