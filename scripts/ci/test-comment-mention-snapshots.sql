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
GRANT SELECT,INSERT,UPDATE ON comment_mention_snapshots,comment_mention_recipients TO strataai_comment_storage_ci;
INSERT INTO comment_mention_snapshots(tenant_id,comment_id,card_id,comment_version,recipient_count,created_at)
 SELECT tenant_id,id,card_id,version,CASE WHEN content='Private B' THEN 0 ELSE 1 END,updated_at FROM card_comments;
INSERT INTO comment_mention_recipients(tenant_id,comment_id,card_id,comment_version,recipient_id)
 SELECT tenant_id,id,card_id,version,author_id FROM card_comments WHERE content<>'Private B';
SET CONSTRAINTS ALL IMMEDIATE;
SET CONSTRAINTS ALL DEFERRED;
SET LOCAL ROLE strataai_comment_storage_ci;
SELECT set_config('app.tenant_id','05500000-0000-0000-0000-000000000001',true);
DO $$ DECLARE statement text; baseline text; BEGIN
 IF (SELECT count(*) FROM comment_mention_snapshots)<>1 OR (SELECT count(*) FROM comment_mention_recipients)<>1 THEN
  RAISE EXCEPTION 'Mention snapshot tenant read widened'; END IF;
 SELECT jsonb_build_object('snapshots',(SELECT jsonb_agg(to_jsonb(s)) FROM comment_mention_snapshots s),
  'recipients',(SELECT jsonb_agg(to_jsonb(r)) FROM comment_mention_recipients r))::text INTO baseline;
 FOREACH statement IN ARRAY ARRAY[
  'UPDATE comment_mention_snapshots SET recipient_count=0',
  'UPDATE comment_mention_snapshots SET comment_version=2',
  'UPDATE comment_mention_snapshots SET created_at=created_at+interval ''1 second''',
  'UPDATE comment_mention_recipients SET recipient_id=''05500000-0000-0000-0000-000000000042'''
 ] LOOP
  BEGIN EXECUTE statement; RAISE EXCEPTION 'Immutable mention metadata changed';
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 BEGIN
  INSERT INTO comment_mention_snapshots SELECT tenant_id,id,card_id,version+1,0,updated_at FROM card_comments;
  RAISE EXCEPTION 'Future comment revision acquired mention snapshot';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO comment_mention_recipients SELECT tenant_id,id,card_id,version,'05500000-0000-0000-0000-000000000042'::uuid FROM card_comments;
  RAISE EXCEPTION 'Foreign Organization recipient acquired mention reference';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  INSERT INTO comment_mention_recipients SELECT tenant_id,id,card_id,version,author_id FROM card_comments;
  RAISE EXCEPTION 'Duplicate mention recipient acquired second reference';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  INSERT INTO comment_mention_snapshots VALUES('05500000-0000-0000-0000-000000000002',
   '05500000-0000-0000-0000-000000000052','05500000-0000-0000-0000-000000000032',1,0,now());
  RAISE EXCEPTION 'Foreign snapshot write accepted';
 EXCEPTION WHEN check_violation OR insufficient_privilege THEN NULL; END;
 BEGIN DELETE FROM comment_mention_snapshots; RAISE EXCEPTION 'Runtime snapshot delete accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN DELETE FROM comment_mention_recipients; RAISE EXCEPTION 'Runtime recipient delete accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 IF baseline IS DISTINCT FROM jsonb_build_object('snapshots',(SELECT jsonb_agg(to_jsonb(s)) FROM comment_mention_snapshots s),
  'recipients',(SELECT jsonb_agg(to_jsonb(r)) FROM comment_mention_recipients r))::text THEN
  RAISE EXCEPTION 'Rejected snapshot mutation changed state'; END IF;
 BEGIN
  UPDATE card_comments SET content='Edited',version=2,edited_at=created_at+interval '1 second',updated_at=created_at+interval '1 second';
  INSERT INTO comment_mention_snapshots SELECT tenant_id,id,card_id,version,1,updated_at FROM card_comments;
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Incomplete snapshot became durable';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF (SELECT version FROM card_comments)<>1 THEN RAISE EXCEPTION 'Incomplete snapshot did not roll back comment revision'; END IF;
 SET CONSTRAINTS ALL DEFERRED;
 UPDATE card_comments SET content='Edited',version=2,edited_at=created_at+interval '1 second',updated_at=created_at+interval '1 second';
 INSERT INTO comment_mention_snapshots SELECT tenant_id,id,card_id,version,0,updated_at FROM card_comments;
 SET CONSTRAINTS ALL IMMEDIATE;
 IF (SELECT count(*) FROM comment_mention_snapshots)<>2 OR (SELECT count(*) FROM comment_mention_recipients)<>1 THEN
  RAISE EXCEPTION 'Empty current snapshot lost former stable recipient history'; END IF;
END $$;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM comment_mention_snapshots) OR EXISTS(SELECT 1 FROM comment_mention_recipients) THEN
  RAISE EXCEPTION 'Mention metadata disclosed without owning tenant'; END IF;
END $$;
ROLLBACK;
\echo 'Mention snapshots: forced tenant RLS, immutable revision/history, exact cardinality, empty/current snapshots, recipient affinity, duplicate and foreign-write denial and incomplete rollback passed.'