-- PRD-01/ARCH-04: actual Worker capabilities retain original sweep scopes.
\set ON_ERROR_STOP on
BEGIN;
INSERT INTO organizations(id,name,created_at,updated_at)
 VALUES('12900000-0000-0000-0000-000000000201','Current checkpoint',now(),now());
SELECT set_config('app.tenant_id','12900000-0000-0000-0000-000000000201',true);
SET LOCAL ROLE strataai_worker_runtime;
SELECT * FROM enqueue_attachment_preview_backfill('12900000-0000-0000-0000-000000000201',32);
SELECT * FROM recover_attachment_scan_page('12900000-0000-0000-0000-000000000201',32);
RESET ROLE;
DO $$ DECLARE relation_name text; first jsonb; changed jsonb; current_row jsonb; BEGIN
 FOREACH relation_name IN ARRAY ARRAY['attachment_preview_sweeps','attachment_scan_sweeps'] LOOP
  EXECUTE format('SELECT to_jsonb(s) FROM %I s WHERE tenant_id=$1',relation_name)
   INTO first USING '12900000-0000-0000-0000-000000000201'::uuid;
  IF first IS NULL OR (first->>'created_at') IS NULL OR first->>'created_at' IS DISTINCT FROM first->>'updated_at'
   OR NOT isfinite((first->>'created_at')::timestamptz) THEN RAISE EXCEPTION 'New sweep creation/update clock unavailable'; END IF;
  EXECUTE format('UPDATE %I SET cursor_created_at=''2040-01-01'',cursor_id=$1,created_at=''infinity'',updated_at=''-infinity'' WHERE tenant_id=$2',relation_name)
   USING '12900000-0000-0000-0000-000000000203'::uuid,'12900000-0000-0000-0000-000000000201'::uuid;
  EXECUTE format('SELECT to_jsonb(s) FROM %I s WHERE tenant_id=$1',relation_name)
   INTO changed USING '12900000-0000-0000-0000-000000000201'::uuid;
  IF changed->>'created_at' IS DISTINCT FROM first->>'created_at' OR NOT isfinite((changed->>'updated_at')::timestamptz)
   OR (changed->>'updated_at')::timestamptz<(first->>'updated_at')::timestamptz
   OR changed->>'updated_at' IS NOT DISTINCT FROM changed->>'cursor_created_at' THEN RAISE EXCEPTION 'Sweep used caller/candidate clock or changed creation'; END IF;
  EXECUTE format('UPDATE %I SET cursor_id=cursor_id,updated_at=''infinity'' WHERE tenant_id=$1',relation_name)
   USING '12900000-0000-0000-0000-000000000201'::uuid;
  EXECUTE format('SELECT to_jsonb(s) FROM %I s WHERE tenant_id=$1',relation_name)
   INTO current_row USING '12900000-0000-0000-0000-000000000201'::uuid;
  IF current_row IS DISTINCT FROM changed THEN RAISE EXCEPTION 'Sweep no-op changed audit clock'; END IF;
  BEGIN
   EXECUTE format('UPDATE %I SET cursor_created_at=NULL WHERE tenant_id=$1',relation_name)
    USING '12900000-0000-0000-0000-000000000201'::uuid;
   RAISE EXCEPTION 'Incomplete cursor pair accepted';
  EXCEPTION WHEN check_violation THEN NULL;
  END;
  EXECUTE format('SELECT to_jsonb(s) FROM %I s WHERE tenant_id=$1',relation_name)
   INTO current_row USING '12900000-0000-0000-0000-000000000201'::uuid;
  IF current_row IS DISTINCT FROM changed THEN RAISE EXCEPTION 'Refused cursor update retained its clock'; END IF;
 END LOOP;
END $$;
SET LOCAL ROLE strataai_worker_runtime;
SELECT * FROM enqueue_attachment_preview_backfill('12900000-0000-0000-0000-000000000201',32);
SELECT * FROM recover_attachment_scan_page('12900000-0000-0000-0000-000000000201',32);
RESET ROLE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM attachment_preview_sweeps WHERE tenant_id='12900000-0000-0000-0000-000000000201' AND cursor_id IS NULL AND updated_at>=created_at)
  OR NOT EXISTS(SELECT 1 FROM attachment_scan_sweeps WHERE tenant_id='12900000-0000-0000-0000-000000000201' AND cursor_id IS NULL AND updated_at>=created_at) THEN
  RAISE EXCEPTION 'Worker sweep reset clock unavailable'; END IF;
END $$;
ROLLBACK;
\echo 'Sweep clocks: actual Worker creation/reset, database clocks, stable creation, no-op preservation and refused-update rollback passed.'
