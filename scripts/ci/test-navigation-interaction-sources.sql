\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_navigation_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_navigation_ci;
GRANT SELECT ON navigation_interaction_events,navigation_interaction_replays TO strataai_navigation_ci;
GRANT EXECUTE ON FUNCTION append_or_replay_navigation_interaction(uuid,text,uuid,uuid,text,uuid,uuid,uuid,bigint,timestamptz) TO strataai_navigation_ci;
GRANT EXECUTE ON FUNCTION append_navigation_interaction(uuid,uuid,text,uuid,uuid,uuid,bigint,timestamptz) TO strataai_navigation_ci;
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at) VALUES
 ('07800000-0000-0000-0000-000000000001','search-a@example.test','SEARCH-A@EXAMPLE.TEST','Search A','ACTIVE','fixture',now(),now()),
 ('07800000-0000-0000-0000-000000000002','search-b@example.test','SEARCH-B@EXAMPLE.TEST','Search B','ACTIVE','fixture',now(),now());
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('07800000-0000-0000-0000-000000000010','Private source fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at) VALUES
 ('07800000-0000-0000-0000-000000000011','07800000-0000-0000-0000-000000000010','07800000-0000-0000-0000-000000000001','MEMBER','ACTIVE',now(),now());
INSERT INTO boards(id,tenant_id,name,visibility,background_type,background_value,created_at,updated_at) VALUES
 ('07800000-0000-0000-0000-000000000020','07800000-0000-0000-0000-000000000010','Private source Board','PRIVATE','COLOR','blue',now(),now());
INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES
 ('07800000-0000-0000-0000-000000000021','07800000-0000-0000-0000-000000000010','07800000-0000-0000-0000-000000000020','07800000-0000-0000-0000-000000000001','MEMBER','ACTIVE',now(),now());

INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('07800000-0000-0000-0000-000000000040','07800000-0000-0000-0000-000000000010','07800000-0000-0000-0000-000000000020','Navigation List','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('07800000-0000-0000-0000-000000000041','07800000-0000-0000-0000-000000000010','07800000-0000-0000-0000-000000000020','07800000-0000-0000-0000-000000000040','Navigation Card','500000000000000000000000000000',now(),now());
SET LOCAL ROLE strataai_navigation_ci;
SELECT set_config('app.identity_subject','07800000-0000-0000-0000-000000000001',true);
SELECT set_config('app.tenant_id','07800000-0000-0000-0000-000000000099',true);
DO $$ DECLARE actor uuid:='07800000-0000-0000-0000-000000000001'; org uuid:='07800000-0000-0000-0000-000000000010'; board uuid:='07800000-0000-0000-0000-000000000020'; event uuid:='07800000-0000-0000-0000-000000000030'; BEGIN
 IF NOT append_navigation_interaction(event,actor,'BOARD_OPENED',org,board,board,1,'2026-10-05T12:00:00Z')
  OR NOT append_navigation_interaction(event,actor,'BOARD_OPENED',org,board,board,1,'2026-10-05T12:00:00Z') THEN RAISE EXCEPTION 'Authorized original/retry denied'; END IF;
 IF append_navigation_interaction(event,actor,'BOARD_OPENED',org,board,board,1,'2026-10-05T12:00:01Z')
  OR append_navigation_interaction(gen_random_uuid(),actor,'BOARD_OPENED',org,board,board,2,now()) THEN RAISE EXCEPTION 'Changed original or stale target accepted'; END IF;
 IF current_setting('app.tenant_id')<>'07800000-0000-0000-0000-000000000099' THEN RAISE EXCEPTION 'Navigation leaked tenant context'; END IF;
 IF (SELECT count(*) FROM navigation_interaction_events)<>1 THEN RAISE EXCEPTION 'Duplicate source'; END IF;
 BEGIN
  UPDATE navigation_interaction_events SET entity_version=2;
  RAISE EXCEPTION 'Direct update permitted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  PERFORM append_navigation_interaction('07800000-0000-0000-0000-000000000031',actor,'BOARD_OPENED',org,board,board,1,now());
  RAISE EXCEPTION 'Rollback fixture' USING ERRCODE='P0002';
 EXCEPTION WHEN SQLSTATE 'P0002' THEN NULL; END;
 IF (SELECT count(*) FROM navigation_interaction_events)<>1 THEN RAISE EXCEPTION 'Refused source survived rollback'; END IF;
END $$;
DO $$ DECLARE actor uuid:='07800000-0000-0000-0000-000000000001'; org uuid:='07800000-0000-0000-0000-000000000010'; board uuid:='07800000-0000-0000-0000-000000000020'; card uuid:='07800000-0000-0000-0000-000000000041'; BEGIN
 IF NOT append_navigation_interaction('07800000-0000-0000-0000-000000000032',actor,'CARD_OPENED',org,board,card,1,now()) THEN RAISE EXCEPTION 'Admitted Card refused'; END IF;
 IF append_navigation_interaction(gen_random_uuid(),actor,'CARD_OPENED',org,board,card,2,now())
  OR append_navigation_interaction(gen_random_uuid(),actor,'CARD_OPENED',org,board,'07800000-0000-0000-0000-000000000099',1,now())
 THEN RAISE EXCEPTION 'Stale/missing Card accepted'; END IF;
 IF (SELECT count(*) FROM navigation_interaction_events)<>2 THEN RAISE EXCEPTION 'Denied Card source persisted'; END IF;
END $$;
RESET ROLE;
UPDATE board_lists SET lifecycle_state='ARCHIVED',archived_at=now() WHERE id='07800000-0000-0000-0000-000000000040';
SET LOCAL ROLE strataai_navigation_ci;
DO $$ BEGIN
 IF append_navigation_interaction(gen_random_uuid(),'07800000-0000-0000-0000-000000000001','CARD_OPENED','07800000-0000-0000-0000-000000000010','07800000-0000-0000-0000-000000000020','07800000-0000-0000-0000-000000000041',1,now()) THEN RAISE EXCEPTION 'Archived parent Card admitted'; END IF;
END $$;
SELECT set_config('app.identity_subject','07800000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM navigation_interaction_events) THEN RAISE EXCEPTION 'Other actor disclosure'; END IF;
 IF append_navigation_interaction(gen_random_uuid(),'07800000-0000-0000-0000-000000000002','BOARD_OPENED','07800000-0000-0000-0000-000000000010','07800000-0000-0000-0000-000000000020','07800000-0000-0000-0000-000000000020',1,now()) THEN RAISE EXCEPTION 'Private Board outsider accepted'; END IF;
END $$;
SELECT set_config('app.identity_subject','',true);
DO $$ BEGIN IF EXISTS(SELECT 1 FROM navigation_interaction_events) THEN RAISE EXCEPTION 'Missing actor disclosure'; END IF; END $$;
SELECT set_config('app.identity_subject','07800000-0000-0000-0000-000000000001',true);
DO $$ DECLARE actor uuid:='07800000-0000-0000-0000-000000000001'; org uuid:='07800000-0000-0000-0000-000000000010'; board uuid:='07800000-0000-0000-0000-000000000020'; key uuid:='08000000-0000-0000-0000-000000000001'; event uuid:='08000000-0000-0000-0000-000000000002'; reply record; BEGIN
 SELECT * INTO reply FROM append_or_replay_navigation_interaction(key,repeat('a',64),event,actor,'BOARD_OPENED',org,board,board,1,'2026-10-05T12:00:00Z');
 IF NOT FOUND OR reply.original_event<>event THEN RAISE EXCEPTION 'Fresh navigation receipt unavailable'; END IF;
 SELECT * INTO reply FROM append_or_replay_navigation_interaction(key,repeat('a',64),gen_random_uuid(),actor,'BOARD_OPENED',org,board,board,1,now());
 IF NOT FOUND OR reply.original_event<>event OR reply.original_created<>'2026-10-05T12:00:00Z'::timestamptz THEN RAISE EXCEPTION 'Original navigation identity lost'; END IF;
 PERFORM * FROM append_or_replay_navigation_interaction(key,repeat('b',64),gen_random_uuid(),actor,'BOARD_OPENED',org,board,board,1,now());
 IF FOUND THEN RAISE EXCEPTION 'Changed navigation intent accepted'; END IF;
 IF (SELECT count(*) FROM navigation_interaction_replays)<>1 THEN RAISE EXCEPTION 'Duplicate navigation receipt'; END IF;
END $$;
RESET ROLE;
-- PRD-01-TC-05/06/07: receipts and sources roll back together.
SET LOCAL ROLE strataai_navigation_ci;
DO $$ DECLARE actor uuid:='07800000-0000-0000-0000-000000000001'; candidate uuid:=gen_random_uuid(); request uuid:=gen_random_uuid(); sources bigint; receipts bigint; BEGIN
 SELECT count(*) INTO sources FROM navigation_interaction_events;
 SELECT count(*) INTO receipts FROM navigation_interaction_replays;
 BEGIN
  PERFORM * FROM append_or_replay_navigation_interaction(request,repeat('c',64),candidate,actor,'APPLICATION_CONTEXT_CHANGED',NULL,NULL,candidate,1,now());
  IF NOT FOUND THEN RAISE EXCEPTION 'Rollback receipt not admitted'; END IF;
  RAISE EXCEPTION 'Late refusal fixture' USING ERRCODE='P0002';
 EXCEPTION WHEN SQLSTATE 'P0002' THEN NULL; END;
 IF (SELECT count(*) FROM navigation_interaction_events)<>sources OR (SELECT count(*) FROM navigation_interaction_replays)<>receipts
 THEN RAISE EXCEPTION 'Receipt/source survived late refusal'; END IF;
END $$;
SELECT set_config('app.identity_subject','07800000-0000-0000-0000-000000000002',true);
DO $$ DECLARE actor uuid:='07800000-0000-0000-0000-000000000002'; candidate uuid:=gen_random_uuid(); BEGIN
 IF EXISTS(SELECT 1 FROM navigation_interaction_replays) THEN RAISE EXCEPTION 'Foreign receipt disclosed'; END IF;
 PERFORM * FROM append_or_replay_navigation_interaction('08000000-0000-0000-0000-000000000001',repeat('a',64),candidate,actor,'APPLICATION_CONTEXT_CHANGED',NULL,NULL,candidate,1,now());
 IF NOT FOUND THEN RAISE EXCEPTION 'Actor retry namespace not independent'; END IF;
 IF (SELECT count(*) FROM navigation_interaction_replays)<>1 THEN RAISE EXCEPTION 'Actor receipt RLS failed'; END IF;
 PERFORM * FROM append_or_replay_navigation_interaction(gen_random_uuid(),repeat('a',64),candidate,'07800000-0000-0000-0000-000000000001','APPLICATION_CONTEXT_CHANGED',NULL,NULL,candidate,1,now());
 IF FOUND THEN RAISE EXCEPTION 'Forged actor admitted'; END IF;
END $$;
RESET ROLE;
-- Populate canonical global originals without changing immutable rows.
INSERT INTO navigation_interaction_events(event_id,actor_id,event_type,entity_type,entity_id,entity_version,created_at)
SELECT md5('navigation-capacity-event-'||n)::uuid,'07800000-0000-0000-0000-000000000002','APPLICATION_CONTEXT_CHANGED','ApplicationContext',md5('navigation-capacity-event-'||n)::uuid,1,now()
FROM generate_series(1,999) n;
INSERT INTO navigation_interaction_replays(actor_id,request_id,event_id,fingerprint,created_at,expires_at)
SELECT '07800000-0000-0000-0000-000000000002',md5('navigation-capacity-request-'||n)::uuid,md5('navigation-capacity-event-'||n)::uuid,repeat('d',64),now(),now()+interval '24 hours'
FROM generate_series(1,999) n;
SET LOCAL ROLE strataai_navigation_ci;
DO $$ DECLARE candidate uuid:=gen_random_uuid(); BEGIN
 IF (SELECT count(*) FROM navigation_interaction_replays)<>1000 THEN RAISE EXCEPTION 'Capacity fixture incomplete'; END IF;
 BEGIN
  PERFORM * FROM append_or_replay_navigation_interaction(gen_random_uuid(),repeat('e',64),candidate,'07800000-0000-0000-0000-000000000002','APPLICATION_CONTEXT_CHANGED',NULL,NULL,candidate,1,now());
  RAISE EXCEPTION 'Capacity overflow admitted' USING ERRCODE='P0002';
 EXCEPTION WHEN SQLSTATE 'P0001' THEN NULL; END;
 IF EXISTS(SELECT 1 FROM navigation_interaction_events WHERE event_id=candidate)
  OR (SELECT count(*) FROM navigation_interaction_replays)<>1000 THEN RAISE EXCEPTION 'Capacity refusal left source or evicted live receipt'; END IF;
END $$;
RESET ROLE;
DELETE FROM navigation_interaction_replays WHERE actor_id='07800000-0000-0000-0000-000000000002' AND request_id=md5('navigation-capacity-request-1')::uuid;
INSERT INTO navigation_interaction_replays(actor_id,request_id,event_id,fingerprint,created_at,expires_at)
VALUES('07800000-0000-0000-0000-000000000002',md5('navigation-capacity-request-1')::uuid,md5('navigation-capacity-event-1')::uuid,repeat('d',64),'2000-01-01T00:00:00Z','2000-01-02T00:00:00Z');
SET LOCAL ROLE strataai_navigation_ci;
DO $$ DECLARE candidate uuid:=gen_random_uuid(); BEGIN
 PERFORM * FROM append_or_replay_navigation_interaction(md5('navigation-capacity-request-1')::uuid,repeat('d',64),candidate,'07800000-0000-0000-0000-000000000002','APPLICATION_CONTEXT_CHANGED',NULL,NULL,candidate,1,now());
 IF FOUND THEN RAISE EXCEPTION 'Expired receipt replayed'; END IF;
 PERFORM * FROM append_or_replay_navigation_interaction(gen_random_uuid(),repeat('e',64),candidate,'07800000-0000-0000-0000-000000000002','APPLICATION_CONTEXT_CHANGED',NULL,NULL,candidate,1,now());
 IF NOT FOUND OR (SELECT count(*) FROM navigation_interaction_replays)<>1000 THEN RAISE EXCEPTION 'Expired capacity not reclaimed'; END IF;
 IF EXISTS(SELECT 1 FROM navigation_interaction_replays WHERE request_id=md5('navigation-capacity-request-1')::uuid)
  OR NOT EXISTS(SELECT 1 FROM navigation_interaction_events WHERE event_id=md5('navigation-capacity-event-1')::uuid)
 THEN RAISE EXCEPTION 'Expiry cleanup changed immutable source'; END IF;
END $$;
SELECT set_config('app.identity_subject','07800000-0000-0000-0000-000000000001',true);
RESET ROLE;
UPDATE board_members SET status='REMOVED' WHERE id='07800000-0000-0000-0000-000000000021';
SET LOCAL ROLE strataai_navigation_ci;
DO $$ BEGIN
 PERFORM * FROM append_or_replay_navigation_interaction('08000000-0000-0000-0000-000000000001',repeat('a',64),gen_random_uuid(),'07800000-0000-0000-0000-000000000001','BOARD_OPENED','07800000-0000-0000-0000-000000000010','07800000-0000-0000-0000-000000000020','07800000-0000-0000-0000-000000000020',1,now());
 IF FOUND THEN RAISE EXCEPTION 'Revoked receipt disclosed'; END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF (SELECT version FROM cards WHERE id='07800000-0000-0000-0000-000000000041')<>1
  OR (SELECT version FROM boards WHERE id='07800000-0000-0000-0000-000000000020')<>1 THEN RAISE EXCEPTION 'Navigation changed shared revisions'; END IF;
 IF NOT (SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='navigation_interaction_events'::regclass) THEN RAISE EXCEPTION 'Forced RLS missing'; END IF;
 IF NOT (SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='navigation_interaction_replays'::regclass) THEN RAISE EXCEPTION 'Receipt forced RLS missing'; END IF;
END $$;
ROLLBACK;
