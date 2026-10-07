-- PRD-02/15/24 + ARCH-04: real migrated global identity metadata and restricted
-- ownership writes. This is not current session/Board recipient authorization.
\set ON_ERROR_STOP on
BEGIN;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM users u LEFT JOIN user_mention_handles h ON h.user_id=u.id
  WHERE h.user_id IS NULL) THEN RAISE EXCEPTION 'Existing account handle backfill is incomplete'; END IF;
END $$;
CREATE ROLE strataai_mention_handle_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_mention_handle_ci;
GRANT INSERT ON users TO strataai_mention_handle_ci;
GRANT SELECT ON user_mention_handles TO strataai_mention_handle_ci;
GRANT UPDATE(handle,updated_at,version) ON user_mention_handles TO strataai_mention_handle_ci;
SET LOCAL ROLE strataai_mention_handle_ci;
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at) VALUES
 ('05600000-0000-0000-0000-000000000001','handle-a@example.test','HANDLE-A@EXAMPLE.TEST','Same name','ACTIVE',true,'fixture',now(),now()),
 ('05600000-0000-0000-0000-000000000002','handle-b@example.test','HANDLE-B@EXAMPLE.TEST','Same name','ACTIVE',true,'fixture',now(),now()),
 ('05600000-0000-0000-0000-000000000003','handle-c@example.test','HANDLE-C@EXAMPLE.TEST','C','ACTIVE',true,'fixture',now(),now());
