\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_member_storage_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_member_storage_ci;
GRANT SELECT,INSERT,UPDATE,DELETE ON card_members TO strataai_member_storage_ci;
GRANT SELECT,INSERT,UPDATE ON card_assignment_notifications TO strataai_member_storage_ci;
GRANT SELECT,INSERT,UPDATE ON watch_subscriptions TO strataai_member_storage_ci;
GRANT SELECT,INSERT ON work_events TO strataai_member_storage_ci;
GRANT SELECT,INSERT,UPDATE ON card_reminders TO strataai_member_storage_ci;
GRANT SELECT ON cards TO strataai_member_storage_ci;
GRANT SELECT,INSERT,UPDATE ON card_routes TO strataai_member_storage_ci;
GRANT UPDATE(start_at,due_at,due_timezone,due_has_time,due_complete) ON cards TO strataai_member_storage_ci;
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
INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,card_id,watching,created_at,updated_at,version)
 SELECT c.tenant_id,CASE WHEN c.id='03000000-0000-0000-0000-000000000031'::uuid THEN '03400000-0000-0000-0000-000000000061'::uuid
 ELSE '03400000-0000-0000-0000-000000000062'::uuid END,CASE WHEN c.id='03000000-0000-0000-0000-000000000031'::uuid
 THEN '03000000-0000-0000-0000-000000000041'::uuid ELSE '03000000-0000-0000-0000-000000000043'::uuid END,
 'CARD',c.id,c.id,true,now(),now(),1 FROM cards c WHERE c.id IN ('03000000-0000-0000-0000-000000000031','03000000-0000-0000-0000-000000000032');
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 SELECT w.tenant_id,gen_random_uuid(),c.board_id,2,w.user_id,'WATCH_CREATED','WatchSubscription',w.id,w.version,'watch-storage',w.created_at
 FROM watch_subscriptions w JOIN cards c ON c.tenant_id=w.tenant_id AND c.id=w.card_id;
-- Exact elapsed hours keep UTC reminder intervals independent of session DST.
SET LOCAL TIME ZONE 'America/Vancouver';
INSERT INTO card_reminders(tenant_id,id,user_id,card_id,interval_code,enabled,due_at,trigger_at,status,generation,created_at,updated_at,version) VALUES
 ('03000000-0000-0000-0000-000000000001','03700000-0000-0000-0000-000000000071','03000000-0000-0000-0000-000000000041','03000000-0000-0000-0000-000000000031','1_DAY',true,'2026-03-09T06:59:59.999999Z','2026-03-08T06:59:59.999999Z','SCHEDULED',1,now(),now(),1),
 ('03000000-0000-0000-0000-000000000002','03700000-0000-0000-0000-000000000072','03000000-0000-0000-0000-000000000043','03000000-0000-0000-0000-000000000032','AT_DUE',true,'2026-03-09T06:59:59.999999Z','2026-03-09T06:59:59.999999Z','SCHEDULED',1,now(),now(),1);
