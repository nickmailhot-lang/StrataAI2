-- Actual forced global-subject RLS and narrow Worker cleanup. Session admission
-- and current-handle hydration are separate Application acceptance requirements.
\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_handle_replay_api_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
CREATE ROLE strataai_handle_replay_worker_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_handle_replay_api_ci,strataai_handle_replay_worker_ci;
GRANT SELECT,INSERT ON identity_handle_claim_replays TO strataai_handle_replay_api_ci;
GRANT SELECT(user_id,key_id,expires_at),DELETE ON identity_handle_claim_replays TO strataai_handle_replay_worker_ci;
GRANT EXECUTE ON FUNCTION purge_expired_identity_handle_claim_replays() TO strataai_handle_replay_worker_ci;
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at) VALUES
 ('05700000-0000-0000-0000-000000000001','replay-a@example.test','REPLAY-A@EXAMPLE.TEST','Replay A','ACTIVE',true,'fixture',now(),now()),
 ('05700000-0000-0000-0000-000000000002','replay-b@example.test','REPLAY-B@EXAMPLE.TEST','Replay B','ACTIVE',true,'fixture',now(),now());
INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed) VALUES
 ('05700000-0000-0000-0000-000000000001','05700000-0000-0000-0000-000000000011',repeat('a',64),2,2,true),
 ('05700000-0000-0000-0000-000000000002','05700000-0000-0000-0000-000000000012',repeat('b',64),1,1,false);
INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed,created_at,updated_at,expires_at)
 SELECT '05700000-0000-0000-0000-000000000001',gen_random_uuid(),repeat('c',64),1,1,false,
  statement_timestamp()-interval '2 days',statement_timestamp()-interval '2 days',statement_timestamp()-interval '1 day'
 FROM generate_series(1,150);
DO $$ BEGIN
 IF (SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='identity_handle_claim_replays'::regclass) IS NOT TRUE THEN
  RAISE EXCEPTION 'Handle receipts lost forced RLS'; END IF;
 IF (SELECT array_agg(attname::text ORDER BY attnum) FROM pg_attribute
  WHERE attrelid='identity_handle_claim_replays'::regclass AND attnum>0 AND NOT attisdropped)
   <> ARRAY['user_id','key_id','fingerprint','user_version','handle_version','changed','created_at','updated_at','expires_at'] THEN
  RAISE EXCEPTION 'Handle acknowledgment stores unexpected body/profile/credential fields'; END IF;
END $$;
SET LOCAL ROLE strataai_handle_replay_api_ci;
DO $$ BEGIN
 IF (SELECT count(*) FROM identity_handle_claim_replays)<>0 THEN RAISE EXCEPTION 'Receipt read without subject widened'; END IF;
 BEGIN
  INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed)
   VALUES('05700000-0000-0000-0000-000000000001',gen_random_uuid(),repeat('d',64),2,2,true);
  RAISE EXCEPTION 'Receipt insertion without subject succeeded';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
