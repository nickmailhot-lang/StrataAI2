DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM operational_lifecycle_delivery_upgrade_fixture) THEN
  RAISE EXCEPTION 'Lifecycle delivery upgrade fixture was empty';
 END IF;
 IF EXISTS(SELECT 1 FROM operational_lifecycle_delivery_upgrade_fixture f
  LEFT JOIN organization_operational_lifecycle_events e USING(tenant_id,version)
  LEFT JOIN organization_operational_lifecycle_history h USING(tenant_id,version)
  LEFT JOIN organization_operational_lifecycle_receipts r USING(tenant_id,version)
  WHERE e.tenant_id IS NULL OR (to_jsonb(e)-ARRAY['actor_id','correlation_id','created_at','ready_at','updated_at']) IS DISTINCT FROM f.original_event
   OR h.source_json IS DISTINCT FROM f.source_json OR r.receipt_json IS DISTINCT FROM f.receipt_json
   OR e.created_at IS DISTINCT FROM (f.source_json->>'CreatedAt')::timestamptz
   OR e.ready_at IS NOT NULL OR e.updated_at IS DISTINCT FROM e.created_at
   OR (SELECT count(*) FROM background_jobs j WHERE j.tenant_id=e.tenant_id
    AND j.job_type='ORGANIZATION_OPERATIONAL_LIFECYCLE_EVENT_READY'
    AND j.service_identity='organization-operational-lifecycle-delivery'
    AND j.idempotency_key='organization-operational-lifecycle-event/'||replace(e.event_id::text,'-','')
    AND j.actor_id=e.actor_id AND j.correlation_id=e.correlation_id
    AND j.safe_metadata=jsonb_build_object('eventId',e.event_id) AND j.state='PENDING')<>1) THEN
  RAISE EXCEPTION 'Lifecycle delivery upgrade changed a committed source or failed exact publication';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_class WHERE relname='organization_operational_lifecycle_events' AND (NOT relrowsecurity OR NOT relforcerowsecurity)) THEN
  RAISE EXCEPTION 'Lifecycle delivery upgrade lost forced RLS';
 END IF;
END $$;
DROP TABLE operational_lifecycle_delivery_upgrade_fixture;
