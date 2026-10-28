DO $$
DECLARE relation text;
BEGIN
 FOREACH relation IN ARRAY ARRAY['strata_lots','strata_lot_history','strata_lot_receipts'] LOOP
  IF NOT EXISTS(SELECT FROM pg_class WHERE oid=to_regclass('public.'||relation) AND relrowsecurity AND relforcerowsecurity)
   OR NOT EXISTS(SELECT FROM pg_policy WHERE polrelid=to_regclass('public.'||relation)) THEN
   RAISE EXCEPTION 'Strata lot upgrade lost tenant isolation: %',relation;
  END IF;
 END LOOP;
 IF NOT EXISTS(SELECT FROM pg_constraint WHERE conrelid='strata_lots'::regclass
  AND conname='strata_lot_current_history' AND convalidated AND condeferrable AND condeferred) THEN
  RAISE EXCEPTION 'Strata lot current history integrity missing';
 END IF;
 IF (SELECT count(*) FROM pg_trigger WHERE tgname IN
  ('strata_lot_identity','strata_lot_history_immutable','strata_lot_receipt_immutable') AND NOT tgisinternal)<>3 THEN
  RAISE EXCEPTION 'Strata lot identity/history guards missing';
 END IF;
END $$;
