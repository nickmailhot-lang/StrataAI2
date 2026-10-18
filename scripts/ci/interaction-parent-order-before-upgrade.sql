BEGIN;
DO $$ BEGIN
 IF to_regprocedure('lock_interaction_board_parent(uuid,uuid)') IS NOT NULL THEN
  RAISE EXCEPTION 'Interaction parent upgrade did not start from the previous schema'; END IF;
END $$;
SELECT set_config('app.identity_subject','14700000-0000-0000-0000-000000000099',true);
SELECT * FROM append_or_replay_board_filter_interaction(
 '15000000-0000-0000-0000-000000000001',repeat('a',64),'15000000-0000-0000-0000-000000000002',
 '14700000-0000-0000-0000-000000000099','14700000-0000-0000-0000-000000000001',
 '14700000-0000-0000-0000-000000000012','2026-10-01T12:00:00Z');
SELECT * FROM append_or_replay_navigation_interaction(
 '15000000-0000-0000-0000-000000000003',repeat('b',64),'15000000-0000-0000-0000-000000000004',
 '14700000-0000-0000-0000-000000000099','BOARD_OPENED','14700000-0000-0000-0000-000000000001',
 '14700000-0000-0000-0000-000000000012','14700000-0000-0000-0000-000000000012',
 (SELECT version FROM boards WHERE id='14700000-0000-0000-0000-000000000012'),'2026-10-01T12:00:00Z');
CREATE TABLE interaction_parent_upgrade_fixture(kind text,value jsonb);
INSERT INTO interaction_parent_upgrade_fixture
 SELECT 'search_event',to_jsonb(e) FROM search_interaction_events e
 UNION ALL SELECT 'search_stream',to_jsonb(s) FROM search_interaction_streams s
 UNION ALL SELECT 'filter_receipt',to_jsonb(r) FROM board_filter_interaction_replays r
 UNION ALL SELECT 'navigation_event',to_jsonb(e) FROM navigation_interaction_events e
 UNION ALL SELECT 'navigation_receipt',to_jsonb(r) FROM navigation_interaction_replays r;
CREATE TABLE interaction_parent_function_upgrade_fixture AS
 SELECT oid,proname,proowner,proacl,prosecdef,proconfig FROM pg_proc
 WHERE pronamespace='public'::regnamespace AND proname IN
 ('append_search_interaction','append_or_replay_board_filter_interaction',
  'append_navigation_interaction','append_or_replay_navigation_interaction');
DO $$ BEGIN
 IF (SELECT count(*) FROM interaction_parent_function_upgrade_fixture)<>4
  OR NOT EXISTS(SELECT FROM board_filter_interaction_replays WHERE request_id='15000000-0000-0000-0000-000000000001')
  OR NOT EXISTS(SELECT FROM navigation_interaction_replays WHERE request_id='15000000-0000-0000-0000-000000000003') THEN
  RAISE EXCEPTION 'Interaction upgrade requires populated original receipts and four existing functions'; END IF;
END $$;
COMMIT;
