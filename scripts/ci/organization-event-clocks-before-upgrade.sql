BEGIN;
INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at)
 VALUES('f21a0000-0000-4000-8000-000000000010','Event clock fixture','f20a0000-0000-4000-8000-000000000001','ACTIVE','2020-01-01','2020-01-01');
INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
 VALUES('f21a0000-0000-4000-8000-000000000011','f21a0000-0000-4000-8000-000000000010','f20a0000-0000-4000-8000-000000000001','OWNER','ACTIVE','2020-01-01','2020-01-01');
INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 SELECT gen_random_uuid(),id,owner_user_id,'ORGANIZATION_CREATED','Organization',id,'event-clock-upgrade'
 FROM organizations WHERE id IN ('f20a0000-0000-4000-8000-000000000010','f21a0000-0000-4000-8000-000000000010');
UPDATE organization_metadata_events SET ready_at=created_at+interval '1 second'
 WHERE tenant_id='f21a0000-0000-4000-8000-000000000010';
-- Lifecycle events require the leased terminal capability. Its separate
-- restricted-role contract supplies real terminal and delivery transitions.
CREATE TABLE organization_event_clock_upgrade_fixture AS
 SELECT 'organization_metadata_events'::text AS source,event_id,to_jsonb(e) AS original FROM organization_metadata_events e
 UNION ALL SELECT 'organization_lifecycle_events',event_id,to_jsonb(e) FROM organization_lifecycle_events e;
COMMIT;
