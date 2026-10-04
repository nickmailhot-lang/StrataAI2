BEGIN;
-- Existing rows retain an unknown creation clock; updated_at is not evidence
-- of creation. Revision 1 is their first retained baseline, not inferred history.
ALTER TABLE user_board_preferences
 ADD COLUMN created_at timestamptz,
 ADD COLUMN version bigint NOT NULL DEFAULT 1,
 ADD CONSTRAINT board_preference_version_positive CHECK (version > 0),
 ADD CONSTRAINT board_preference_clock_order CHECK (created_at IS NULL OR created_at <= updated_at);
INSERT INTO schema_migrations(version) VALUES('070_board_star_revisions');
COMMIT;
