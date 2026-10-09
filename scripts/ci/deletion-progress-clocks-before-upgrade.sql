BEGIN;
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('f2400000-0000-4000-8000-000000000001','progress-clock@example.test','PROGRESS-CLOCK@EXAMPLE.TEST','Progress clock fixture','ACTIVE','unused-fixture-hash','2020-01-01','2020-01-01');
INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
 VALUES('f2400000-0000-4000-8000-000000000010','Progress clock fixture','f2400000-0000-4000-8000-000000000001','DELETING',2,'2020-01-01','2020-01-01'),
 ('f2400000-0000-4000-8000-000000000020','Contradictory progress fixture','f2400000-0000-4000-8000-000000000001','DELETING',2,'2020-01-01','2020-01-01');
INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
 VALUES('f2400000-0000-4000-8000-000000000011','f2400000-0000-4000-8000-000000000010','f2400000-0000-4000-8000-000000000001','OWNER','ACTIVE','2020-01-01','2020-01-01'),
 ('f2400000-0000-4000-8000-000000000021','f2400000-0000-4000-8000-000000000020','f2400000-0000-4000-8000-000000000001','OWNER','ACTIVE','2020-01-01','2020-01-01');
INSERT INTO organization_deletion_requests(tenant_id,request_id,actor_id,accepted_version,correlation_id,created_at)
 VALUES('f2400000-0000-4000-8000-000000000010','f2400000-0000-4000-8000-000000000012','f2400000-0000-4000-8000-000000000001',2,'progress-clock-fixture','2020-01-02 03:04:05.123456+00'),
 ('f2400000-0000-4000-8000-000000000020','f2400000-0000-4000-8000-000000000022','f2400000-0000-4000-8000-000000000001',2,'progress-clock-refusal','2020-01-02 03:04:05.654321+00');
INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase,updated_at)
 VALUES('f2400000-0000-4000-8000-000000000010','f2400000-0000-4000-8000-000000000012','f2400000-0000-4000-8000-000000000012','ATTACHMENTS','2021-01-01'),
 ('f2400000-0000-4000-8000-000000000020','f2400000-0000-4000-8000-000000000022','f2400000-0000-4000-8000-000000000022','ATTACHMENTS','2019-01-01');
CREATE TABLE deletion_progress_clock_upgrade_fixture AS
 SELECT p.tenant_id,to_jsonb(p) AS original,r.created_at AS source_created FROM organization_deletion_progress p
 JOIN organization_deletion_requests r USING(tenant_id,request_id)
 WHERE p.tenant_id<>'f2400000-0000-4000-8000-000000000020';
COMMIT;
