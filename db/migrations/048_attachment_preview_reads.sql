BEGIN;
-- Read-only private integrity metadata. Owning Application reads apply current
-- Card/Board authorization; both tables still enforce tenant RLS. Publication
-- and recovery functions remain exclusively Worker capabilities.
DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT ON attachment_previews,attachment_preview_publications TO strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('048_attachment_preview_reads');
COMMIT;
