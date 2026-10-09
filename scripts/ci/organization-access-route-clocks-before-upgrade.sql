BEGIN;
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('f20a0000-0000-4000-8000-000000000001','route-owner@example.test','ROUTE-OWNER@EXAMPLE.TEST','Route owner','ACTIVE',true,'unused-fixture-hash','2020-01-01','2020-01-01'),
 ('f20a0000-0000-4000-8000-000000000002','route-member@example.test','ROUTE-MEMBER@EXAMPLE.TEST','Route member','ACTIVE',true,'unused-fixture-hash','2020-01-01','2020-01-01');
INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at)
 VALUES('f20a0000-0000-4000-8000-000000000010','Access route clock fixture','f20a0000-0000-4000-8000-000000000001','ACTIVE','2020-01-01','2020-01-01');
INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
 VALUES('f20a0000-0000-4000-8000-000000000011','f20a0000-0000-4000-8000-000000000010','f20a0000-0000-4000-8000-000000000001','OWNER','ACTIVE','2020-01-01','2020-01-01'),
 ('f20a0000-0000-4000-8000-000000000012','f20a0000-0000-4000-8000-000000000010','f20a0000-0000-4000-8000-000000000002','MEMBER','ACTIVE','2020-01-02','2020-01-02');
CREATE TABLE organization_access_route_clock_upgrade_fixture AS
 SELECT r.user_id,r.tenant_id,to_jsonb(r) AS original,m.created_at AS source_created,m.updated_at AS source_updated
 FROM user_organization_access r JOIN organization_members m
  ON m.user_id=r.user_id AND m.tenant_id=r.tenant_id AND m.role=r.role AND m.status=r.status;
COMMIT;