DO $$ DECLARE candidate text; baseline text; baseline_version bigint; affected integer; idx integer; BEGIN
 IF (SELECT count(*) FROM user_mention_handles WHERE user_id IN
  ('05600000-0000-0000-0000-000000000001','05600000-0000-0000-0000-000000000002','05600000-0000-0000-0000-000000000003')
   AND handle='u_'||replace(user_id::text,'-','') AND version=1 AND updated_at=created_at)<>3 THEN
  RAISE EXCEPTION 'Restricted account insertion did not atomically seed canonical defaults'; END IF;
 IF has_table_privilege(current_user,'mention_handle_reservations','SELECT')
  OR has_table_privilege(current_user,'mention_handle_reservations','INSERT')
  OR has_table_privilege(current_user,'mention_handle_reservations','UPDATE')
  OR has_table_privilege(current_user,'mention_handle_reservations','DELETE') THEN
  RAISE EXCEPTION 'Runtime can read or change reserved aliases directly'; END IF;
 IF has_function_privilege(current_user,'public.seed_user_mention_handle()','EXECUTE')
  OR has_function_privilege(current_user,'public.enforce_user_mention_handle_revision()','EXECUTE')
  OR has_function_privilege(current_user,'public.enforce_mention_handle_reservation()','EXECUTE') THEN
  RAISE EXCEPTION 'Runtime has direct trigger capability execution'; END IF;
 BEGIN
  UPDATE user_mention_handles SET handle='no_subject',version=2,updated_at=clock_timestamp()
   WHERE user_id='05600000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Missing subject changed a handle';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 PERFORM set_config('app.identity_subject','05600000-0000-0000-0000-000000000002',true);
 BEGIN
  UPDATE user_mention_handles SET handle='foreign_subject',version=2,updated_at=clock_timestamp()
   WHERE user_id='05600000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Foreign subject changed a handle';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 PERFORM set_config('app.identity_subject','05600000-0000-0000-0000-000000000001',true);
 SELECT to_jsonb(h)::text INTO baseline FROM user_mention_handles h WHERE user_id='05600000-0000-0000-0000-000000000001';
 FOREACH candidate IN ARRAY ARRAY['', 'aa',repeat('x',41),'Nick',' nick','nick ',E'nick\n',
   '1nick','nick-name','nick.name','nïck','card','board','u_other', 'u_05600000000000000000000000000002'] LOOP
  BEGIN
   UPDATE user_mention_handles SET handle=candidate,version=version+1,updated_at=clock_timestamp()
    WHERE user_id='05600000-0000-0000-0000-000000000001';
   RAISE EXCEPTION 'Invalid/reserved handle accepted';
  EXCEPTION WHEN check_violation OR unique_violation THEN NULL; END;
  IF (SELECT to_jsonb(h)::text FROM user_mention_handles h WHERE user_id='05600000-0000-0000-0000-000000000001')<>baseline THEN
   RAISE EXCEPTION 'Refused claim changed current handle'; END IF;
 END LOOP;
 FOREACH candidate IN ARRAY ARRAY['handle=''valid_without_revision''',
  'version=version+1','handle=''valid_wrong_revision'',version=version+2',
  'handle=''valid_past_time'',version=version+1,updated_at=created_at-interval ''1 second''',
  'handle=''valid_infinite_time'',version=version+1,updated_at=''infinity''::timestamptz'] LOOP
  BEGIN
   EXECUTE 'UPDATE user_mention_handles SET '||candidate||' WHERE user_id=''05600000-0000-0000-0000-000000000001''';
   RAISE EXCEPTION 'Invalid revision accepted';
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 BEGIN
  UPDATE user_mention_handles SET user_id='05600000-0000-0000-0000-000000000003'
   WHERE user_id='05600000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Runtime changed identity';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  UPDATE user_mention_handles SET created_at=created_at+interval '1 second'
   WHERE user_id='05600000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Runtime changed creation history';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  INSERT INTO user_mention_handles(user_id,handle,created_at,updated_at)
   VALUES(gen_random_uuid(),'injected',now(),now());
  RAISE EXCEPTION 'Runtime inserted current handle';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  DELETE FROM user_mention_handles WHERE user_id='05600000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Runtime deleted handle history';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 UPDATE user_mention_handles SET handle='handle_alpha',version=2,updated_at=clock_timestamp()
  WHERE user_id='05600000-0000-0000-0000-000000000001' AND version=1;
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>1 THEN RAISE EXCEPTION 'Current claim failed'; END IF;
 UPDATE user_mention_handles SET handle='stale',version=2,updated_at=clock_timestamp()
  WHERE user_id='05600000-0000-0000-0000-000000000001' AND version=1;
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Stale claim changed identity'; END IF;
 UPDATE user_mention_handles SET handle='handle_alpha'
  WHERE user_id='05600000-0000-0000-0000-000000000001';
 IF (SELECT version FROM user_mention_handles WHERE user_id='05600000-0000-0000-0000-000000000001')<>2 THEN
  RAISE EXCEPTION 'Exact no-op advanced revision'; END IF;
 UPDATE user_mention_handles SET handle='handle_beta',version=3,updated_at=clock_timestamp()
  WHERE user_id='05600000-0000-0000-0000-000000000001';
 PERFORM set_config('app.identity_subject','05600000-0000-0000-0000-000000000002',true);
 FOREACH candidate IN ARRAY ARRAY['handle_alpha','handle_beta','u_05600000000000000000000000000001'] LOOP
  BEGIN
   UPDATE user_mention_handles SET handle=candidate,version=2,updated_at=clock_timestamp()
    WHERE user_id='05600000-0000-0000-0000-000000000002';
   RAISE EXCEPTION 'Foreign former/current/default handle reassigned';
  EXCEPTION WHEN unique_violation OR check_violation THEN NULL; END;
 END LOOP;
 PERFORM set_config('app.identity_subject','05600000-0000-0000-0000-000000000001',true);
 UPDATE user_mention_handles SET handle='handle_alpha',version=4,updated_at=clock_timestamp()
  WHERE user_id='05600000-0000-0000-0000-000000000001';
 IF EXISTS(SELECT 1 FROM user_mention_handles WHERE handle='handle_beta') THEN
  RAISE EXCEPTION 'Former alias still resolves as current handle'; END IF;
 PERFORM set_config('app.identity_subject','05600000-0000-0000-0000-000000000003',true);
 FOR idx IN 1..31 LOOP
  UPDATE user_mention_handles SET handle='bounded_'||idx,version=version+1,updated_at=clock_timestamp()
   WHERE user_id='05600000-0000-0000-0000-000000000003';
 END LOOP;
 BEGIN
  UPDATE user_mention_handles SET handle='bounded_overflow',version=version+1,updated_at=clock_timestamp()
   WHERE user_id='05600000-0000-0000-0000-000000000003';
  RAISE EXCEPTION 'Lifetime reservation bound escaped';
 EXCEPTION WHEN check_violation THEN NULL; END;
 UPDATE user_mention_handles SET handle='bounded_1',version=version+1,updated_at=clock_timestamp()
  WHERE user_id='05600000-0000-0000-0000-000000000003';
