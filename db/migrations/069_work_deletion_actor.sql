BEGIN;
-- Historical deletions did not persist an actor. Leave their attribution
-- unknown rather than guessing from a mutable membership or profile.
ALTER TABLE boards ADD COLUMN deleted_by uuid REFERENCES users(id) ON DELETE RESTRICT,
 ADD CONSTRAINT board_deletion_actor_state CHECK (deleted_by IS NULL OR lifecycle_state='DELETED');
ALTER TABLE board_lists ADD COLUMN deleted_by uuid REFERENCES users(id) ON DELETE RESTRICT,
 ADD CONSTRAINT list_deletion_actor_state CHECK (deleted_by IS NULL OR lifecycle_state='DELETED');
ALTER TABLE cards ADD COLUMN deleted_by uuid REFERENCES users(id) ON DELETE RESTRICT,
 ADD CONSTRAINT card_deletion_actor_state CHECK (deleted_by IS NULL OR lifecycle_state='DELETED');
INSERT INTO schema_migrations(version) VALUES('069_work_deletion_actor');
COMMIT;
