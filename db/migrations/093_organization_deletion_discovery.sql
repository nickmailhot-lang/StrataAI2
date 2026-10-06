BEGIN;
-- Worker routing capability: only bounded Organization references, never
-- graph content, membership, account/session data, job metadata or mutations.
CREATE FUNCTION discover_organization_deletion_scopes(p_after uuid,p_limit integer)
RETURNS TABLE(tenant_id uuid) LANGUAGE plpgsql SECURITY DEFINER
SET search_path=pg_catalog,public AS $$
BEGIN
 IF p_limit IS NULL OR p_limit<1 OR p_limit>100 THEN
  RAISE EXCEPTION 'Deletion scope page limit is invalid' USING ERRCODE='22023';
 END IF;
 RETURN QUERY
 SELECT r.tenant_id FROM public.organization_deletion_requests r
 JOIN public.organizations o ON o.id=r.tenant_id
 JOIN public.organization_deletion_progress p USING(tenant_id,request_id)
 WHERE (p_after IS NULL OR r.tenant_id>p_after)
 AND EXISTS(SELECT 1 FROM public.background_jobs j WHERE j.tenant_id=r.tenant_id
  AND j.actor_id=r.actor_id AND j.correlation_id=r.correlation_id
  AND ((j.state='PENDING' AND j.available_at<=clock_timestamp())
    OR (j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp()))
  AND ((o.status='DELETING' AND o.version=r.accepted_version AND p.phase<>'COMPLETE'
    AND j.job_type='ORGANIZATION_DELETE_PAGE' AND j.service_identity='organization-lifecycle'
    AND j.safe_metadata=jsonb_build_object('requestId',r.request_id,'stepId',p.step_id,'acceptedVersion',r.accepted_version))
   OR (o.status='DELETED' AND o.version=r.accepted_version+1 AND o.deleted_by=r.actor_id
    AND p.phase='COMPLETE' AND p.completed_at=o.deleted_at
    AND j.job_type='ORGANIZATION_LIFECYCLE_EVENT_READY' AND j.service_identity='organization-lifecycle-delivery'
    AND EXISTS(SELECT 1 FROM public.organization_lifecycle_events e WHERE e.tenant_id=r.tenant_id
      AND e.actor_id=r.actor_id AND e.entity_version=o.version AND e.created_at=o.deleted_at
      AND e.correlation_id=r.correlation_id AND e.event_type='ORGANIZATION_DELETED'
      AND j.safe_metadata=jsonb_build_object('eventId',e.event_id)))))
 ORDER BY r.tenant_id LIMIT p_limit;
END $$;
REVOKE ALL ON FUNCTION discover_organization_deletion_scopes(uuid,integer) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('093_organization_deletion_discovery');
COMMIT;
