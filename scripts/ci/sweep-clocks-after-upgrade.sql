DO $$ DECLARE relation_name text; valid boolean; BEGIN
 IF (SELECT count(*) FROM sweep_clock_upgrade_fixture)<>4 THEN RAISE EXCEPTION 'Legacy sweep fixture incomplete'; END IF;
 FOREACH relation_name IN ARRAY ARRAY['attachment_preview_sweeps','attachment_scan_sweeps'] LOOP
  EXECUTE format('SELECT NOT EXISTS(SELECT 1 FROM sweep_clock_upgrade_fixture f LEFT JOIN %I s ON s.tenant_id=(f.body->>''tenant_id'')::uuid WHERE f.relation=$1 AND (s.tenant_id IS NULL OR s.created_at IS NOT NULL OR s.updated_at IS NOT NULL OR (to_jsonb(s)-''created_at''-''updated_at'') IS DISTINCT FROM f.body))',relation_name)
   INTO valid USING relation_name;
  IF NOT valid THEN RAISE EXCEPTION 'Legacy sweep payload or unknown clocks changed'; END IF;
 END LOOP;
END $$;
BEGIN;
SELECT set_config('app.tenant_id','12900000-0000-0000-0000-000000000101',true);
SET LOCAL ROLE strataai_worker_runtime;
SELECT * FROM enqueue_attachment_preview_backfill('12900000-0000-0000-0000-000000000101',32);
SELECT * FROM recover_attachment_scan_page('12900000-0000-0000-0000-000000000101',32);
RESET ROLE;
DO $$ DECLARE relation_name text; valid boolean; BEGIN
 FOREACH relation_name IN ARRAY ARRAY['attachment_preview_sweeps','attachment_scan_sweeps'] LOOP
  EXECUTE format('SELECT created_at IS NULL AND updated_at IS NOT NULL AND isfinite(updated_at) AND cursor_created_at IS NULL AND cursor_id IS NULL FROM %I WHERE tenant_id=$1',relation_name)
   INTO valid USING '12900000-0000-0000-0000-000000000101'::uuid;
  IF NOT valid THEN RAISE EXCEPTION 'Actual legacy sweep reset lost unknown creation or recorded update'; END IF;
  EXECUTE format('UPDATE %I SET created_at=clock_timestamp(),updated_at=clock_timestamp() WHERE tenant_id=$1',relation_name)
   USING '12900000-0000-0000-0000-000000000102'::uuid;
  EXECUTE format('SELECT created_at IS NULL AND updated_at IS NULL FROM %I WHERE tenant_id=$1',relation_name)
   INTO valid USING '12900000-0000-0000-0000-000000000102'::uuid;
  IF NOT valid THEN RAISE EXCEPTION 'No-op manufactured legacy sweep audit clocks'; END IF;
 END LOOP;
END $$;
ROLLBACK;
