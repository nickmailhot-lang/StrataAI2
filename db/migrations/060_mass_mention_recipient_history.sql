BEGIN;
-- Username declarations remain capped separately; group history retains every
-- admitted recipient rather than applying the username discovery page limit.
ALTER TABLE comment_mention_snapshots DROP CONSTRAINT comment_mention_snapshots_recipient_count_check;
ALTER TABLE comment_mention_snapshots ADD CONSTRAINT comment_mention_snapshots_recipient_count_check CHECK(recipient_count>=0);
INSERT INTO schema_migrations(version) VALUES('060_mass_mention_recipient_history');
COMMIT;
