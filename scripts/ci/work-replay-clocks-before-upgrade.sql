INSERT INTO organizations(id,name,created_at,updated_at)
 VALUES('12800000-0000-0000-0000-000000000101','Clock upgrade',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('12800000-0000-0000-0000-000000000102','work-clock-upgrade@example.test','WORK-CLOCK-UPGRADE@EXAMPLE.TEST','Clock upgrade','ACTIVE','fixture',now(),now());
INSERT INTO work_command_replays(tenant_id,actor_id,key_id,fingerprint,result_json,created_at,expires_at)
 SELECT '12800000-0000-0000-0000-000000000101','12800000-0000-0000-0000-000000000102',
  id,repeat('A',64),result,now()-interval '2 days',now()-interval '1 day'
 FROM (VALUES('12800000-0000-0000-0000-000000000103'::uuid,'{"Succeeded":true}'::jsonb),
  ('12800000-0000-0000-0000-000000000104'::uuid,NULL::jsonb)) legacy(id,result);
CREATE TABLE work_replay_clock_upgrade_fixture AS
 SELECT key_id,to_jsonb(r) AS body FROM work_command_replays r
 WHERE tenant_id='12800000-0000-0000-0000-000000000101';
