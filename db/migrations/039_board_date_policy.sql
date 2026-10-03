BEGIN;
-- Previous releases applied this column without recording the ledger entry.
-- Preserve existing values during that upgrade; reject incompatible shape.
ALTER TABLE boards ADD COLUMN IF NOT EXISTS date_timezone_override text;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_attribute WHERE attrelid='boards'::regclass
   AND attname='date_timezone_override' AND NOT attisdropped AND atttypid='text'::regtype
   AND NOT attnotnull AND NOT atthasdef AND attgenerated='' AND attidentity='') THEN
  RAISE EXCEPTION 'Incompatible Board date timezone column';
 END IF;
END $$;
ALTER TABLE boards DROP CONSTRAINT IF EXISTS board_date_timezone_check;
ALTER TABLE boards ADD CONSTRAINT board_date_timezone_check CHECK (
 date_timezone_override IS NULL OR (length(date_timezone_override) BETWEEN 1 AND 100
 AND date_timezone_override ~ '^[A-Za-z0-9_+./-]+$'));
INSERT INTO schema_migrations(version) VALUES('039_board_date_policy');
COMMIT;
