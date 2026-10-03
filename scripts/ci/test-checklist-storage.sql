\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_checklist_storage_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_checklist_storage_ci;
GRANT SELECT,INSERT,UPDATE ON checklists,checklist_items TO strataai_checklist_storage_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('04000000-0000-0000-0000-000000000001','Checklist A',now(),now()),
 ('04000000-0000-0000-0000-000000000002','Checklist B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('04000000-0000-0000-0000-000000000041','checklist-storage@example.test','CHECKLIST-STORAGE@EXAMPLE.TEST','Checklist fixture','ACTIVE',true,'unused-checklist-hash',now(),now()),
 ('04000000-0000-0000-0000-000000000042','checklist-foreign@example.test','CHECKLIST-FOREIGN@EXAMPLE.TEST','Foreign fixture','ACTIVE',true,'unused-checklist-hash',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000041','MEMBER','ACTIVE'),
 (gen_random_uuid(),'04000000-0000-0000-0000-000000000002','04000000-0000-0000-0000-000000000042','MEMBER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('04000000-0000-0000-0000-000000000011','04000000-0000-0000-0000-000000000001','A Board',now(),now()),
 ('04000000-0000-0000-0000-000000000012','04000000-0000-0000-0000-000000000002','B Board',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('04000000-0000-0000-0000-000000000021','04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000011','A List','500000000000000000000000000000',now(),now()),
 ('04000000-0000-0000-0000-000000000022','04000000-0000-0000-0000-000000000002','04000000-0000-0000-0000-000000000012','B List','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('04000000-0000-0000-0000-000000000031','04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000011','04000000-0000-0000-0000-000000000021','A Card','500000000000000000000000000000',now(),now()),
 ('04000000-0000-0000-0000-000000000032','04000000-0000-0000-0000-000000000002','04000000-0000-0000-0000-000000000012','04000000-0000-0000-0000-000000000022','B Card','500000000000000000000000000000',now(),now());
INSERT INTO checklists(id,tenant_id,card_id,title,rank,created_at,updated_at) VALUES
 ('04000000-0000-0000-0000-000000000051','04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000031','First','500000000000000000000000000000',now(),now()),
 ('04000000-0000-0000-0000-000000000052','04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000031','Second','600000000000000000000000000000',now(),now()),
 ('04000000-0000-0000-0000-000000000053','04000000-0000-0000-0000-000000000002','04000000-0000-0000-0000-000000000032','Other tenant','500000000000000000000000000000',now(),now());
INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,created_at,updated_at) VALUES
 ('04000000-0000-0000-0000-000000000061','04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000051','First item','500000000000000000000000000000',now(),now()),
 ('04000000-0000-0000-0000-000000000062','04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000051','Second item','600000000000000000000000000000',now(),now()),
 ('04000000-0000-0000-0000-000000000063','04000000-0000-0000-0000-000000000002','04000000-0000-0000-0000-000000000053','Foreign item','500000000000000000000000000000',now(),now());
SET LOCAL ROLE strataai_checklist_storage_ci;
SELECT set_config('app.tenant_id','04000000-0000-0000-0000-000000000001',true);
DO $$ DECLARE affected integer; BEGIN
 IF (SELECT count(*) FROM checklists)<>2 OR (SELECT count(*) FROM checklist_items)<>2 THEN RAISE EXCEPTION 'Checklist tenant reads widened'; END IF;
 IF (SELECT array_agg(title ORDER BY rank) FROM checklists) <> ARRAY['First','Second'] THEN RAISE EXCEPTION 'Checklist ordering changed'; END IF;
 UPDATE checklist_items SET text='Forbidden' WHERE id='04000000-0000-0000-0000-000000000063';
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Foreign item was updated'; END IF;
 BEGIN
  UPDATE checklists SET tenant_id='04000000-0000-0000-0000-000000000002' WHERE id='04000000-0000-0000-0000-000000000051';
  RAISE EXCEPTION 'Cross-tenant Checklist write accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  INSERT INTO checklists(id,tenant_id,card_id,title,rank,created_at,updated_at) VALUES
   (gen_random_uuid(),'04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000032','Foreign Card','700000000000000000000000000000',now(),now());
  RAISE EXCEPTION 'Cross-tenant Card parent accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,created_at,updated_at) VALUES
   (gen_random_uuid(),'04000000-0000-0000-0000-000000000001','04000000-0000-0000-0000-000000000053','Foreign Checklist','700000000000000000000000000000',now(),now());
  RAISE EXCEPTION 'Cross-tenant Checklist parent accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE checklists SET rank='600000000000000000000000000000' WHERE id='04000000-0000-0000-0000-000000000051';
  RAISE EXCEPTION 'Duplicate active Checklist rank accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET rank='600000000000000000000000000000' WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Duplicate active item rank accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  UPDATE checklists SET rank='000000000000000000000000000000' WHERE id='04000000-0000-0000-0000-000000000051';
  RAISE EXCEPTION 'Invalid Checklist rank accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET completed=true,completed_at=now(),completed_by='04000000-0000-0000-0000-000000000042',updated_at=now() WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Cross-tenant completion attribution accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET completed=true WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Completion without attribution accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET completed_at=now(),completed_by='04000000-0000-0000-0000-000000000041' WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Uncompleted item retained attribution';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET version=0 WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Invalid Checklist item revision accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET completed=true,completed_at=now()+interval '1 second',completed_by='04000000-0000-0000-0000-000000000041',updated_at=now() WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Completion timestamp beyond update accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklists SET title=repeat('x',161) WHERE id='04000000-0000-0000-0000-000000000051';
  RAISE EXCEPTION 'Unbounded Checklist title accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET text=repeat('x',2001) WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Unbounded Checklist item text accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET updated_at=created_at-interval '1 second' WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Backwards item timestamp accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklists SET deleted_at=created_at-interval '1 second' WHERE id='04000000-0000-0000-0000-000000000051';
  RAISE EXCEPTION 'Backwards Checklist deletion timestamp accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE checklist_items SET text=E'\t\n' WHERE id='04000000-0000-0000-0000-000000000061';
  RAISE EXCEPTION 'Blank item text accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
UPDATE checklist_items SET completed=true,completed_at=now(),completed_by='04000000-0000-0000-0000-000000000041',updated_at=now(),version=version+1
 WHERE id='04000000-0000-0000-0000-000000000061';
DO $$ BEGIN
 IF (SELECT count(*) FILTER(WHERE completed)*100.0/NULLIF(count(*),0) FROM checklist_items WHERE checklist_id='04000000-0000-0000-0000-000000000051' AND deleted_at IS NULL)<>50 THEN RAISE EXCEPTION 'Checklist progress differs from canonical item counts'; END IF;
END $$;
UPDATE checklist_items SET deleted_at=now(),updated_at=now(),version=version+1 WHERE id='04000000-0000-0000-0000-000000000062';
UPDATE checklist_items SET rank='600000000000000000000000000000',updated_at=now(),version=version+1 WHERE id='04000000-0000-0000-0000-000000000061';
DO $$ BEGIN
 IF (SELECT count(*) FILTER(WHERE completed)*100.0/NULLIF(count(*),0) FROM checklist_items WHERE checklist_id='04000000-0000-0000-0000-000000000051' AND deleted_at IS NULL)<>100 THEN RAISE EXCEPTION 'Deleted item remained in progress denominator'; END IF;
 IF (SELECT COALESCE(count(*) FILTER(WHERE completed)*100.0/NULLIF(count(*),0),0) FROM checklist_items WHERE checklist_id='04000000-0000-0000-0000-000000000052' AND deleted_at IS NULL)<>0 THEN RAISE EXCEPTION 'Empty Checklist progress is not zero'; END IF;
END $$;
SELECT set_config('app.tenant_id','04000000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM checklists)<>1 OR (SELECT count(*) FROM checklist_items)<>1 THEN RAISE EXCEPTION 'Second tenant scope widened'; END IF;
 IF (SELECT text FROM checklist_items)<>'Foreign item' THEN RAISE EXCEPTION 'Foreign tenant item changed'; END IF;
END $$;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM checklists) OR EXISTS(SELECT 1 FROM checklist_items) THEN RAISE EXCEPTION 'Missing tenant exposed Checklist content'; END IF;
END $$;
RESET ROLE;
ROLLBACK;
\echo 'Checklist storage: forced tenant RLS, composite parent integrity, ranks, attribution, timestamps, tombstone progress and rollback passed.'
