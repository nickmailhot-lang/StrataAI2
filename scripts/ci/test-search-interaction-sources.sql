-- Restricted source capability foundation; not producer/transport acceptance.
\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_search_source_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_search_source_ci;
GRANT SELECT ON search_interaction_events,search_interaction_streams TO strataai_search_source_ci;
GRANT SELECT ON board_filter_interaction_replays TO strataai_search_source_ci;
GRANT EXECUTE ON FUNCTION append_search_interaction(uuid,uuid,text,uuid,uuid,timestamptz) TO strataai_search_source_ci;
GRANT EXECUTE ON FUNCTION append_or_replay_board_filter_interaction(uuid,text,uuid,uuid,uuid,uuid,timestamptz) TO strataai_search_source_ci;
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at) VALUES
 ('07600000-0000-0000-0000-000000000001','search-a@example.test','SEARCH-A@EXAMPLE.TEST','Search A','ACTIVE','fixture',now(),now()),
 ('07600000-0000-0000-0000-000000000002','search-b@example.test','SEARCH-B@EXAMPLE.TEST','Search B','ACTIVE','fixture',now(),now());
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('07600000-0000-0000-0000-000000000010','Private source fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at) VALUES
 ('07600000-0000-0000-0000-000000000011','07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000001','MEMBER','ACTIVE',now(),now());
INSERT INTO boards(id,tenant_id,name,visibility,background_type,background_value,created_at,updated_at) VALUES
 ('07600000-0000-0000-0000-000000000020','07600000-0000-0000-0000-000000000010','Private source Board','PRIVATE','COLOR','blue',now(),now());
INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES
 ('07600000-0000-0000-0000-000000000021','07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020','07600000-0000-0000-0000-000000000001','MEMBER','ACTIVE',now(),now());
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_class WHERE oid IN ('search_interaction_events'::regclass,'search_interaction_streams'::regclass)
   AND NOT(relrowsecurity AND relforcerowsecurity)) THEN RAISE EXCEPTION 'Private source RLS missing'; END IF;
END $$;
SET LOCAL ROLE strataai_search_source_ci;
SELECT set_config('app.identity_subject','07600000-0000-0000-0000-000000000001',true);
SELECT append_search_interaction('07600000-0000-0000-0000-000000000030','07600000-0000-0000-0000-000000000001','SEARCH_EXECUTED',null,null,'2026-10-05T12:00:00Z');
SELECT append_search_interaction('07600000-0000-0000-0000-000000000030','07600000-0000-0000-0000-000000000001','SEARCH_EXECUTED',null,null,'2026-10-05T12:00:00Z');
SELECT append_search_interaction('07600000-0000-0000-0000-000000000031','07600000-0000-0000-0000-000000000001','BOARD_FILTER_CHANGED',
 '07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020','2026-10-05T12:00:01Z');
DO $$ DECLARE before_clock timestamptz; BEGIN
 IF (SELECT count(*) FROM search_interaction_events)<>2 OR (SELECT last_sequence FROM search_interaction_streams)<>2
   OR EXISTS(SELECT 1 FROM search_interaction_events WHERE metadata<>'{}' OR entity_id<>event_id OR entity_version<>1)
 THEN RAISE EXCEPTION 'Canonical source identity/deduplication lost'; END IF;
 SELECT updated_at INTO before_clock FROM search_interaction_streams;
 PERFORM append_search_interaction('07600000-0000-0000-0000-000000000030','07600000-0000-0000-0000-000000000001','SEARCH_EXECUTED',null,null,'2026-10-05T12:00:00Z');
 IF (SELECT updated_at FROM search_interaction_streams) IS DISTINCT FROM before_clock THEN
  RAISE EXCEPTION 'Duplicate source changed original stream clock'; END IF;
 BEGIN
  PERFORM append_search_interaction('07600000-0000-0000-0000-000000000030','07600000-0000-0000-0000-000000000001','SEARCH_EXECUTED',null,null,'2026-10-05T12:00:02Z');
  RAISE EXCEPTION 'Changed source identity accepted';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN
  IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF;
 END;
 BEGIN
  UPDATE search_interaction_streams SET last_sequence=99;
  RAISE EXCEPTION 'Direct counter write accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  DELETE FROM search_interaction_events;
  RAISE EXCEPTION 'Runtime source deletion accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
