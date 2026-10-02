\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_member_storage_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_member_storage_ci;
GRANT SELECT,INSERT,UPDATE,DELETE ON card_members TO strataai_member_storage_ci;
GRANT SELECT,INSERT,UPDATE ON card_assignment_notifications TO strataai_member_storage_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('03000000-0000-0000-0000-000000000001','Member A',now(),now()),
 ('03000000-0000-0000-0000-000000000002','Member B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 SELECT id,'member-storage-'||id||'@example.test',upper('member-storage-'||id||'@example.test'), 'Member storage fixture','ACTIVE',true,'unused-member-storage-hash',now(),now()
 FROM unnest(ARRAY['03000000-0000-0000-0000-000000000041','03000000-0000-0000-0000-000000000042',
 '03000000-0000-0000-0000-000000000043','03000000-0000-0000-0000-000000000044']::uuid[]) ids(id);
INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000041','MEMBER','ACTIVE'),
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000042','MEMBER','ACTIVE'),
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000002','03000000-0000-0000-0000-000000000043','MEMBER','ACTIVE'),
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000044','OWNER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('03000000-0000-0000-0000-000000000011','03000000-0000-0000-0000-000000000001','A one',now(),now()),
 ('03000000-0000-0000-0000-000000000012','03000000-0000-0000-0000-000000000001','A two',now(),now()),
 ('03000000-0000-0000-0000-000000000013','03000000-0000-0000-0000-000000000002','B one',now(),now());
INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011','03000000-0000-0000-0000-000000000041','MEMBER','ACTIVE',now(),now()),
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000012','03000000-0000-0000-0000-000000000042','MEMBER','ACTIVE',now(),now()),
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000002','03000000-0000-0000-0000-000000000013','03000000-0000-0000-0000-000000000043','MEMBER','ACTIVE',now(),now()),
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011','03000000-0000-0000-0000-000000000044','ADMIN','ACTIVE',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('03000000-0000-0000-0000-000000000021','03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011','A List','500000000000000000000000000000',now(),now()),
 ('03000000-0000-0000-0000-000000000022','03000000-0000-0000-0000-000000000002','03000000-0000-0000-0000-000000000013','B List','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('03000000-0000-0000-0000-000000000031','03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011','03000000-0000-0000-0000-000000000021','A Card','500000000000000000000000000000',now(),now()),
 ('03000000-0000-0000-0000-000000000032','03000000-0000-0000-0000-000000000002','03000000-0000-0000-0000-000000000013','03000000-0000-0000-0000-000000000022','B Card','500000000000000000000000000000',now(),now());
INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by) VALUES
 ('03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011','03000000-0000-0000-0000-000000000031','03000000-0000-0000-0000-000000000041','03000000-0000-0000-0000-000000000044'),
 ('03000000-0000-0000-0000-000000000002','03000000-0000-0000-0000-000000000013','03000000-0000-0000-0000-000000000032','03000000-0000-0000-0000-000000000043','03000000-0000-0000-0000-000000000043'),
 ('03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011','03000000-0000-0000-0000-000000000031','03000000-0000-0000-0000-000000000044','03000000-0000-0000-0000-000000000044');
INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'03000000-0000-0000-0000-000000000002','03000000-0000-0000-0000-000000000044','OWNER','ACTIVE');
INSERT INTO work_event_streams(tenant_id,board_id) VALUES
 ('03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011'),
 ('03000000-0000-0000-0000-000000000002','03000000-0000-0000-0000-000000000013');
INSERT INTO work_events(tenant_id,board_id,event_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 SELECT c.tenant_id,c.board_id,CASE WHEN c.id='03000000-0000-0000-0000-000000000031'::uuid
 THEN '03000000-0000-0000-0000-000000000051'::uuid ELSE '03000000-0000-0000-0000-000000000052'::uuid END,
 1,'03000000-0000-0000-0000-000000000044','CARD_MEMBER_ADDED','Card',c.id,1,'notification-storage',now()
 FROM cards c WHERE c.id IN ('03000000-0000-0000-0000-000000000031','03000000-0000-0000-0000-000000000032');
INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at)
 SELECT e.tenant_id,CASE WHEN e.entity_id='03000000-0000-0000-0000-000000000031'::uuid
 THEN '03000000-0000-0000-0000-000000000061'::uuid ELSE '03000000-0000-0000-0000-000000000062'::uuid END,
 e.board_id,e.entity_id,e.event_id,CASE WHEN e.entity_id='03000000-0000-0000-0000-000000000031'::uuid
 THEN '03000000-0000-0000-0000-000000000041'::uuid ELSE '03000000-0000-0000-0000-000000000043'::uuid END,
 e.actor_id,e.entity_version,e.created_at FROM work_events e WHERE e.correlation_id='notification-storage';