SET LOCAL TIME ZONE 'UTC';
SET LOCAL ROLE strataai_member_storage_ci;
SELECT set_config('app.tenant_id','03000000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM work_events WHERE entity_type='WatchSubscription' AND watch_subscription_id=entity_id) <> 1 THEN RAISE EXCEPTION 'Valid watch event reference rejected or exposed another tenant'; END IF;
 BEGIN
  INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
   SELECT tenant_id,gen_random_uuid(),board_id,3,actor_id,'WATCH_CREATED','WatchSubscription','03400000-0000-0000-0000-000000000062',1,'watch-cross-tenant',created_at FROM work_events WHERE entity_type='WatchSubscription';
  RAISE EXCEPTION 'Watch event crossed subscription tenant';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
   SELECT tenant_id,gen_random_uuid(),board_id,3,actor_id,'CARD_UPDATED','WatchSubscription',entity_id,1,'watch-invalid-type',created_at FROM work_events WHERE entity_type='WatchSubscription';
  RAISE EXCEPTION 'Watch event accepted an unrelated event type';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
   SELECT tenant_id,gen_random_uuid(),board_id,3,actor_id,'WATCH_CREATED','Board',board_id,1,'watch-invalid-entity',created_at FROM work_events WHERE entity_type='WatchSubscription';
  RAISE EXCEPTION 'Watch event accepted an unrelated entity type';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
   SELECT tenant_id,gen_random_uuid(),board_id,3,actor_id,'CARD_UPDATED','Unknown',entity_id,1,'watch-unknown-entity',created_at FROM work_events WHERE entity_type='WatchSubscription';
  RAISE EXCEPTION 'Work event accepted an unknown entity type';
 EXCEPTION WHEN check_violation THEN NULL; END;
 IF (SELECT count(*) FROM watch_subscriptions) <> 1 THEN RAISE EXCEPTION 'Watch tenant reads widened'; END IF;
 BEGIN
  INSERT INTO watch_subscriptions SELECT tenant_id,gen_random_uuid(),user_id,entity_type,entity_id,board_id,list_id,card_id,watching,created_at,updated_at,version FROM watch_subscriptions;
  RAISE EXCEPTION 'Duplicate personal watch accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  UPDATE watch_subscriptions SET user_id='03000000-0000-0000-0000-000000000043';
  RAISE EXCEPTION 'Watch user crossed tenant';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE watch_subscriptions SET entity_id='03000000-0000-0000-0000-000000000032',card_id='03000000-0000-0000-0000-000000000032';
  RAISE EXCEPTION 'Watch entity crossed tenant';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE watch_subscriptions SET card_id=NULL;
  RAISE EXCEPTION 'Watch lost its typed entity reference';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE watch_subscriptions SET entity_type='LIST';
  RAISE EXCEPTION 'Watch type disagreed with its reference';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE watch_subscriptions SET version=0;
  RAISE EXCEPTION 'Watch accepted zero persisted revision';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE watch_subscriptions SET updated_at=created_at-interval '1 second';
  RAISE EXCEPTION 'Watch update preceded creation';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO watch_subscriptions SELECT '03000000-0000-0000-0000-000000000002',gen_random_uuid(),user_id,entity_type,entity_id,board_id,list_id,card_id,watching,created_at,updated_at,version FROM watch_subscriptions;
  RAISE EXCEPTION 'Watch cross-tenant insert escaped RLS';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 IF (SELECT count(*) FROM card_assignment_notifications) <> 1 THEN RAISE EXCEPTION 'Notification tenant reads widened'; END IF;
 BEGIN
  INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,notification_type,card_version,created_at,read_at)
   SELECT tenant_id,gen_random_uuid(),board_id,card_id,event_id,recipient_id,actor_id,notification_type,card_version,created_at,read_at FROM card_assignment_notifications;
  RAISE EXCEPTION 'Duplicate event-recipient notification accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET notification_type='UNKNOWN_ACTIVITY';
  RAISE EXCEPTION 'Notification accepted an unconfigured type';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE card_assignment_notifications SET notification_type='CARD_MOVED';
  RAISE EXCEPTION 'Notification activity mismatched its originating event';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
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
-- Card date columns share canonical Card RLS and enforce context/order/flags.
DO $$ BEGIN
 UPDATE cards SET start_at='2026-03-08T08:00:00Z',due_at='2026-03-09T06:59:59.999999Z',
   due_timezone='America/Vancouver',due_has_time=false,due_complete=true;
 IF (SELECT count(*) FROM cards WHERE due_at='2026-03-09T06:59:59.999999Z' AND due_complete) <> 1
   THEN RAISE EXCEPTION 'Date write or tenant isolation failed'; END IF;
 BEGIN
   UPDATE cards SET due_timezone=NULL;
   RAISE EXCEPTION 'Dates accepted missing timezone';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
   UPDATE cards SET start_at=due_at+interval '1 second';
   RAISE EXCEPTION 'Dates accepted start after due';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
   UPDATE cards SET due_at=NULL;
   RAISE EXCEPTION 'Due completion accepted without due date';
 EXCEPTION WHEN check_violation THEN NULL; END;
 UPDATE cards SET start_at=NULL,due_at=NULL,due_timezone=NULL,due_complete=false,due_has_time=false;
 BEGIN
   UPDATE cards SET due_has_time=true;
   RAISE EXCEPTION 'Timed flag accepted without due date';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
   UPDATE cards SET due_timezone='UTC';
   RAISE EXCEPTION 'Timezone context remained without dates';
 EXCEPTION WHEN check_violation THEN NULL; END;
 UPDATE cards SET due_at='2026-11-02T15:00:00Z',due_timezone='America/Vancouver',due_has_time=true;
 UPDATE cards SET due_complete=true WHERE tenant_id='03000000-0000-0000-0000-000000000002';
 IF FOUND THEN RAISE EXCEPTION 'Date write crossed tenant'; END IF;
