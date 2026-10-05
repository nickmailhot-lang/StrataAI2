-- Reader admission foundation, not HTTP/realtime acceptance evidence.
\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_reader_epochs_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_reader_epochs_ci;
GRANT SELECT ON organization_board_directory_epochs TO strataai_reader_epochs_ci;
GRANT SELECT,INSERT,UPDATE,DELETE ON board_members TO strataai_reader_epochs_ci;
GRANT SELECT,UPDATE ON boards TO strataai_reader_epochs_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('07500000-0000-0000-0000-000000000001','Reader A',now(),now()),
 ('07500000-0000-0000-0000-000000000002','Reader B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at) VALUES
 ('07500000-0000-0000-0000-000000000041','reader-a@example.test','READER-A@EXAMPLE.TEST','Reader A','ACTIVE','fixture',now(),now()),
 ('07500000-0000-0000-0000-000000000042','reader-b@example.test','READER-B@EXAMPLE.TEST','Reader B','ACTIVE','fixture',now(),now()),
 ('07500000-0000-0000-0000-000000000043','reader-c@example.test','READER-C@EXAMPLE.TEST','Reader admin','ACTIVE','fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),o.id,u.id,CASE WHEN u.id='07500000-0000-0000-0000-000000000043' THEN 'ADMIN' ELSE 'MEMBER' END,'ACTIVE'
 FROM organizations o CROSS JOIN users u WHERE o.id IN
 ('07500000-0000-0000-0000-000000000001','07500000-0000-0000-0000-000000000002')
 AND u.id IN ('07500000-0000-0000-0000-000000000041','07500000-0000-0000-0000-000000000042','07500000-0000-0000-0000-000000000043');
INSERT INTO boards(id,tenant_id,name,visibility,created_at,updated_at) VALUES
 ('07500000-0000-0000-0000-000000000011','07500000-0000-0000-0000-000000000001','Private reader Board','PRIVATE',now(),now());
SET LOCAL ROLE strataai_reader_epochs_ci;
SET LOCAL app.tenant_id='07500000-0000-0000-0000-000000000001';
DO $$ DECLARE before_row jsonb; archive_row jsonb; BEGIN
 IF (SELECT count(*) FROM organization_board_directory_epochs)<>3 THEN RAISE EXCEPTION 'Reader epoch RLS leaked another tenant'; END IF;
 SELECT to_jsonb(e) INTO before_row FROM organization_board_directory_epochs e WHERE user_id='07500000-0000-0000-0000-000000000042';
 SELECT to_jsonb(e)-'reader_revision'-'reader_updated_at' INTO archive_row FROM organization_board_directory_epochs e WHERE user_id='07500000-0000-0000-0000-000000000041';
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES
 ('07500000-0000-0000-0000-000000000051','07500000-0000-0000-0000-000000000001','07500000-0000-0000-0000-000000000011','07500000-0000-0000-0000-000000000041','MEMBER','ACTIVE',now(),now());
 IF (SELECT reader_revision FROM organization_board_directory_epochs WHERE user_id='07500000-0000-0000-0000-000000000041')<>2 THEN RAISE EXCEPTION 'Member grant did not revise reader admission'; END IF;
 IF (SELECT to_jsonb(e)-'reader_revision'-'reader_updated_at' FROM organization_board_directory_epochs e WHERE user_id='07500000-0000-0000-0000-000000000041') IS DISTINCT FROM archive_row THEN RAISE EXCEPTION 'Member grant changed archive admission'; END IF;
 IF (SELECT to_jsonb(e) FROM organization_board_directory_epochs e WHERE user_id='07500000-0000-0000-0000-000000000042') IS DISTINCT FROM before_row THEN RAISE EXCEPTION 'Personal member grant leaked another actor activity'; END IF;
 UPDATE board_members SET role='ADMIN' WHERE id='07500000-0000-0000-0000-000000000051';
 UPDATE board_members SET role='MEMBER' WHERE id='07500000-0000-0000-0000-000000000051';
 UPDATE board_members SET status=status WHERE id='07500000-0000-0000-0000-000000000051';
 IF (SELECT reader_revision FROM organization_board_directory_epochs WHERE user_id='07500000-0000-0000-0000-000000000041')<>2 THEN RAISE EXCEPTION 'Unchanged read eligibility manufactured revision'; END IF;
 UPDATE board_members SET status='REMOVED' WHERE id='07500000-0000-0000-0000-000000000051';
 IF (SELECT reader_revision FROM organization_board_directory_epochs WHERE user_id='07500000-0000-0000-0000-000000000041')<>3 THEN RAISE EXCEPTION 'Reader withdrawal missed admission revision'; END IF;
 SELECT to_jsonb(e) INTO before_row FROM organization_board_directory_epochs e WHERE user_id='07500000-0000-0000-0000-000000000041';
 BEGIN
  UPDATE board_members SET status='ACTIVE' WHERE id='07500000-0000-0000-0000-000000000051';
  IF (SELECT reader_revision FROM organization_board_directory_epochs WHERE user_id='07500000-0000-0000-0000-000000000041')<>4 THEN RAISE EXCEPTION 'Rollback fixture missed revision'; END IF;
  RAISE EXCEPTION 'Declared reader rollback' USING ERRCODE='23514';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF (SELECT to_jsonb(e) FROM organization_board_directory_epochs e WHERE user_id='07500000-0000-0000-0000-000000000041') IS DISTINCT FROM before_row THEN RAISE EXCEPTION 'Rollback retained reader revision or clock'; END IF;
 UPDATE boards SET name='Private edit',visibility=visibility WHERE id='07500000-0000-0000-0000-000000000011';
 IF (SELECT reader_revision FROM organization_board_directory_epochs WHERE user_id='07500000-0000-0000-0000-000000000042')<>1 THEN RAISE EXCEPTION 'Private edit disclosed hidden activity'; END IF;
 UPDATE boards SET visibility='PUBLIC' WHERE id='07500000-0000-0000-0000-000000000011';
 UPDATE boards SET visibility='PRIVATE' WHERE id='07500000-0000-0000-0000-000000000011';
 IF (SELECT reader_revision FROM organization_board_directory_epochs WHERE user_id='07500000-0000-0000-0000-000000000042')<>3 THEN RAISE EXCEPTION 'Visibility admission changes missed ordinary reader'; END IF;
 IF (SELECT reader_revision FROM organization_board_directory_epochs WHERE user_id='07500000-0000-0000-0000-000000000043')<>1 THEN RAISE EXCEPTION 'Visibility change revised always-admitted administrator'; END IF;
 BEGIN
  UPDATE organization_board_directory_epochs SET reader_revision=99;
  RAISE EXCEPTION 'Runtime directly changed reader admission';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  PERFORM revise_board_reader_visibility();
  RAISE EXCEPTION 'Runtime directly invoked reader capability';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM organization_board_directory_epochs WHERE tenant_id='07500000-0000-0000-0000-000000000002' AND reader_revision<>1) THEN RAISE EXCEPTION 'Reader revision crossed tenant boundary'; END IF;
END $$;
ROLLBACK;
\echo 'Reader epochs: tenant isolation, personal reader admission, archive separation, eligibility/no-op, rollback, visibility transitions and runtime capability refusal passed.'
