BEGIN;
DO $$ BEGIN
 IF EXISTS(SELECT FROM interaction_parent_function_upgrade_fixture old LEFT JOIN pg_proc p USING(oid)
   WHERE ROW(p.proname,p.proowner,p.proacl,p.prosecdef,p.proconfig)
    IS DISTINCT FROM ROW(old.proname,old.proowner,old.proacl,old.prosecdef,old.proconfig)) THEN
  RAISE EXCEPTION 'Interaction upgrade changed an existing function identity or capability'; END IF;
 IF NOT EXISTS(SELECT FROM pg_proc WHERE oid='lock_interaction_board_parent(uuid,uuid)'::regprocedure
   AND NOT prosecdef AND proconfig @> ARRAY['search_path=pg_catalog, public','app.tenant_id='])
  OR has_function_privilege('strataai_api_runtime','lock_interaction_board_parent(uuid,uuid)','EXECUTE')
  OR has_function_privilege('strataai_worker_runtime','lock_interaction_board_parent(uuid,uuid)','EXECUTE') THEN
  RAISE EXCEPTION 'Interaction parent helper is not private, invoker-only and context-bounded'; END IF;
END $$;
SELECT set_config('app.identity_subject','14700000-0000-0000-0000-000000000099',true);
SELECT set_config('app.tenant_id','15000000-0000-0000-0000-000000000099',true);
DO $$ DECLARE original record; BEGIN
 SELECT * INTO original FROM append_or_replay_board_filter_interaction(
  '15000000-0000-0000-0000-000000000001',repeat('a',64),'15000000-0000-0000-0000-000000000002',
  '14700000-0000-0000-0000-000000000099','14700000-0000-0000-0000-000000000001',
  '14700000-0000-0000-0000-000000000012','2026-10-01T12:00:00Z');
 IF original.original_event IS DISTINCT FROM '15000000-0000-0000-0000-000000000002'::uuid
  OR original.original_created IS DISTINCT FROM '2026-10-01T12:00:00Z'::timestamptz THEN
  RAISE EXCEPTION 'Filter original receipt changed across upgrade'; END IF;
 SELECT * INTO original FROM append_or_replay_navigation_interaction(
  '15000000-0000-0000-0000-000000000003',repeat('b',64),'15000000-0000-0000-0000-000000000004',
  '14700000-0000-0000-0000-000000000099','BOARD_OPENED','14700000-0000-0000-0000-000000000001',
  '14700000-0000-0000-0000-000000000012','14700000-0000-0000-0000-000000000012',
  (SELECT version FROM boards WHERE id='14700000-0000-0000-0000-000000000012'),'2026-10-01T12:00:00Z');
 IF original.original_event IS DISTINCT FROM '15000000-0000-0000-0000-000000000004'::uuid
  OR original.original_created IS DISTINCT FROM '2026-10-01T12:00:00Z'::timestamptz THEN
  RAISE EXCEPTION 'Navigation original receipt changed across upgrade'; END IF;
 IF current_setting('app.tenant_id')<>'15000000-0000-0000-0000-000000000099' THEN
  RAISE EXCEPTION 'Interaction parent upgrade leaked its tenant context'; END IF;
END $$;
DO $$ BEGIN
 IF EXISTS(
  WITH current_rows AS (
   SELECT 'search_event' kind,to_jsonb(e) value FROM search_interaction_events e
   UNION ALL SELECT 'search_stream',to_jsonb(s) FROM search_interaction_streams s
   UNION ALL SELECT 'filter_receipt',to_jsonb(r) FROM board_filter_interaction_replays r
   UNION ALL SELECT 'navigation_event',to_jsonb(e) FROM navigation_interaction_events e
   UNION ALL SELECT 'navigation_receipt',to_jsonb(r) FROM navigation_interaction_replays r)
  (SELECT * FROM interaction_parent_upgrade_fixture EXCEPT ALL SELECT * FROM current_rows)
  UNION ALL (SELECT * FROM current_rows EXCEPT ALL SELECT * FROM interaction_parent_upgrade_fixture)) THEN
  RAISE EXCEPTION 'Interaction parent upgrade or replay rewrote retained history'; END IF;
END $$;
ROLLBACK;
