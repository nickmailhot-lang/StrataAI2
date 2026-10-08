DO $$
DECLARE row record; current_state jsonb; checked integer=0;
BEGIN
 FOR row IN SELECT * FROM identity_lifecycle_clock_upgrade_fixture LOOP
  EXECUTE format('SELECT to_jsonb(t) FROM %I t WHERE id=$1',row.table_name) INTO current_state USING row.id;
  IF current_state IS NULL OR current_state-'updated_at' IS DISTINCT FROM row.before_state
    OR (current_state->>'updated_at')::timestamptz IS DISTINCT FROM row.expected THEN
   RAISE EXCEPTION 'Identity lifecycle clock upgrade changed history or lost a known clock';
  END IF;
  checked=checked+1;
 END LOOP;
 IF checked<>9 THEN RAISE EXCEPTION 'Incomplete identity clock upgrade fixture'; END IF;
 IF (SELECT count(*) FROM information_schema.columns WHERE table_schema='public'
      AND table_name IN ('sessions','password_reset_tokens','email_verification_tokens')
      AND column_name='updated_at' AND is_nullable='NO')<>3 THEN
  RAISE EXCEPTION 'Identity lifecycle clocks must be non-null';
 END IF;
END $$;
DROP TABLE identity_lifecycle_clock_upgrade_fixture;