SELECT set_config('app.identity_subject','05700000-0000-0000-0000-000000000001',true);
DO $$ DECLARE candidate text; affected integer; BEGIN
 IF (SELECT count(*) FROM identity_handle_claim_replays)<>151 THEN RAISE EXCEPTION 'Own receipt scope lost expired/live metadata'; END IF;
 IF EXISTS(SELECT 1 FROM identity_handle_claim_replays WHERE user_id='05700000-0000-0000-0000-000000000002') THEN
  RAISE EXCEPTION 'Foreign receipt read escaped RLS'; END IF;
 BEGIN
  INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed)
   VALUES('05700000-0000-0000-0000-000000000002',gen_random_uuid(),repeat('d',64),2,2,true);
  RAISE EXCEPTION 'Foreign receipt inserted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed)
   VALUES('05700000-0000-0000-0000-000000000001','05700000-0000-0000-0000-000000000011',repeat('d',64),3,3,true);
  RAISE EXCEPTION 'Original key was reassigned';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 FOREACH candidate IN ARRAY ARRAY['',repeat('a',63),repeat('a',65),repeat('A',64),repeat('z',64),'former_username'] LOOP
  BEGIN
   INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed)
    VALUES('05700000-0000-0000-0000-000000000001',gen_random_uuid(),candidate,2,2,true);
   RAISE EXCEPTION 'Noncanonical receipt fingerprint was admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 BEGIN
  INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed)
   VALUES('05700000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000000',repeat('a',64),2,2,true);
  RAISE EXCEPTION 'Empty retry key admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 FOREACH candidate IN ARRAY ARRAY['user_version','handle_version'] LOOP
  BEGIN
   EXECUTE 'INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed) VALUES('
    ||quote_literal('05700000-0000-0000-0000-000000000001')||',gen_random_uuid(),repeat(''a'',64),'
    ||CASE WHEN candidate='user_version' THEN '0,2' ELSE '2,0' END||',true)';
   RAISE EXCEPTION 'Nonpositive receipt revision admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 FOREACH candidate IN ARRAY ARRAY[
  'statement_timestamp()+interval ''23 hours''','statement_timestamp()+interval ''25 hours''','''infinity''::timestamptz'] LOOP
  BEGIN
   EXECUTE 'INSERT INTO identity_handle_claim_replays(user_id,key_id,fingerprint,user_version,handle_version,changed,expires_at) VALUES('
    ||quote_literal('05700000-0000-0000-0000-000000000001')||',gen_random_uuid(),repeat(''a'',64),2,2,true,'||candidate||')';
   RAISE EXCEPTION 'Invalid receipt retention window admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 BEGIN
  UPDATE identity_handle_claim_replays SET fingerprint=repeat('d',64);
  RAISE EXCEPTION 'API changed immutable receipt';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  DELETE FROM identity_handle_claim_replays;
  RAISE EXCEPTION 'API deleted receipt history';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  PERFORM purge_expired_identity_handle_claim_replays();
  RAISE EXCEPTION 'API obtained maintenance capability';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
RESET ROLE;
DO $$ DECLARE mutation text; baseline text; BEGIN
 SELECT to_jsonb(r)::text INTO baseline FROM identity_handle_claim_replays r WHERE key_id='05700000-0000-0000-0000-000000000011';
 FOREACH mutation IN ARRAY ARRAY['fingerprint=repeat(''d'',64)','user_id=''05700000-0000-0000-0000-000000000002''',
  'key_id=gen_random_uuid()','user_version=3','handle_version=3','changed=false',
  'created_at=created_at+interval ''1 second''','updated_at=updated_at+interval ''1 second''','expires_at=expires_at+interval ''1 second'''] LOOP
  BEGIN
   EXECUTE 'UPDATE identity_handle_claim_replays SET '||mutation||' WHERE key_id=''05700000-0000-0000-0000-000000000011''';
   RAISE EXCEPTION 'Receipt identity/history changed';
  EXCEPTION WHEN check_violation THEN NULL; END;
  IF (SELECT to_jsonb(r)::text FROM identity_handle_claim_replays r WHERE key_id='05700000-0000-0000-0000-000000000011')<>baseline THEN
   RAISE EXCEPTION 'Refused receipt mutation changed history'; END IF;
 END LOOP;
END $$;
SET LOCAL ROLE strataai_handle_replay_worker_ci;
DO $$ DECLARE private_column text; affected integer; BEGIN
 IF (SELECT count(user_id) FROM identity_handle_claim_replays)<>0 OR purge_expired_identity_handle_claim_replays()<>0 THEN
  RAISE EXCEPTION 'Cleanup read/purge without service scope widened'; END IF;
 FOREACH private_column IN ARRAY ARRAY['fingerprint','user_version','handle_version','changed','created_at','updated_at'] LOOP
  BEGIN
   EXECUTE 'SELECT '||private_column||' FROM identity_handle_claim_replays LIMIT 1';
   RAISE EXCEPTION 'Worker read private acknowledgment column';
  EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 END LOOP;
 BEGIN
  UPDATE identity_handle_claim_replays SET expires_at=statement_timestamp();
  RAISE EXCEPTION 'Worker expired a live acknowledgment';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 DELETE FROM identity_handle_claim_replays;
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Worker deleted without service scope'; END IF;
END $$;
SELECT set_config('app.service_scope','GLOBAL_IDENTITY_RETRY_CLEANUP',true);
DO $$ DECLARE affected integer; BEGIN
 IF (SELECT count(user_id) FROM identity_handle_claim_replays)<>150 THEN RAISE EXCEPTION 'Cleanup disclosed live receipts via subject spoof'; END IF;
 DELETE FROM identity_handle_claim_replays WHERE expires_at>clock_timestamp();
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Cleanup deleted live acknowledgment'; END IF;
 IF purge_expired_identity_handle_claim_replays()<>100 THEN RAISE EXCEPTION 'Cleanup first page was not bounded to 100'; END IF;
 IF (SELECT count(user_id) FROM identity_handle_claim_replays)<>50 THEN RAISE EXCEPTION 'Cleanup lost its remaining expired page'; END IF;
 IF purge_expired_identity_handle_claim_replays()<>50 OR purge_expired_identity_handle_claim_replays()<>0 THEN
  RAISE EXCEPTION 'Cleanup tail/repeat did not converge'; END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF (SELECT count(*) FROM identity_handle_claim_replays)<>2 THEN RAISE EXCEPTION 'Cleanup changed live original receipts'; END IF;
END $$;
SET CONSTRAINTS ALL IMMEDIATE;
ROLLBACK;
\echo 'Handle claim receipts: body-free immutable revisions, subject RLS, canonical keys/fingerprints, exact retention and expired-only bounded private Worker cleanup passed.'
