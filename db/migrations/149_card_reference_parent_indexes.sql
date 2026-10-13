BEGIN;

-- Board-filter indexes serve UI reads. Reparenting a Card requires exact
-- reverse FK probes; every constrained column must be an index prefix so a
-- cached plan cannot scan the whole Board while rejecting old references.
CREATE INDEX ix_card_labels_parent_identity ON card_labels(card_id,board_id,tenant_id);
CREATE INDEX ix_card_members_parent_identity ON card_members(card_id,board_id,tenant_id);

INSERT INTO schema_migrations(version) VALUES('149_card_reference_parent_indexes');
COMMIT;
