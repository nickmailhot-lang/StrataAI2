BEGIN;
DO $$
BEGIN
 IF EXISTS(SELECT 1 FROM configuration_attribution_upgrade_fixture f
  LEFT JOIN organization_configuration_events e ON e.event_id=f.event_id
  WHERE e.event_id IS NULL
   OR (to_jsonb(e)-'activity_actor_label'-'acting_capacity'-'source_membership_id'-'source_membership_version') IS DISTINCT FROM f.source
   OR e.activity_actor_label IS DISTINCT FROM 'Historical actor '||e.actor_id::text
   OR e.acting_capacity IS DISTINCT FROM 'UNRECORDED'
   OR e.source_membership_id IS NOT NULL OR e.source_membership_version IS NOT NULL) THEN
  RAISE EXCEPTION 'Configuration attribution upgrade fabricated or rewrote historical evidence';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_proc WHERE oid='capture_organization_configuration_attribution()'::regprocedure
  AND prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'])
  OR EXISTS(SELECT 1 FROM information_schema.role_routine_grants
   WHERE routine_name IN ('capture_organization_configuration_attribution','guard_organization_configuration_attribution')
    AND grantee IN ('PUBLIC','strataai_api_runtime','strataai_worker_runtime')) THEN
  RAISE EXCEPTION 'Configuration attribution capture exposes a runtime capability';
 END IF;
 IF EXISTS(SELECT 1 FROM pg_class WHERE relname='organization_configuration_events'
  AND (NOT relrowsecurity OR NOT relforcerowsecurity)) THEN
  RAISE EXCEPTION 'Configuration attribution lost forced tenant RLS';
 END IF;
END $$;
DROP TABLE configuration_attribution_upgrade_fixture;
COMMIT;
