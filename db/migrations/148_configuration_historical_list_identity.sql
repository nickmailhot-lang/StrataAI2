BEGIN;

-- Immutable configuration history records the Board at source time. A former
-- intake List can move later without rewriting that historical attribution.
-- Current configuration retains its composite Board/List FK. Its existing
-- source guard validates the live parent and the history journal requires the
-- exact current record, so this does not admit a fabricated source relationship.
DO $$ DECLARE existing_name name;
BEGIN
 SELECT conname INTO STRICT existing_name FROM pg_constraint
 WHERE conrelid='public.organization_configuration_history'::regclass
   AND confrelid='public.board_lists'::regclass AND contype='f'
   AND pg_get_constraintdef(oid) LIKE 'FOREIGN KEY (intake_list_id, intake_board_id, tenant_id)%';
 EXECUTE format('ALTER TABLE public.organization_configuration_history DROP CONSTRAINT %I',existing_name);
END $$;
ALTER TABLE organization_configuration_history
 ADD CONSTRAINT configuration_history_stable_list_fk
 FOREIGN KEY(intake_list_id,tenant_id) REFERENCES board_lists(id,tenant_id) ON DELETE RESTRICT;

INSERT INTO schema_migrations(version) VALUES('148_configuration_historical_list_identity');
COMMIT;
