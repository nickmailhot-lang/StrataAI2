BEGIN;
DO $$ BEGIN
 IF (SELECT count(*) FROM card_reference_upgrade_fixture)<>2 OR EXISTS(
  SELECT FROM card_reference_upgrade_fixture old WHERE NOT EXISTS(
   SELECT FROM card_labels c WHERE old.kind='label' AND to_jsonb(c)=old.value
   UNION ALL SELECT FROM card_members c WHERE old.kind='member' AND to_jsonb(c)=old.value)) THEN
  RAISE EXCEPTION 'Reference index upgrade rewrote existing associations'; END IF;
 IF (SELECT count(*) FROM pg_index i JOIN pg_class idx ON idx.oid=i.indexrelid
   WHERE idx.relname IN ('ix_card_labels_parent_identity','ix_card_members_parent_identity')
   AND i.indisvalid AND i.indisready AND i.indnkeyatts=3
   AND (SELECT array_agg(a.attname::text ORDER BY k.ordinality) FROM unnest(i.indkey::smallint[]) WITH ORDINALITY k(attnum,ordinality)
        JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum)=ARRAY['card_id','board_id','tenant_id'])<>2 THEN
  RAISE EXCEPTION 'Card reference indexes lack their valid ordered parent identity prefix'; END IF;
END $$;
ROLLBACK;
