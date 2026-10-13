BEGIN;

-- Ordinary Card writes still check the parent immediately. An owning List
-- transfer transaction can defer this one check while reparenting its graph.
ALTER TABLE cards ALTER CONSTRAINT cards_list_id_board_id_tenant_id_fkey
    DEFERRABLE INITIALLY IMMEDIATE;

INSERT INTO schema_migrations(version) VALUES ('147_list_board_move_parent_integrity');
COMMIT;
