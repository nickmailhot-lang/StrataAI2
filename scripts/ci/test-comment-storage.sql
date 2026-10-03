-- PRD-15/ARCH-04: real migrated shape, immutable author/history and forced RLS.
-- Session/Board authorization and transactional command effects are separate.
\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_comment_storage_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_comment_storage_ci;
GRANT SELECT,INSERT,UPDATE ON card_comments TO strataai_comment_storage_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('05500000-0000-0000-0000-000000000001','Comment A',now(),now()),
 ('05500000-0000-0000-0000-000000000002','Comment B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at) VALUES
 ('05500000-0000-0000-0000-000000000041','comment-a@example.test','COMMENT-A@EXAMPLE.TEST','Author A','ACTIVE',true,'fixture',now(),now()),
 ('05500000-0000-0000-0000-000000000042','comment-b@example.test','COMMENT-B@EXAMPLE.TEST','Author B','ACTIVE',true,'fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'05500000-0000-0000-0000-000000000001','05500000-0000-0000-0000-000000000041','MEMBER','ACTIVE'),
 (gen_random_uuid(),'05500000-0000-0000-0000-000000000002','05500000-0000-0000-0000-000000000042','MEMBER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('05500000-0000-0000-0000-000000000011','05500000-0000-0000-0000-000000000001','A Board',now(),now()),
 ('05500000-0000-0000-0000-000000000012','05500000-0000-0000-0000-000000000002','B Board',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('05500000-0000-0000-0000-000000000021','05500000-0000-0000-0000-000000000001','05500000-0000-0000-0000-000000000011','A List','500000000000000000000000000000',now(),now()),
 ('05500000-0000-0000-0000-000000000022','05500000-0000-0000-0000-000000000002','05500000-0000-0000-0000-000000000012','B List','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('05500000-0000-0000-0000-000000000031','05500000-0000-0000-0000-000000000001','05500000-0000-0000-0000-000000000011','05500000-0000-0000-0000-000000000021','A Card','500000000000000000000000000000',now(),now()),
 ('05500000-0000-0000-0000-000000000032','05500000-0000-0000-0000-000000000002','05500000-0000-0000-0000-000000000012','05500000-0000-0000-0000-000000000022','B Card','500000000000000000000000000000',now(),now());
INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at) VALUES
 ('05500000-0000-0000-0000-000000000051','05500000-0000-0000-0000-000000000001','05500000-0000-0000-0000-000000000031','05500000-0000-0000-0000-000000000041',E'Comment\nwith\ttabs',now(),now()),
 ('05500000-0000-0000-0000-000000000052','05500000-0000-0000-0000-000000000002','05500000-0000-0000-0000-000000000032','05500000-0000-0000-0000-000000000042','Private B',now(),now());
SET LOCAL ROLE strataai_comment_storage_ci;
SELECT set_config('app.tenant_id','05500000-0000-0000-0000-000000000001',true);
DO $$ DECLARE affected integer; mutation text; baseline text; body text; BEGIN
 IF (SELECT count(*) FROM card_comments)<>1 THEN RAISE EXCEPTION 'Comment tenant read widened'; END IF;
 SELECT to_jsonb(c)::text INTO baseline FROM card_comments c;
 UPDATE card_comments SET content='Foreign' WHERE id='05500000-0000-0000-0000-000000000052';
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Foreign comment was updated'; END IF;
 BEGIN
  INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
   VALUES(gen_random_uuid(),'05500000-0000-0000-0000-000000000002','05500000-0000-0000-0000-000000000032',
    '05500000-0000-0000-0000-000000000042','Foreign tenant',now(),now());
  RAISE EXCEPTION 'Foreign tenant comment inserted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 FOREACH mutation IN ARRAY ARRAY[
  'id=gen_random_uuid()', 'tenant_id=gen_random_uuid()', 'card_id=gen_random_uuid()', 'author_id=gen_random_uuid()',
  'created_at=created_at+interval ''1 second''', 'content=''Edited without revision''',
  'version=version+1', 'content=''Edited'',version=version+1',
  'content=''Edited'',version=version+2,edited_at=updated_at',
  'content=NULL,deleted_at=updated_at,version=version+1',
  'content=NULL,deleted_at=updated_at,deleted_by=gen_random_uuid(),version=version+1',
  'content=NULL,deleted_at=updated_at,deleted_by=author_id,edited_at=updated_at,version=version+1',
  'content=''Edited'',version=version+1,updated_at=created_at-interval ''1 second'',edited_at=created_at-interval ''1 second'''] LOOP
  BEGIN
   EXECUTE 'UPDATE card_comments SET '||mutation||' WHERE id=''05500000-0000-0000-0000-000000000051''';
   RAISE EXCEPTION 'Invalid comment mutation accepted: %',mutation;
  EXCEPTION WHEN check_violation THEN NULL; END;
  IF (SELECT to_jsonb(c)::text FROM card_comments c)<>baseline THEN RAISE EXCEPTION 'Refused comment changed state'; END IF;
 END LOOP;
 FOREACH body IN ARRAY ARRAY['',E' \n\t ',repeat('x',10001),E'bad\rtext',E'bad\001text',NULL] LOOP
  BEGIN
   INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
    VALUES(gen_random_uuid(),'05500000-0000-0000-0000-000000000001','05500000-0000-0000-0000-000000000031',
      '05500000-0000-0000-0000-000000000041',body,now(),now());
   RAISE EXCEPTION 'Invalid comment body accepted';
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 BEGIN
  INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
   VALUES(gen_random_uuid(),'05500000-0000-0000-0000-000000000001','05500000-0000-0000-0000-000000000032',
    '05500000-0000-0000-0000-000000000041','Foreign Card',now(),now());
  RAISE EXCEPTION 'Foreign Card comment accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
   VALUES(gen_random_uuid(),'05500000-0000-0000-0000-000000000001','05500000-0000-0000-0000-000000000031',
    '05500000-0000-0000-0000-000000000042','Foreign author',now(),now());
  RAISE EXCEPTION 'Foreign author comment accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 UPDATE card_comments SET content='Edited',version=2,updated_at=created_at+interval '1 second',edited_at=created_at+interval '1 second';
 UPDATE card_comments SET content=NULL,version=3,updated_at=created_at+interval '2 seconds',deleted_at=created_at+interval '2 seconds',deleted_by=author_id;
 IF (SELECT content IS NULL AND version=3 AND edited_at=created_at+interval '1 second' AND deleted_by=author_id FROM card_comments) IS NOT TRUE THEN
  RAISE EXCEPTION 'Comment deletion lost redaction or historical attribution'; END IF;
 BEGIN
  UPDATE card_comments SET content='Revived',deleted_at=NULL,deleted_by=NULL,version=4;
  RAISE EXCEPTION 'Deleted comment revived';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  DELETE FROM card_comments;
  RAISE EXCEPTION 'Runtime hard-deleted retained comments';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN IF EXISTS(SELECT 1 FROM card_comments) THEN RAISE EXCEPTION 'Comments disclosed without tenant context'; END IF; END $$;
ROLLBACK;
\echo 'Comment storage: forced tenant RLS, Card/author affinity, bounded content, immutable ownership, revision fences, edited history, redaction, no revival and hard-delete refusal passed.'