END $$;
DO $$ BEGIN
 IF (SELECT count(*) FROM card_reminders)<>1 OR EXISTS(SELECT FROM card_reminders WHERE tenant_id<>'03000000-0000-0000-0000-000000000001')
   THEN RAISE EXCEPTION 'Reminder tenant reads widened'; END IF;
 BEGIN
  INSERT INTO card_reminders SELECT tenant_id,gen_random_uuid(),user_id,card_id,interval_code,enabled,due_at,trigger_at,status,generation,created_at,updated_at,version FROM card_reminders;
  RAISE EXCEPTION 'Duplicate personal Card reminder accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  UPDATE card_reminders SET user_id='03000000-0000-0000-0000-000000000043';
  RAISE EXCEPTION 'Reminder user crossed Organization';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE card_reminders SET card_id='03000000-0000-0000-0000-000000000032';
  RAISE EXCEPTION 'Reminder Card crossed Organization';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  UPDATE card_reminders SET interval_code='UNKNOWN';
  RAISE EXCEPTION 'Unsupported reminder interval accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE card_reminders SET generation=2;
  RAISE EXCEPTION 'Reminder generation exceeded persisted revision';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE card_reminders SET trigger_at=due_at;
  RAISE EXCEPTION 'Reminder trigger disagreed with selected interval';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE card_reminders SET status='CANCELLED';
  RAISE EXCEPTION 'Enabled scheduled reminder accepted cancelled state';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO card_reminders SELECT '03000000-0000-0000-0000-000000000002',gen_random_uuid(),user_id,card_id,interval_code,enabled,due_at,trigger_at,status,generation,created_at,updated_at,version FROM card_reminders;
  RAISE EXCEPTION 'Reminder insert crossed tenant RLS';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 UPDATE card_reminders SET status='CANCELLED',enabled=false,due_at=NULL,trigger_at=NULL,generation=2,version=2;
 IF EXISTS(SELECT FROM card_reminders WHERE enabled OR trigger_at IS NOT NULL OR generation<>2)
   THEN RAISE EXCEPTION 'Reminder cancellation retained deliverable generation'; END IF;
END $$;
SELECT set_config('app.tenant_id','03000000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM watch_subscriptions) <> 1 THEN RAISE EXCEPTION 'Other tenant watch reads widened'; END IF;
 IF (SELECT count(*) FROM card_reminders)<>1 OR EXISTS(SELECT FROM card_reminders WHERE tenant_id<>'03000000-0000-0000-0000-000000000002') THEN RAISE EXCEPTION 'Other tenant reminder reads widened'; END IF;
 IF (SELECT count(*) FROM card_assignment_notifications) <> 1 OR EXISTS(SELECT 1 FROM card_assignment_notifications WHERE tenant_id<>'03000000-0000-0000-0000-000000000002') THEN RAISE EXCEPTION 'Other tenant notifications widened'; END IF;
 IF (SELECT count(*) FROM card_members) <> 1 OR EXISTS(SELECT 1 FROM card_members WHERE tenant_id<>'03000000-0000-0000-0000-000000000002') THEN RAISE EXCEPTION 'Other tenant assignment reads widened'; END IF;
END $$;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM watch_subscriptions) THEN RAISE EXCEPTION 'Missing tenant exposed watches'; END IF;
 IF EXISTS(SELECT FROM card_reminders) THEN RAISE EXCEPTION 'Missing tenant exposed reminders'; END IF;
 IF EXISTS(SELECT 1 FROM card_assignment_notifications) THEN RAISE EXCEPTION 'Missing tenant exposed notifications'; END IF;
 IF EXISTS(SELECT 1 FROM card_members) THEN RAISE EXCEPTION 'Missing tenant exposed assignments'; END IF;
