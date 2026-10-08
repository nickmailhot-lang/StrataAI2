BEGIN;
-- FOUND-FR-009 / AUTH data requirements: retain known lifecycle clocks on
-- upgrade. Do not pretend the migration's execution time was a past mutation.
ALTER TABLE sessions ADD COLUMN updated_at timestamptz;
UPDATE sessions SET updated_at = greatest(created_at, revoked_at, last_seen_at);
ALTER TABLE sessions ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE sessions ALTER COLUMN updated_at SET DEFAULT now();

ALTER TABLE password_reset_tokens ADD COLUMN updated_at timestamptz;
UPDATE password_reset_tokens SET updated_at = greatest(created_at, used_at, revoked_at);
ALTER TABLE password_reset_tokens ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE password_reset_tokens ALTER COLUMN updated_at SET DEFAULT now();

ALTER TABLE email_verification_tokens ADD COLUMN updated_at timestamptz;
UPDATE email_verification_tokens SET updated_at = greatest(created_at, used_at, revoked_at);
ALTER TABLE email_verification_tokens ALTER COLUMN updated_at SET NOT NULL;
ALTER TABLE email_verification_tokens ALTER COLUMN updated_at SET DEFAULT now();
-- Owning writers explicitly use the accepted command's lifecycle timestamp.
-- Defaults retain compatibility with existing administrative fixture inserts.
INSERT INTO schema_migrations(version) VALUES ('114_identity_lifecycle_clocks');
COMMIT;
