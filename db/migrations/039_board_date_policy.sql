BEGIN;
ALTER TABLE boards ADD COLUMN date_timezone_override text;
ALTER TABLE boards ADD CONSTRAINT board_date_timezone_check CHECK (
 date_timezone_override IS NULL OR (length(date_timezone_override) BETWEEN 1 AND 100
 AND date_timezone_override ~ '^[A-Za-z0-9_+./-]+$'));
COMMIT;
