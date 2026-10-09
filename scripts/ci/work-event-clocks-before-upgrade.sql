INSERT INTO organizations(id,name,created_at,updated_at)
 VALUES('13000000-0000-0000-0000-000000000101','Legacy activity clock',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('13000000-0000-0000-0000-000000000102','event-clock@example.test','EVENT-CLOCK@EXAMPLE.TEST','Clock fixture','ACTIVE','fixture',now(),now());
INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
 VALUES('13000000-0000-0000-0000-000000000103','13000000-0000-0000-0000-000000000101','Clock board',now(),now());
INSERT INTO work_event_streams(tenant_id,board_id)
 VALUES('13000000-0000-0000-0000-000000000101','13000000-0000-0000-0000-000000000103');
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at,ready_at)
 SELECT '13000000-0000-0000-0000-000000000101',md5('legacy-clock-'||n)::uuid,
 '13000000-0000-0000-0000-000000000103',n,'13000000-0000-0000-0000-000000000102',
 'BOARD_UPDATED','Board','13000000-0000-0000-0000-000000000103',1,'clock-upgrade',
 now()-interval '2 days',CASE WHEN n>1 THEN now()-interval '1 day' END FROM generate_series(1,3) n;
-- A reset source looks pending again; its lost history must not be invented.
UPDATE work_events SET ready_at=NULL WHERE correlation_id='clock-upgrade' AND sequence=3;
CREATE TABLE work_event_clock_upgrade_fixture AS
 SELECT event_id,to_jsonb(e) AS body FROM work_events e WHERE correlation_id='clock-upgrade';
