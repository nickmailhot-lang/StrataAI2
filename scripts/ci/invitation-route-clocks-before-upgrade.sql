-- The forward-upgrade database only. Keep all historical routing fields and
-- canonical clock facts, including accepted/revoked/pending rows already seeded.
CREATE TABLE invitation_route_clock_upgrade_fixture AS
 SELECT r.invitation_id,to_jsonb(r) AS original,i.created_at AS owner_created_at,i.updated_at AS owner_updated_at
 FROM invitation_routes r JOIN invitations i ON i.id=r.invitation_id AND i.tenant_id=r.tenant_id AND i.token_hash=r.token_hash;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_route_clock_upgrade_fixture) THEN
  RAISE EXCEPTION 'Invitation route clock forward fixture is empty';
 END IF;
END $$;
-- A legacy unmatched route must roll back every new column and the ledger.
INSERT INTO invitation_routes(token_hash,invitation_id,tenant_id,email_normalized,target_surface,target_role,
 expires_at,organization_name)
 SELECT repeat('f',64),'f19a0000-0000-4000-8000-000000000099',tenant_id,
 'ROUTE-CLOCK-ORPHAN@EXAMPLE.TEST','INTERNAL','MEMBER',expires_at,organization_name
 FROM invitation_routes ORDER BY invitation_id LIMIT 1;