END $$;
RESET ROLE;
DO $$ DECLARE baseline text; mutation text; BEGIN
 IF (SELECT count(*) FROM mention_handle_reservations WHERE user_id='05600000-0000-0000-0000-000000000001')<>3
  OR (SELECT count(*) FROM mention_handle_reservations WHERE user_id='05600000-0000-0000-0000-000000000003')<>32 THEN
  RAISE EXCEPTION 'Failed claims leaked reservations or same-owner reclaim consumed quota'; END IF;
 FOREACH mutation IN ARRAY ARRAY['user_id=gen_random_uuid()', 'handle=''reassigned''', 'created_at=created_at+interval ''1 second'''] LOOP
  BEGIN
   EXECUTE 'UPDATE mention_handle_reservations SET '||mutation||' WHERE handle=''handle_beta''';
   RAISE EXCEPTION 'Reservation ownership/history changed';
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 UPDATE users SET status='DEACTIVATED',version=version+1 WHERE id='05600000-0000-0000-0000-000000000001';
 IF (SELECT handle FROM user_mention_handles WHERE user_id='05600000-0000-0000-0000-000000000001')<>'handle_alpha'
  OR (SELECT count(*) FROM mention_handle_reservations WHERE user_id='05600000-0000-0000-0000-000000000001')<>3 THEN
  RAISE EXCEPTION 'Account deactivation lost alias ownership'; END IF;
END $$;
SET LOCAL ROLE strataai_mention_handle_ci;
SELECT set_config('app.identity_subject','05600000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 BEGIN
  UPDATE user_mention_handles SET handle='handle_alpha',version=2,updated_at=clock_timestamp()
   WHERE user_id='05600000-0000-0000-0000-000000000002';
  RAISE EXCEPTION 'Deactivated account handle reassigned';
 EXCEPTION WHEN unique_violation THEN NULL; END;
END $$;
RESET ROLE;
-- Even privileged cleanup cannot delete retained account transition history.
-- This synthetic transition has no canonical identity event or routing job.
DO $$ BEGIN
 BEGIN
  DELETE FROM users WHERE id='05600000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Account transition history was deleted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 IF (SELECT count(*) FROM invitation_issuer_authority_proofs WHERE actor_id='05600000-0000-0000-0000-000000000001')<>1
  OR EXISTS(SELECT 1 FROM invitation_issuer_authority_sources WHERE actor_id='05600000-0000-0000-0000-000000000001') THEN
  RAISE EXCEPTION 'Synthetic transition lost its proof or invented a canonical source'; END IF;
END $$;
-- Unreferenced fixtures still exercise both deferred alias cascade edges.
DELETE FROM users WHERE id IN ('05600000-0000-0000-0000-000000000002','05600000-0000-0000-0000-000000000003');
SET CONSTRAINTS ALL IMMEDIATE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM mention_handle_reservations WHERE user_id IN
  ('05600000-0000-0000-0000-000000000002','05600000-0000-0000-0000-000000000003')) THEN
  RAISE EXCEPTION 'Privileged fixture cleanup left orphan aliases'; END IF;
END $$;
ROLLBACK;
\echo 'Mention handle storage: canonical defaults/backfill, subject/CAS guards, immutable ownership, bounded reservations, deactivation retention and restricted privileges passed.'