SET LOCAL ROLE strataai_member_storage_ci;
SELECT set_config('app.tenant_id','03000000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM card_assignment_notifications) <> 1 THEN RAISE EXCEPTION 'Notification tenant reads widened'; END IF;
 BEGIN
  INSERT INTO card_assignment_notifications SELECT tenant_id,gen_random_uuid(),board_id,card_id,event_id,recipient_id,actor_id,notification_type,card_version,created_at,read_at FROM card_assignment_notifications;
  RAISE EXCEPTION 'Duplicate event-recipient notification accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET board_id='03000000-0000-0000-0000-000000000012';
  RAISE EXCEPTION 'Notification crossed Board';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET card_id='03000000-0000-0000-0000-000000000032';
  RAISE EXCEPTION 'Notification crossed tenant Card';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET recipient_id='03000000-0000-0000-0000-000000000043';
  RAISE EXCEPTION 'Notification recipient crossed tenant';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET recipient_id=actor_id;
  RAISE EXCEPTION 'Actor self notification accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET card_version=0;
  RAISE EXCEPTION 'Notification nonpositive revision accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET read_at=created_at-interval '1 second';
  RAISE EXCEPTION 'Notification read before creation accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET tenant_id='03000000-0000-0000-0000-000000000002';
  RAISE EXCEPTION 'Cross tenant notification write accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
DO $$ BEGIN
 IF (SELECT count(*) FROM card_members) <> 2 THEN RAISE EXCEPTION 'Multiple assignments or tenant reads failed'; END IF;
 BEGIN
  INSERT INTO card_members SELECT * FROM card_members;
  RAISE EXCEPTION 'Duplicate Card member accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by) VALUES
   ('03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011','03000000-0000-0000-0000-000000000031','03000000-0000-0000-0000-000000000042','03000000-0000-0000-0000-000000000044');
  RAISE EXCEPTION 'Cross-Board assignee accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by) VALUES
   ('03000000-0000-0000-0000-000000000001','03000000-0000-0000-0000-000000000011','03000000-0000-0000-0000-000000000031','03000000-0000-0000-0000-000000000043','03000000-0000-0000-0000-000000000044');
  RAISE EXCEPTION 'Cross-Organization assignee accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE card_members SET assigned_by='03000000-0000-0000-0000-000000000043';
  RAISE EXCEPTION 'Cross-Organization assigning actor accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE card_members SET board_id='03000000-0000-0000-0000-000000000012';
  RAISE EXCEPTION 'Cross-Board Card association accepted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE card_members SET version=0;
  RAISE EXCEPTION 'Invalid assignment revision accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by) VALUES
   ('03000000-0000-0000-0000-000000000002','03000000-0000-0000-0000-000000000013','03000000-0000-0000-0000-000000000032','03000000-0000-0000-0000-000000000043','03000000-0000-0000-0000-000000000043');
  RAISE EXCEPTION 'Cross-tenant assignment write accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
SELECT set_config('app.tenant_id','03000000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM card_assignment_notifications) <> 1 OR EXISTS(SELECT 1 FROM card_assignment_notifications WHERE tenant_id<>'03000000-0000-0000-0000-000000000002') THEN RAISE EXCEPTION 'Other tenant notifications widened'; END IF;
 IF (SELECT count(*) FROM card_members) <> 1 OR EXISTS(SELECT 1 FROM card_members WHERE tenant_id<>'03000000-0000-0000-0000-000000000002') THEN RAISE EXCEPTION 'Other tenant assignment reads widened'; END IF;
END $$;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM card_assignment_notifications) THEN RAISE EXCEPTION 'Missing tenant exposed notifications'; END IF;
 IF EXISTS(SELECT 1 FROM card_members) THEN RAISE EXCEPTION 'Missing tenant exposed assignments'; END IF;
END $$;
RESET ROLE;
-- Soft membership departure preserves immutable user/assigner references; the
-- command cleanup will remove current assignments atomically with its events.
UPDATE board_members SET status='REMOVED' WHERE user_id='03000000-0000-0000-0000-000000000041';
UPDATE organization_members SET status='REMOVED' WHERE user_id='03000000-0000-0000-0000-000000000044';
DO $$ BEGIN
 IF (SELECT count(*) FROM card_assignment_notifications) <> 2 THEN RAISE EXCEPTION 'Membership departure erased notification history'; END IF;
 IF NOT EXISTS(SELECT 1 FROM card_members WHERE assigned_by='03000000-0000-0000-0000-000000000044') THEN RAISE EXCEPTION 'Departure erased historical actor reference'; END IF;
END $$;
ROLLBACK;
\echo 'Card member and assignment notification storage: forced RLS, composite references, event-recipient uniqueness, self suppression, revision/read-time constraints and retained attribution passed.'