SAVEPOINT declared_late_refusal;
SELECT append_search_interaction('07600000-0000-0000-0000-000000000032','07600000-0000-0000-0000-000000000001','SEARCH_EXECUTED',null,null,'2026-10-05T12:00:03Z');
ROLLBACK TO declared_late_refusal;
DO $$ BEGIN
 IF (SELECT count(*) FROM search_interaction_events)<>2 OR (SELECT last_sequence FROM search_interaction_streams)<>2
 THEN RAISE EXCEPTION 'Late rollback retained source or counter'; END IF;
END $$;
SELECT set_config('app.identity_subject','07600000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM search_interaction_events) OR EXISTS(SELECT 1 FROM search_interaction_streams)
 THEN RAISE EXCEPTION 'Another actor read private history'; END IF;
 BEGIN
  PERFORM append_search_interaction('07600000-0000-0000-0000-000000000033','07600000-0000-0000-0000-000000000001','SEARCH_EXECUTED',null,null,now());
  RAISE EXCEPTION 'Foreign actor source accepted';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF; END;
 BEGIN
  PERFORM append_search_interaction('07600000-0000-0000-0000-000000000034','07600000-0000-0000-0000-000000000002','BOARD_FILTER_CHANGED',
    '07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020',now());
  RAISE EXCEPTION 'Private nonmember source accepted';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF; END;
END $$;
RESET ROLE;
SAVEPOINT retry_receipt_contract;
SET LOCAL ROLE strataai_search_source_ci;
SELECT set_config('app.identity_subject','07600000-0000-0000-0000-000000000001',true);
DO $$ DECLARE result record; stream_clock timestamptz; BEGIN
 SELECT * INTO result FROM append_or_replay_board_filter_interaction('07700000-0000-0000-0000-000000000040',repeat('a',64),
  '07700000-0000-0000-0000-000000000036','07600000-0000-0000-0000-000000000001',
  '07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020','2026-10-05T12:00:04Z');
 IF result.original_event<>'07700000-0000-0000-0000-000000000036' OR result.original_created<>'2026-10-05T12:00:04Z'::timestamptz
  OR (SELECT count(*) FROM board_filter_interaction_replays)<>1 OR (SELECT last_sequence FROM search_interaction_streams)<>3 THEN
  RAISE EXCEPTION 'Retry receipt did not bind the first canonical original'; END IF;
 SELECT updated_at INTO stream_clock FROM search_interaction_streams;
 SELECT * INTO result FROM append_or_replay_board_filter_interaction('07700000-0000-0000-0000-000000000040',repeat('a',64),
  '07700000-0000-0000-0000-000000000037','07600000-0000-0000-0000-000000000001',
  '07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020','2026-10-05T12:00:05Z');
 IF result.original_event<>'07700000-0000-0000-0000-000000000036' OR result.original_created<>'2026-10-05T12:00:04Z'::timestamptz
  OR (SELECT last_sequence FROM search_interaction_streams)<>3 OR (SELECT updated_at FROM search_interaction_streams) IS DISTINCT FROM stream_clock THEN
  RAISE EXCEPTION 'Retry allocated another source or changed its original clock'; END IF;
 BEGIN
  PERFORM append_or_replay_board_filter_interaction('07700000-0000-0000-0000-000000000040',repeat('b',64),
   gen_random_uuid(),'07600000-0000-0000-0000-000000000001','07600000-0000-0000-0000-000000000010',
   '07600000-0000-0000-0000-000000000020',now());
  RAISE EXCEPTION 'Changed retry intent accepted';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF; END;
 BEGIN
  DELETE FROM board_filter_interaction_replays;
  RAISE EXCEPTION 'Direct runtime receipt deletion accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
SAVEPOINT retry_receipt_late_refusal;
SELECT * FROM append_or_replay_board_filter_interaction('07700000-0000-0000-0000-000000000041',repeat('a',64),
 '07700000-0000-0000-0000-000000000037','07600000-0000-0000-0000-000000000001',
 '07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020',now());
ROLLBACK TO retry_receipt_late_refusal;
DO $$ BEGIN
 IF (SELECT count(*) FROM board_filter_interaction_replays)<>1 OR (SELECT last_sequence FROM search_interaction_streams)<>3 THEN
  RAISE EXCEPTION 'Late refusal retained receipt or source'; END IF;
END $$;
SELECT set_config('app.identity_subject','07600000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM board_filter_interaction_replays) THEN RAISE EXCEPTION 'Foreign actor read retry receipt'; END IF;
 BEGIN
  PERFORM append_or_replay_board_filter_interaction('07700000-0000-0000-0000-000000000040',repeat('a',64),
   gen_random_uuid(),'07600000-0000-0000-0000-000000000001','07600000-0000-0000-0000-000000000010',
   '07600000-0000-0000-0000-000000000020',now());
  RAISE EXCEPTION 'Foreign actor replay accepted';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF; END;
END $$;
RESET ROLE;
INSERT INTO board_filter_interaction_replays(actor_id,request_id,event_id,fingerprint,created_at,expires_at) VALUES
 ('07600000-0000-0000-0000-000000000001','07700000-0000-0000-0000-000000000042','07600000-0000-0000-0000-000000000031',
 repeat('a',64),now()-interval '25 hours',now()-interval '1 hour');
SET LOCAL ROLE strataai_search_source_ci;
SELECT set_config('app.identity_subject','07600000-0000-0000-0000-000000000001',true);
DO $$ DECLARE sequence_before bigint; BEGIN
 BEGIN
  PERFORM append_or_replay_board_filter_interaction('07700000-0000-0000-0000-000000000042',repeat('a',64),
   gen_random_uuid(),'07600000-0000-0000-0000-000000000001','07600000-0000-0000-0000-000000000010',
   '07600000-0000-0000-0000-000000000020',now());
  RAISE EXCEPTION 'Expired retry accepted';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF; END;
 FOR i IN 1..999 LOOP
  PERFORM append_or_replay_board_filter_interaction(gen_random_uuid(),repeat('a',64),gen_random_uuid(),
   '07600000-0000-0000-0000-000000000001','07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020',now());
 END LOOP;
 IF (SELECT count(*) FROM board_filter_interaction_replays)<>1000
  OR EXISTS(SELECT 1 FROM board_filter_interaction_replays WHERE expires_at<=clock_timestamp()) THEN
  RAISE EXCEPTION 'Bounded expired-only retry cleanup failed'; END IF;
 SELECT last_sequence INTO sequence_before FROM search_interaction_streams;
 BEGIN
  PERFORM append_or_replay_board_filter_interaction(gen_random_uuid(),repeat('a',64),gen_random_uuid(),
   '07600000-0000-0000-0000-000000000001','07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020',now());
  RAISE EXCEPTION 'Live receipt evicted at capacity';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF; END;
 IF (SELECT last_sequence FROM search_interaction_streams)<>sequence_before THEN RAISE EXCEPTION 'Capacity refusal retained new source'; END IF;
END $$;
RESET ROLE;
UPDATE board_members SET status='REMOVED',version=version+1,updated_at=clock_timestamp()
 WHERE id='07600000-0000-0000-0000-000000000021';
SET LOCAL ROLE strataai_search_source_ci;
DO $$ BEGIN
 BEGIN
  PERFORM append_or_replay_board_filter_interaction('07700000-0000-0000-0000-000000000040',repeat('a',64),
   gen_random_uuid(),'07600000-0000-0000-0000-000000000001','07600000-0000-0000-0000-000000000010',
   '07600000-0000-0000-0000-000000000020',now());
  RAISE EXCEPTION 'Stored receipt bypassed withdrawn Board grant';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF; END;
END $$;
ROLLBACK TO retry_receipt_contract;
RESET ROLE;
UPDATE board_members SET status='REMOVED',version=version+1,updated_at=clock_timestamp()
 WHERE id='07600000-0000-0000-0000-000000000021';
SET LOCAL ROLE strataai_search_source_ci;
SELECT set_config('app.identity_subject','07600000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 BEGIN
  PERFORM append_search_interaction('07600000-0000-0000-0000-000000000035','07600000-0000-0000-0000-000000000001','BOARD_FILTER_CHANGED',
    '07600000-0000-0000-0000-000000000010','07600000-0000-0000-0000-000000000020',now());
  RAISE EXCEPTION 'Withdrawn private grant source accepted';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN IF SQLERRM<>'Search interaction unavailable' THEN RAISE; END IF; END;
 IF (SELECT last_sequence FROM search_interaction_streams)<>2 THEN RAISE EXCEPTION 'Refusal changed private stream'; END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 BEGIN
  UPDATE search_interaction_events SET created_at=created_at+interval '1 second';
  RAISE EXCEPTION 'Administrative history mutation accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
ROLLBACK;
\echo 'Private search source RLS, capability, canonical identity, deduplication, rollback and private grant refusal passed; producers/transport remain separate acceptance.'
