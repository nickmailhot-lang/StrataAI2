BEGIN;
DO $$
BEGIN
 IF (SELECT count(*) FROM configuration_delivery_upgrade_fixture)<>1
  OR EXISTS(SELECT 1 FROM configuration_delivery_upgrade_fixture f LEFT JOIN organization_configuration_events e
   ON e.tenant_id=f.tenant_id AND e.event_id=f.event_id WHERE e.event_id IS NULL OR e.event_json IS DISTINCT FROM f.event_json
    OR e.ready_at IS NOT NULL OR e.actor_id IS DISTINCT FROM (f.event_json->>'ActorId')::uuid
    OR e.correlation_id IS DISTINCT FROM f.event_json->>'CorrelationId'
    OR e.created_at IS DISTINCT FROM (f.event_json->>'CreatedAt')::timestamptz OR e.updated_at IS DISTINCT FROM e.created_at)
  OR (SELECT count(*) FROM background_jobs j JOIN configuration_delivery_upgrade_fixture f ON j.tenant_id=f.tenant_id
   WHERE j.job_type='ORGANIZATION_CONFIGURATION_EVENT_READY' AND j.service_identity='organization-configuration-delivery'
    AND j.state='PENDING' AND j.attempt_count=0 AND j.correlation_id=f.event_json->>'CorrelationId'
    AND j.actor_id=(f.event_json->>'ActorId')::uuid AND j.safe_metadata=jsonb_build_object('eventId',f.event_id)
    AND j.idempotency_key='organization-configuration-event/'||replace(f.event_id::text,'-',''))<>1 THEN
  RAISE EXCEPTION 'Configuration delivery upgrade lost committed history or original queue attribution';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM discover_organization_configuration_scopes(NULL,100) d
  JOIN configuration_delivery_upgrade_fixture f ON f.tenant_id=d.tenant_id) THEN
  RAISE EXCEPTION 'Configuration delivery upgrade omitted historical pending routing';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_class WHERE relname='organization_configuration_events' AND (NOT relrowsecurity OR NOT relforcerowsecurity)) THEN
  RAISE EXCEPTION 'Configuration delivery lost forced RLS';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid='deliver_organization_configuration_event(uuid,uuid,uuid,uuid,uuid,uuid)'::regprocedure
  AND prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public']) OR NOT EXISTS(SELECT 1 FROM pg_proc
  WHERE oid='discover_organization_configuration_scopes(uuid,integer)'::regprocedure AND prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'])
  OR NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid='claim_organization_configuration_job(uuid)'::regprocedure
  AND NOT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public']) THEN
  RAISE EXCEPTION 'Configuration delivery capability has an invalid execution scope';
 END IF;
 IF (SELECT count(*) FROM information_schema.columns WHERE table_name='organization_configuration_events'
  AND column_name IN ('actor_id','correlation_id','created_at','ready_at','updated_at'))<>5 THEN
  RAISE EXCEPTION 'Configuration delivery clocks or source attribution are missing';
 END IF;
END $$;
DROP TABLE configuration_delivery_upgrade_fixture;
COMMIT;
