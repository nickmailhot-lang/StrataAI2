BEGIN;
UPDATE organizations SET name='Counter clock next revision',version=version+1,updated_at=updated_at+interval '3 seconds'
 WHERE id='f20a0000-0000-4000-8000-000000000010';
INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 VALUES(gen_random_uuid(),'f20a0000-0000-4000-8000-000000000010','f20a0000-0000-4000-8000-000000000001',
 'ORGANIZATION_UPDATED','Organization','f20a0000-0000-4000-8000-000000000010','counter-clock-upgrade');
CREATE TABLE organization_stream_clock_upgrade_fixture AS
 SELECT tenant_id,to_jsonb(s) AS original FROM organization_metadata_event_streams s;
INSERT INTO organizations(id,name,created_at,updated_at)
 VALUES('f22a0000-0000-4000-8000-000000000010','Unproven counter fixture','2020-01-01','2020-01-01');
INSERT INTO organization_metadata_event_streams(tenant_id,last_sequence)
 VALUES('f22a0000-0000-4000-8000-000000000010',1);
COMMIT;
