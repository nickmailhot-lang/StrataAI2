\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_label_storage_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_label_storage_ci;
GRANT SELECT,INSERT,UPDATE,DELETE ON board_labels,card_labels TO strataai_label_storage_ci;
GRANT SELECT,INSERT,UPDATE,DELETE ON label_routes TO strataai_label_storage_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('02800000-0000-0000-0000-000000000001','Label A',now(),now()),
 ('02800000-0000-0000-0000-000000000002','Label B',now(),now());
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('02800000-0000-0000-0000-000000000011','02800000-0000-0000-0000-000000000001','A one',now(),now()),
 ('02800000-0000-0000-0000-000000000012','02800000-0000-0000-0000-000000000001','A two',now(),now()),
 ('02800000-0000-0000-0000-000000000013','02800000-0000-0000-0000-000000000002','B one',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('02800000-0000-0000-0000-000000000021','02800000-0000-0000-0000-000000000001','02800000-0000-0000-0000-000000000011','A List','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('02800000-0000-0000-0000-000000000031','02800000-0000-0000-0000-000000000001','02800000-0000-0000-0000-000000000011','02800000-0000-0000-0000-000000000021','A Card','500000000000000000000000000000',now(),now());
INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank) VALUES
 ('02800000-0000-0000-0000-000000000041','02800000-0000-0000-0000-000000000001','02800000-0000-0000-0000-000000000011','','green','500000000000000000000000000000'),
 ('02800000-0000-0000-0000-000000000042','02800000-0000-0000-0000-000000000001','02800000-0000-0000-0000-000000000012','Other Board','blue','500000000000000000000000000000'),
 ('02800000-0000-0000-0000-000000000043','02800000-0000-0000-0000-000000000002','02800000-0000-0000-0000-000000000013','Other Organization','red','500000000000000000000000000000');
SET LOCAL ROLE strataai_label_storage_ci;
SELECT set_config('app.tenant_id','02800000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM board_labels) <> 2 THEN RAISE EXCEPTION 'Label tenant reads widened'; END IF;
END $$;
INSERT INTO card_labels(tenant_id,board_id,card_id,label_id) VALUES
 ('02800000-0000-0000-0000-000000000001','02800000-0000-0000-0000-000000000011','02800000-0000-0000-0000-000000000031','02800000-0000-0000-0000-000000000041');
DO $$ BEGIN
 BEGIN
  INSERT INTO card_labels(tenant_id,board_id,card_id,label_id) VALUES
   ('02800000-0000-0000-0000-000000000001','02800000-0000-0000-0000-000000000011','02800000-0000-0000-0000-000000000031','02800000-0000-0000-0000-000000000041');
  RAISE EXCEPTION 'Duplicate Card label was accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  INSERT INTO card_labels(tenant_id,board_id,card_id,label_id) VALUES
   ('02800000-0000-0000-0000-000000000001','02800000-0000-0000-0000-000000000011','02800000-0000-0000-0000-000000000031','02800000-0000-0000-0000-000000000042');
  RAISE EXCEPTION 'Cross-Board label was accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  INSERT INTO card_labels(tenant_id,board_id,card_id,label_id) VALUES
   ('02800000-0000-0000-0000-000000000001','02800000-0000-0000-0000-000000000011','02800000-0000-0000-0000-000000000031','02800000-0000-0000-0000-000000000043');
  RAISE EXCEPTION 'Cross-Organization label was accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE board_labels SET color='private-invalid-input' WHERE id='02800000-0000-0000-0000-000000000041';
  RAISE EXCEPTION 'Invalid label color was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE board_labels SET version=0 WHERE id='02800000-0000-0000-0000-000000000041';
  RAISE EXCEPTION 'Invalid label revision was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE board_labels SET status='DELETED' WHERE id='02800000-0000-0000-0000-000000000041';
  RAISE EXCEPTION 'Missing deletion timestamp was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE board_labels SET tenant_id='02800000-0000-0000-0000-000000000002' WHERE id='02800000-0000-0000-0000-000000000042';
  RAISE EXCEPTION 'Cross-Organization label write was accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
SELECT set_config('app.tenant_id','02800000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM board_labels) <> 1 OR (SELECT count(*) FROM card_labels) <> 0 THEN RAISE EXCEPTION 'Label association tenant reads widened'; END IF;
END $$;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS (SELECT 1 FROM board_labels) OR EXISTS (SELECT 1 FROM card_labels) THEN RAISE EXCEPTION 'Missing tenant exposed labels'; END IF;
 IF EXISTS (SELECT 1 FROM label_routes) THEN RAISE EXCEPTION 'Missing route scope exposed labels'; END IF;
END $$;
SELECT set_config('app.route_kind','LABEL',true),set_config('app.route_key','02800000-0000-0000-0000-000000000041',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM label_routes) <> 1 THEN RAISE EXCEPTION 'Typed label lookup did not select one route'; END IF;
 IF EXISTS (SELECT 1 FROM board_labels) THEN RAISE EXCEPTION 'Label lookup exposed protected records'; END IF;
END $$;
SELECT set_config('app.route_kind','CARD',true);
DO $$ BEGIN
 IF EXISTS (SELECT 1 FROM label_routes) THEN RAISE EXCEPTION 'Wrong lookup type exposed a label route'; END IF;
END $$;
SELECT set_config('app.route_kind','LABEL',true),set_config('app.route_key','',true);
DO $$ BEGIN
 IF EXISTS (SELECT 1 FROM label_routes) THEN RAISE EXCEPTION 'Blank lookup exposed label routes'; END IF;
END $$;
RESET ROLE;
ROLLBACK;
\echo 'Board label storage: forced RLS, same-Board/Organization FKs, unique associations, constrained revisions/colors/lifecycle and rollback passed.'
