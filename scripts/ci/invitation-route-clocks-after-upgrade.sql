DO $$ BEGIN
 IF (SELECT count(*) FROM invitation_route_clock_upgrade_fixture)<>(SELECT count(*) FROM invitation_routes)
  OR EXISTS(SELECT 1 FROM invitation_route_clock_upgrade_fixture f LEFT JOIN invitation_routes r USING(invitation_id)
   WHERE r.invitation_id IS NULL OR (to_jsonb(r)-'created_at'-'updated_at') IS DISTINCT FROM f.original
    OR r.created_at IS DISTINCT FROM f.owner_created_at OR r.updated_at IS DISTINCT FROM f.owner_updated_at) THEN
  RAISE EXCEPTION 'Invitation route upgrade changed historical body or canonical clocks';
 END IF;
END $$;
DROP TABLE invitation_route_clock_upgrade_fixture;
BEGIN;
DO $$
DECLARE row_id uuid; tenant uuid; source_created timestamptz; source_updated timestamptz; current_created timestamptz; current_updated timestamptz;
BEGIN
 SELECT invitation_id,tenant_id,created_at,updated_at INTO STRICT row_id,tenant,source_created,source_updated
 FROM invitation_routes ORDER BY invitation_id LIMIT 1;
 PERFORM set_config('app.tenant_id',tenant::text,true);
 BEGIN
  UPDATE invitation_routes SET created_at=created_at+interval '1 second' WHERE invitation_id=row_id;
  RAISE EXCEPTION 'Invitation route creation clock replacement was admitted';
 EXCEPTION WHEN check_violation THEN NULL;
 END;
 BEGIN
  UPDATE invitation_routes SET updated_at=updated_at+interval '100 years' WHERE invitation_id=row_id;
  RAISE EXCEPTION 'Invitation route update clock replacement was admitted';
 EXCEPTION WHEN check_violation THEN NULL;
 END;
 UPDATE invitation_routes SET updated_at=updated_at WHERE invitation_id=row_id;
 SELECT created_at,updated_at INTO current_created,current_updated FROM invitation_routes WHERE invitation_id=row_id;
 IF current_created<>source_created OR current_updated<>source_updated THEN
  RAISE EXCEPTION 'No-op invitation route update changed canonical clocks';
 END IF;
END $$;
ROLLBACK;
