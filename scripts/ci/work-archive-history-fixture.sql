-- Run against the populated migration fixture. No history/data is retained.
BEGIN;
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('11200000-0000-0000-0000-000000000001','archive-history@example.test','ARCHIVE-HISTORY@EXAMPLE.TEST',
 'History fixture','ACTIVE','unused-fixture','2026-10-08T00:00:00Z','2026-10-08T00:00:00Z');
INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
 VALUES('11200000-0000-0000-0000-000000000002','History fixture','11200000-0000-0000-0000-000000000001',
 '2026-10-08T00:00:00Z','2026-10-08T00:00:00Z');
INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 VALUES(gen_random_uuid(),'11200000-0000-0000-0000-000000000002','11200000-0000-0000-0000-000000000001','OWNER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,lifecycle_state,archived_at,created_at,updated_at)
 VALUES('11200000-0000-0000-0000-000000000003','11200000-0000-0000-0000-000000000002','History Board',
 'ARCHIVED','2026-10-08T00:00:01Z','2026-10-08T00:00:00Z','2026-10-08T00:00:01Z');
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,archived_at,created_at,updated_at)
 VALUES('11200000-0000-0000-0000-000000000004','11200000-0000-0000-0000-000000000002','11200000-0000-0000-0000-000000000003',
 'History List',lpad('1000',30,'0'),'ARCHIVED','2026-10-08T00:00:01Z','2026-10-08T00:00:00Z','2026-10-08T00:00:01Z');
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,created_at,updated_at)
 SELECT ('11200000-0000-0000-0000-'||lpad(n::text,12,'0'))::uuid,'11200000-0000-0000-0000-000000000002',
 '11200000-0000-0000-0000-000000000003','11200000-0000-0000-0000-000000000004',
 'History Card',lpad(n::text,30,'0'),'ARCHIVED','2026-10-08T00:00:01Z','2026-10-08T00:00:00Z','2026-10-08T00:00:01Z'
 FROM generate_series(5,6) n;
GRANT SELECT,UPDATE ON boards,board_lists,cards TO strataai_api_runtime;
-- Match runtime grants required by the existing lifecycle route triggers.
GRANT SELECT,INSERT,UPDATE,DELETE ON board_routes,list_routes,card_routes TO strataai_api_runtime;
SET LOCAL ROLE strataai_api_runtime;
SELECT set_config('app.tenant_id','11200000-0000-0000-0000-000000000002',true);
DO $$
DECLARE target text; target_id uuid; before_row jsonb; after_row jsonb; affected integer;
BEGIN
 FOREACH target IN ARRAY ARRAY['boards','board_lists','cards'] LOOP
  target_id=CASE target WHEN 'boards' THEN '11200000-0000-0000-0000-000000000003'::uuid
   WHEN 'board_lists' THEN '11200000-0000-0000-0000-000000000004'::uuid ELSE '11200000-0000-0000-0000-000000000005'::uuid END;
  EXECUTE format('UPDATE %I SET lifecycle_state=''ACTIVE'',updated_at=updated_at+interval ''1 second'',version=version+1 WHERE id=$1',target) USING target_id;
  GET DIAGNOSTICS affected=ROW_COUNT;
  IF affected<>1 THEN RAISE EXCEPTION 'Restricted restoration fixture unavailable'; END IF;
  EXECUTE format('SELECT to_jsonb(r) FROM %I r WHERE id=$1',target) INTO before_row USING target_id;
  IF before_row->>'lifecycle_state'<>'ACTIVE' OR (before_row->>'archived_at')::timestamptz<>'2026-10-08T00:00:01Z'::timestamptz THEN
   RAISE EXCEPTION 'Restoration lost archive history'; END IF;
  BEGIN
   EXECUTE format('UPDATE %I SET archived_at=NULL,version=version+1 WHERE id=$1',target) USING target_id;
   RAISE EXCEPTION 'Archive clearing was admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
  BEGIN
   EXECUTE format('UPDATE %I SET archived_at=archived_at+interval ''1 second'',version=version+1 WHERE id=$1',target) USING target_id;
   RAISE EXCEPTION 'History rewrite was admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
  BEGIN
   EXECUTE format('UPDATE %I SET lifecycle_state=''ARCHIVED'',archived_at=archived_at-interval ''1 second'',updated_at=archived_at-interval ''1 second'',version=version+1 WHERE id=$1',target) USING target_id;
   RAISE EXCEPTION 'Backward archive clock was admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
  EXECUTE format('SELECT to_jsonb(r) FROM %I r WHERE id=$1',target) INTO after_row USING target_id;
  IF after_row IS DISTINCT FROM before_row THEN RAISE EXCEPTION 'Rejected history writes changed work'; END IF;
  EXECUTE format('UPDATE %I SET lifecycle_state=''ARCHIVED'',archived_at=updated_at+interval ''1 second'',updated_at=updated_at+interval ''1 second'',version=version+1 WHERE id=$1',target) USING target_id;
  EXECUTE format('SELECT to_jsonb(r) FROM %I r WHERE id=$1',target) INTO after_row USING target_id;
  IF after_row->>'lifecycle_state'<>'ARCHIVED' OR (after_row->>'archived_at')::timestamptz<>'2026-10-08T00:00:03Z'::timestamptz THEN
   RAISE EXCEPTION 'Rearchive did not advance latest history'; END IF;
 END LOOP;
 -- A late invalid Card row rolls back the whole bulk mutation.
 SELECT jsonb_agg(to_jsonb(c) ORDER BY id) INTO before_row FROM cards c;
 BEGIN
  UPDATE cards SET title='Must roll back',archived_at=CASE WHEN id='11200000-0000-0000-0000-000000000006' THEN NULL ELSE archived_at END;
  RAISE EXCEPTION 'Mixed history mutation was admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 SELECT jsonb_agg(to_jsonb(c) ORDER BY id) INTO after_row FROM cards c;
 IF after_row IS DISTINCT FROM before_row THEN RAISE EXCEPTION 'Mixed history mutation retained effects'; END IF;
 PERFORM set_config('app.tenant_id','11200000-0000-0000-0000-000000000099',true);
 IF EXISTS(SELECT 1 FROM boards WHERE id='11200000-0000-0000-0000-000000000003')
  OR EXISTS(SELECT 1 FROM board_lists WHERE id='11200000-0000-0000-0000-000000000004')
  OR EXISTS(SELECT 1 FROM cards WHERE id='11200000-0000-0000-0000-000000000005') THEN
  RAISE EXCEPTION 'Foreign tenant disclosed archive history'; END IF;
 UPDATE cards SET archived_at=NULL WHERE id='11200000-0000-0000-0000-000000000005';
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Foreign tenant changed archive history'; END IF;
END $$;
ROLLBACK;
