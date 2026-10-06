BEGIN;
-- Read-only Worker routing hints. No source content, account/membership/session
-- data, global queue reads or mutation authority is returned by this capability.
CREATE FUNCTION discover_organization_metadata_scopes(p_after uuid,p_limit integer)
RETURNS TABLE(tenant_id uuid) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF p_limit IS NULL OR p_limit<1 OR p_limit>100 THEN
  RAISE EXCEPTION 'Metadata scope page limit is invalid' USING ERRCODE='22023';
 END IF;
 RETURN QUERY SELECT s.tenant_id FROM public.organization_metadata_event_streams s
 WHERE (p_after IS NULL OR s.tenant_id>p_after)
 AND EXISTS(SELECT 1 FROM public.organization_metadata_events e JOIN public.background_jobs j ON j.tenant_id=e.tenant_id
  WHERE e.tenant_id=s.tenant_id AND j.job_type='ORGANIZATION_METADATA_EVENT_READY'
   AND j.service_identity='organization-metadata-delivery' AND j.actor_id=e.actor_id
   AND j.correlation_id=e.correlation_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id)
   AND j.idempotency_key='organization-metadata-event/'||replace(e.event_id::text,'-','')
   AND ((j.state='PENDING' AND j.available_at<=clock_timestamp() AND j.attempt_count<j.max_attempts)
    OR (j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp())))
 ORDER BY s.tenant_id LIMIT p_limit;
END $$;
REVOKE ALL ON FUNCTION discover_organization_metadata_scopes(uuid,integer) FROM PUBLIC;

-- This invoker path retains ordinary queue grants and forced tenant RLS.
-- Automatic metadata delivery must never claim or retire unrelated providers.
CREATE FUNCTION claim_organization_metadata_job(p_worker_id uuid) RETURNS SETOF background_jobs
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE v_now timestamptz := clock_timestamp();
BEGIN
 IF p_worker_id IS NULL OR p_worker_id='00000000-0000-0000-0000-000000000000'::uuid THEN
  RAISE EXCEPTION 'Worker identity is required' USING ERRCODE='22023';
 END IF;
 UPDATE public.background_jobs SET state='FAILED',lease_id=NULL,worker_id=NULL,lease_expires_at=NULL,
  last_error_code='lease_expired',updated_at=v_now,version=version+1
 WHERE tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid
  AND job_type='ORGANIZATION_METADATA_EVENT_READY' AND service_identity='organization-metadata-delivery'
  AND state='RUNNING' AND lease_expires_at<=v_now AND attempt_count>=max_attempts;
 RETURN QUERY WITH candidate AS (
  SELECT id FROM public.background_jobs WHERE tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid
   AND job_type='ORGANIZATION_METADATA_EVENT_READY' AND service_identity='organization-metadata-delivery'
   AND attempt_count<max_attempts
   AND ((state='PENDING' AND available_at<=v_now) OR (state='RUNNING' AND lease_expires_at<=v_now))
  ORDER BY available_at,created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
 ) UPDATE public.background_jobs j SET state='RUNNING',attempt_count=attempt_count+1,lease_id=gen_random_uuid(),
  worker_id=p_worker_id,lease_expires_at=v_now+interval '2 minutes',updated_at=v_now,version=version+1
 FROM candidate c WHERE j.id=c.id RETURNING j.*;
END $$;
REVOKE ALL ON FUNCTION claim_organization_metadata_job(uuid) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('096_organization_metadata_discovery');
COMMIT;