END $$;
RESET ROLE;
-- Soft membership departure preserves immutable user/assigner references; the
-- command cleanup will remove current assignments atomically with its events.
UPDATE board_members SET status='REMOVED' WHERE user_id='03000000-0000-0000-0000-000000000041';
UPDATE organization_members SET status='REMOVED' WHERE user_id='03000000-0000-0000-0000-000000000044';
DO $$ BEGIN
 IF (SELECT count(*) FROM watch_subscriptions) <> 2 THEN RAISE EXCEPTION 'Departure erased watch history'; END IF;
 IF (SELECT count(*) FROM card_assignment_notifications) <> 2 THEN RAISE EXCEPTION 'Membership departure erased notification history'; END IF;
 IF NOT EXISTS(SELECT 1 FROM card_members WHERE assigned_by='03000000-0000-0000-0000-000000000044') THEN RAISE EXCEPTION 'Departure erased historical actor reference'; END IF;
END $$;
-- PRD-08 / PRD-15: actual database movement preserves historical notifications.
-- This is a storage contract, not an authorized move API acceptance claim.
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('06600000-0000-0000-0000-000000000021','03000000-0000-0000-0000-000000000001',
  '03000000-0000-0000-0000-000000000012','Destination','500000000000000000000000000000',now(),now());
CREATE TEMP TABLE notification_before_move AS SELECT to_jsonb(n) AS envelope FROM card_assignment_notifications n;
DELETE FROM card_members WHERE card_id='03000000-0000-0000-0000-000000000031';
UPDATE cards SET board_id='03000000-0000-0000-0000-000000000012',
 list_id='06600000-0000-0000-0000-000000000021',version=version+1,updated_at=clock_timestamp()
 WHERE id='03000000-0000-0000-0000-000000000031';
DO $$ BEGIN
 IF (SELECT board_id FROM cards WHERE id='03000000-0000-0000-0000-000000000031')
    <> '03000000-0000-0000-0000-000000000012'::uuid THEN RAISE EXCEPTION 'Storage movement did not persist'; END IF;
 IF EXISTS((SELECT envelope FROM notification_before_move EXCEPT SELECT to_jsonb(n) FROM card_assignment_notifications n)
   UNION ALL (SELECT to_jsonb(n) FROM card_assignment_notifications n EXCEPT SELECT envelope FROM notification_before_move))
 THEN RAISE EXCEPTION 'Movement changed historical notifications'; END IF;
END $$;
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('06600000-0000-0000-0000-000000000031','03000000-0000-0000-0000-000000000001',
  '03000000-0000-0000-0000-000000000012','06600000-0000-0000-0000-000000000021',
  'Unrelated Card','600000000000000000000000000000',now(),now());
SET LOCAL ROLE strataai_member_storage_ci;
SELECT set_config('app.tenant_id','03000000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM card_assignment_notifications)<>1 THEN RAISE EXCEPTION 'Movement widened historical tenant reads'; END IF;
 BEGIN
  UPDATE card_assignment_notifications SET card_id='06600000-0000-0000-0000-000000000031';
  RAISE EXCEPTION 'Historical source rebound to unrelated same-tenant Card';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 UPDATE card_assignment_notifications SET read_at=GREATEST(created_at,clock_timestamp());
 IF EXISTS(SELECT FROM card_assignment_notifications WHERE read_at IS NULL
    OR board_id<>'03000000-0000-0000-0000-000000000011'
    OR card_id<>'03000000-0000-0000-0000-000000000031')
 THEN RAISE EXCEPTION 'Historical read acknowledgement changed source or failed'; END IF;
END $$;
RESET ROLE;
ROLLBACK;
\echo 'Card member and assignment notification storage: forced RLS, composite references, event-recipient uniqueness, self suppression, revision/read-time constraints and retained attribution passed.'
