DO $$ BEGIN
 IF (SELECT count(*) FROM organization_access_route_clock_upgrade_fixture)<>(SELECT count(*) FROM user_organization_access)
 OR EXISTS(SELECT 1 FROM organization_access_route_clock_upgrade_fixture f LEFT JOIN user_organization_access r USING(user_id,tenant_id)
  WHERE r.user_id IS NULL OR to_jsonb(r)-'created_at' IS DISTINCT FROM f.original
   OR r.created_at IS DISTINCT FROM f.source_created OR r.updated_at IS DISTINCT FROM f.source_updated) THEN
  RAISE EXCEPTION 'Organization access route upgrade changed history or canonical clocks';
 END IF;
END $$;
DROP TABLE organization_access_route_clock_upgrade_fixture;
BEGIN;
SELECT set_config('app.tenant_id','f20a0000-0000-4000-8000-000000000010',true);
DO $$
DECLARE original_created timestamptz; original_updated timestamptz;
BEGIN
 SELECT created_at,updated_at INTO STRICT original_created,original_updated FROM user_organization_access
 WHERE user_id='f20a0000-0000-4000-8000-000000000002' AND tenant_id='f20a0000-0000-4000-8000-000000000010';
 BEGIN
  UPDATE user_organization_access SET created_at=created_at+interval '1 second'
   WHERE user_id='f20a0000-0000-4000-8000-000000000002' AND tenant_id='f20a0000-0000-4000-8000-000000000010';
  RAISE EXCEPTION 'Organization access creation clock replacement was admitted';
 EXCEPTION WHEN check_violation THEN NULL;
 END;
 BEGIN
  UPDATE user_organization_access SET updated_at=updated_at+interval '100 years'
   WHERE user_id='f20a0000-0000-4000-8000-000000000002' AND tenant_id='f20a0000-0000-4000-8000-000000000010';
  RAISE EXCEPTION 'Organization access update clock replacement was admitted';
 EXCEPTION WHEN check_violation THEN NULL;
 END;
 UPDATE user_organization_access SET updated_at=updated_at
  WHERE user_id='f20a0000-0000-4000-8000-000000000002' AND tenant_id='f20a0000-0000-4000-8000-000000000010';
 IF NOT EXISTS(SELECT 1 FROM user_organization_access WHERE user_id='f20a0000-0000-4000-8000-000000000002'
  AND tenant_id='f20a0000-0000-4000-8000-000000000010' AND created_at=original_created AND updated_at=original_updated) THEN
  RAISE EXCEPTION 'No-op organization route update changed clocks';
 END IF;
 UPDATE organization_members SET role='ADMIN',status='SUSPENDED',updated_at=clock_timestamp(),version=version+1
  WHERE user_id='f20a0000-0000-4000-8000-000000000002' AND tenant_id='f20a0000-0000-4000-8000-000000000010';
 IF NOT EXISTS(SELECT 1 FROM user_organization_access r JOIN organization_members m USING(user_id,tenant_id)
  WHERE r.user_id='f20a0000-0000-4000-8000-000000000002' AND r.tenant_id='f20a0000-0000-4000-8000-000000000010'
   AND r.role='ADMIN' AND r.status='SUSPENDED' AND r.created_at=original_created AND r.updated_at=m.updated_at AND r.updated_at>original_updated) THEN
  RAISE EXCEPTION 'Membership role/status transition did not synchronize canonical route clocks';
 END IF;
 SET CONSTRAINTS ALL IMMEDIATE;
END $$;
ROLLBACK;
