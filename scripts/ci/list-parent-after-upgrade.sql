BEGIN;
SELECT set_config('app.tenant_id','14700000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF NOT EXISTS(SELECT FROM pg_constraint WHERE conrelid='cards'::regclass
  AND conname='cards_list_id_board_id_tenant_id_fkey' AND condeferrable AND NOT condeferred)
  OR (SELECT count(*) FROM list_parent_upgrade_fixture)<>3
  OR EXISTS(SELECT FROM list_parent_upgrade_fixture old WHERE NOT EXISTS(
   SELECT FROM cards c WHERE old.kind='card' AND c.id=old.id AND to_jsonb(c)=old.value
   UNION ALL SELECT FROM board_lists l WHERE old.kind='list' AND l.id=old.id AND to_jsonb(l)=old.value)) THEN
  RAISE EXCEPTION 'Parent upgrade changed existing rows or weakened the default constraint mode'; END IF;
 BEGIN
  UPDATE cards SET board_id='14700000-0000-0000-0000-000000000012'
   WHERE id='14700000-0000-0000-0000-000000000031';
  RAISE EXCEPTION 'Ordinary inconsistent Card parent was admitted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
END $$;
SET CONSTRAINTS cards_list_id_board_id_tenant_id_fkey DEFERRED;
UPDATE board_lists SET board_id='14700000-0000-0000-0000-000000000012',version=version+1,updated_at=clock_timestamp()
WHERE id='14700000-0000-0000-0000-000000000021';
UPDATE cards SET board_id='14700000-0000-0000-0000-000000000012',version=version+1,updated_at=clock_timestamp()
WHERE tenant_id='14700000-0000-0000-0000-000000000001';
SET CONSTRAINTS cards_list_id_board_id_tenant_id_fkey IMMEDIATE;
DO $$ BEGIN
 IF EXISTS(SELECT FROM list_parent_upgrade_fixture old WHERE NOT EXISTS(
  SELECT FROM cards c WHERE old.kind='card' AND c.id=old.id AND c.board_id='14700000-0000-0000-0000-000000000012'
   AND to_jsonb(c)-'board_id'-'version'-'updated_at'=old.value-'board_id'-'version'-'updated_at'
  UNION ALL SELECT FROM board_lists l WHERE old.kind='list' AND l.id=old.id AND l.board_id='14700000-0000-0000-0000-000000000012'
   AND to_jsonb(l)-'board_id'-'version'-'updated_at'=old.value-'board_id'-'version'-'updated_at')) THEN
  RAISE EXCEPTION 'Deferred List transfer failed to preserve its complete historical children'; END IF;
END $$;
COMMIT;
