BEGIN;
SELECT set_config('app.tenant_id','14800000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM configuration_list_upgrade_fixture)<>5
  OR EXISTS(SELECT FROM configuration_list_upgrade_fixture old WHERE NOT EXISTS(
   SELECT FROM organization_configurations c WHERE old.kind='current' AND to_jsonb(c)=old.value
   UNION ALL SELECT FROM organization_configuration_history h WHERE old.kind='history' AND to_jsonb(h)=old.value
   UNION ALL SELECT FROM organization_configuration_receipts r WHERE old.kind='receipt' AND to_jsonb(r)=old.value)) THEN
  RAISE EXCEPTION 'Historical List upgrade rewrote existing configuration provenance'; END IF;
 UPDATE board_lists SET board_id='14800000-0000-0000-0000-000000000012',version=version+1,updated_at=clock_timestamp()
  WHERE id='14800000-0000-0000-0000-000000000021';
 BEGIN
  UPDATE board_lists SET board_id='14800000-0000-0000-0000-000000000012'
   WHERE id='14800000-0000-0000-0000-000000000022';
  RAISE EXCEPTION 'Upgrade released the current intake List binding';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  DELETE FROM board_lists WHERE id='14800000-0000-0000-0000-000000000021';
  RAISE EXCEPTION 'Upgrade allowed deleting a historically referenced List';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 IF EXISTS(SELECT FROM configuration_list_upgrade_fixture old WHERE old.kind='history' AND NOT EXISTS(
   SELECT FROM organization_configuration_history h WHERE to_jsonb(h)=old.value)) THEN
  RAISE EXCEPTION 'Moving the former intake List rewrote its immutable source-time Board'; END IF;
END $$;
COMMIT;
