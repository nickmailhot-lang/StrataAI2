-- Admission metadata proof only; no HTTP session/live cursor claim.
\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_directory_epochs_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_directory_epochs_ci;
GRANT SELECT ON organization_board_directory_epochs TO strataai_directory_epochs_ci;
GRANT SELECT,INSERT,UPDATE,DELETE ON board_members TO strataai_directory_epochs_ci;
GRANT SELECT,UPDATE ON organization_members TO strataai_directory_epochs_ci;
GRANT SELECT,INSERT,UPDATE,DELETE ON user_organization_access TO strataai_directory_epochs_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('07400000-0000-0000-0000-000000000001','Directory A',now(),now()),
 ('07400000-0000-0000-0000-000000000002','Directory B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at) VALUES
 ('07400000-0000-0000-0000-000000000041','epochs-a@example.test','EPOCHS-A@EXAMPLE.TEST','Subject A','ACTIVE','fixture',now(),now()),
 ('07400000-0000-0000-0000-000000000042','epochs-b@example.test','EPOCHS-B@EXAMPLE.TEST','Subject B','ACTIVE','fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),o.id,u.id,'MEMBER','ACTIVE' FROM organizations o CROSS JOIN users u
 WHERE o.id IN ('07400000-0000-0000-0000-000000000001','07400000-0000-0000-0000-000000000002')
 AND u.id IN ('07400000-0000-0000-0000-000000000041','07400000-0000-0000-0000-000000000042');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('07400000-0000-0000-0000-000000000011','07400000-0000-0000-0000-000000000001','Grant Board',now(),now());
SET LOCAL ROLE strataai_directory_epochs_ci;
SET LOCAL app.tenant_id='07400000-0000-0000-0000-000000000001';
DO $$ DECLARE before_row jsonb; unchanged_b jsonb; next_revision bigint; BEGIN
 IF (SELECT count(*) FROM organization_board_directory_epochs)<>2 THEN RAISE EXCEPTION 'Epoch tenant RLS failed'; END IF;
 SELECT (to_jsonb(e)-'reader_revision'-'reader_updated_at') INTO before_row FROM organization_board_directory_epochs e
 WHERE user_id='07400000-0000-0000-0000-000000000041';
 SELECT (to_jsonb(e)-'reader_revision'-'reader_updated_at') INTO unchanged_b FROM organization_board_directory_epochs e
 WHERE user_id='07400000-0000-0000-0000-000000000042';
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES
 ('07400000-0000-0000-0000-000000000051','07400000-0000-0000-0000-000000000001',
 '07400000-0000-0000-0000-000000000011','07400000-0000-0000-0000-000000000041','MEMBER','ACTIVE',now(),now());
 IF (SELECT (to_jsonb(e)-'reader_revision'-'reader_updated_at') FROM organization_board_directory_epochs e WHERE user_id='07400000-0000-0000-0000-000000000041') IS DISTINCT FROM before_row THEN
  RAISE EXCEPTION 'Nonadministrative membership changed directory epoch'; END IF;
 UPDATE board_members SET role='ADMIN' WHERE id='07400000-0000-0000-0000-000000000051';
 IF (SELECT permission_revision FROM organization_board_directory_epochs WHERE user_id='07400000-0000-0000-0000-000000000041')<>2 THEN
  RAISE EXCEPTION 'Administration expansion did not revise subject epoch'; END IF;
 UPDATE board_members SET role='MEMBER' WHERE id='07400000-0000-0000-0000-000000000051';
 IF (SELECT permission_revision FROM organization_board_directory_epochs WHERE user_id='07400000-0000-0000-0000-000000000041')<>3 THEN
  RAISE EXCEPTION 'Administration withdrawal did not revise subject epoch'; END IF;
 IF (SELECT (to_jsonb(e)-'reader_revision'-'reader_updated_at') FROM organization_board_directory_epochs e WHERE user_id='07400000-0000-0000-0000-000000000042') IS DISTINCT FROM unchanged_b THEN
  RAISE EXCEPTION 'Another actor grant activity changed subject epoch'; END IF;
 SELECT (to_jsonb(e)-'reader_revision'-'reader_updated_at') INTO before_row FROM organization_board_directory_epochs e WHERE user_id='07400000-0000-0000-0000-000000000041';
 BEGIN
  UPDATE board_members SET role='ADMIN' WHERE id='07400000-0000-0000-0000-000000000051';
  IF (SELECT permission_revision FROM organization_board_directory_epochs WHERE user_id='07400000-0000-0000-0000-000000000041')<>4 THEN
   RAISE EXCEPTION 'Declared rollback did not reach epoch'; END IF;
  RAISE EXCEPTION 'Declared late refusal' USING ERRCODE='23514';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF (SELECT (to_jsonb(e)-'reader_revision'-'reader_updated_at') FROM organization_board_directory_epochs e WHERE user_id='07400000-0000-0000-0000-000000000041') IS DISTINCT FROM before_row THEN
  RAISE EXCEPTION 'Owning rollback retained epoch revision or clock'; END IF;
 UPDATE organization_members SET role='ADMIN',status='REMOVED',version=version+1,updated_at=clock_timestamp()
 WHERE tenant_id='07400000-0000-0000-0000-000000000001' AND user_id='07400000-0000-0000-0000-000000000041';
 UPDATE organization_members SET status='ACTIVE',version=version+1,updated_at=clock_timestamp()
 WHERE tenant_id='07400000-0000-0000-0000-000000000001' AND user_id='07400000-0000-0000-0000-000000000041';
 SELECT permission_revision INTO next_revision FROM organization_board_directory_epochs WHERE user_id='07400000-0000-0000-0000-000000000041';
 IF next_revision<>5 THEN RAISE EXCEPTION 'Organization withdrawal/restoration reused permission revision'; END IF;
 UPDATE organization_members SET role=role,status=status
 WHERE tenant_id='07400000-0000-0000-0000-000000000001' AND user_id='07400000-0000-0000-0000-000000000041';
 IF (SELECT permission_revision FROM organization_board_directory_epochs WHERE user_id='07400000-0000-0000-0000-000000000041')<>next_revision THEN
  RAISE EXCEPTION 'No-op membership write manufactured a revision'; END IF;
 BEGIN
  UPDATE organization_board_directory_epochs SET permission_revision=99;
  RAISE EXCEPTION 'Runtime directly changed admission metadata';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  PERFORM revise_board_directory_membership();
  RAISE EXCEPTION 'Runtime directly invoked epoch capability';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
RESET ROLE;
-- Hard membership replacement must get a fresh generation even if a trusted
-- fixture reuses identity. The cascade also keeps administrative cleanup valid.
DO $$ DECLARE original_generation uuid; BEGIN
 SELECT generation INTO original_generation FROM organization_board_directory_epochs
 WHERE tenant_id='07400000-0000-0000-0000-000000000002' AND user_id='07400000-0000-0000-0000-000000000041';
 DELETE FROM organization_members WHERE tenant_id='07400000-0000-0000-0000-000000000002' AND user_id='07400000-0000-0000-0000-000000000041';
 INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),
 '07400000-0000-0000-0000-000000000002','07400000-0000-0000-0000-000000000041','MEMBER','ACTIVE');
 IF (SELECT generation FROM organization_board_directory_epochs
  WHERE tenant_id='07400000-0000-0000-0000-000000000002' AND user_id='07400000-0000-0000-0000-000000000041')=original_generation THEN
  RAISE EXCEPTION 'Replacement membership reused permission generation'; END IF;
END $$;
ROLLBACK;
\echo 'Directory permission epochs: tenant isolation, actor-only administration changes, owning rollback, Organization withdrawal/restore, no-op/direct-write/capability refusal and replacement generation passed.'
