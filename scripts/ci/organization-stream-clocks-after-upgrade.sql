DO $$ BEGIN
 IF (SELECT count(*) FROM organization_stream_clock_upgrade_fixture)<>(SELECT count(*) FROM organization_metadata_event_streams)
 OR EXISTS(SELECT 1 FROM organization_stream_clock_upgrade_fixture f LEFT JOIN organization_metadata_event_streams s USING(tenant_id)
  WHERE s.tenant_id IS NULL OR to_jsonb(s)-'created_at'-'updated_at' IS DISTINCT FROM f.original
   OR s.created_at IS DISTINCT FROM (SELECT created_at FROM organization_metadata_events WHERE tenant_id=s.tenant_id AND sequence=1)
   OR s.updated_at IS DISTINCT FROM (SELECT max(created_at) FROM organization_metadata_events WHERE tenant_id=s.tenant_id)) THEN
  RAISE EXCEPTION 'Organization stream clock upgrade changed retained history';
 END IF;
END $$;
DROP TABLE organization_stream_clock_upgrade_fixture;
BEGIN;
DO $$
DECLARE creation timestamptz; modified timestamptz;
BEGIN
 SELECT created_at,updated_at INTO STRICT creation,modified FROM organization_metadata_event_streams
 WHERE tenant_id='f20a0000-0000-4000-8000-000000000010';
 BEGIN
  UPDATE organization_metadata_event_streams SET created_at=created_at+interval '1 second'
   WHERE tenant_id='f20a0000-0000-4000-8000-000000000010';
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Counter creation clock tamper was admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE organization_metadata_event_streams SET updated_at=updated_at+interval '100 years'
   WHERE tenant_id='f20a0000-0000-4000-8000-000000000010';
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Counter update clock tamper was admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE organization_metadata_event_streams SET last_sequence=last_sequence+1
   WHERE tenant_id='f20a0000-0000-4000-8000-000000000010';
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Counter without journal source was admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 UPDATE organization_metadata_event_streams SET last_sequence=last_sequence
  WHERE tenant_id='f20a0000-0000-4000-8000-000000000010';
 SET CONSTRAINTS ALL IMMEDIATE;
 IF NOT EXISTS(SELECT 1 FROM organization_metadata_event_streams WHERE tenant_id='f20a0000-0000-4000-8000-000000000010'
  AND created_at=creation AND updated_at=modified) THEN RAISE EXCEPTION 'Counter no-op changed clocks'; END IF;
 SET CONSTRAINTS ALL DEFERRED;
 UPDATE organizations SET name='Counter clock command',version=version+1,updated_at=updated_at+interval '1 second'
 WHERE id='f20a0000-0000-4000-8000-000000000010';
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 VALUES(gen_random_uuid(),'f20a0000-0000-4000-8000-000000000010','f20a0000-0000-4000-8000-000000000001',
 'ORGANIZATION_UPDATED','Organization','f20a0000-0000-4000-8000-000000000010','counter-clock-command');
 SET CONSTRAINTS ALL IMMEDIATE;
 IF NOT EXISTS(SELECT 1 FROM organization_metadata_event_streams WHERE tenant_id='f20a0000-0000-4000-8000-000000000010'
  AND created_at=creation AND updated_at=modified+interval '1 second') THEN
  RAISE EXCEPTION 'Canonical journal append did not preserve and advance stream clocks';
 END IF;
END $$;
ROLLBACK;
