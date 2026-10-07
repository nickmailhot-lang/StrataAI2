BEGIN;
-- Global routing returns only bounded Organization references. Private source,
-- page and recipient data remain behind the existing leased capability.
CREATE FUNCTION discover_invitation_recipient_authority_scopes(p_after uuid,p_limit integer)
RETURNS TABLE(tenant_id uuid) LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF p_limit IS NULL OR p_limit<1 OR p_limit>100 THEN
  RAISE EXCEPTION 'Authority scope page limit is invalid' USING ERRCODE='22023';
 END IF;
 RETURN QUERY SELECT p.tenant_id FROM public.invitation_recipient_authority_pages p
 JOIN public.organization_metadata_events e ON e.tenant_id=p.tenant_id AND e.event_id=p.source_event_id
 JOIN public.background_jobs j ON j.tenant_id=p.tenant_id AND j.id=p.job_id
 WHERE (p_after IS NULL OR p.tenant_id>p_after)
  AND e.event_type IN ('ORGANIZATION_UPDATED','ORGANIZATION_MEMBER_REMOVED')
  AND j.job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE' AND j.service_identity='invitation-recipient-authority'
  AND j.actor_id=e.actor_id AND j.correlation_id=e.correlation_id
  AND j.safe_metadata=jsonb_build_object('eventId',e.event_id)
  AND j.idempotency_key='invitation-authority/'||replace(e.event_id::text,'-','')||'/'||replace(p.after_id::text,'-','')
  AND ((j.state='PENDING' AND j.available_at<=clock_timestamp() AND j.attempt_count<j.max_attempts)
    OR (j.state='RUNNING' AND j.lease_expires_at<=clock_timestamp()))
 GROUP BY p.tenant_id ORDER BY p.tenant_id LIMIT p_limit;
END $$;
REVOKE ALL ON FUNCTION discover_invitation_recipient_authority_scopes(uuid,integer) FROM PUBLIC;

-- Tenant invoker claims retain forced RLS and ordinary queue permissions. They
-- cannot claim or retire metadata, deletion, mail, preview or provider work.
CREATE FUNCTION claim_invitation_recipient_authority_job(p_worker_id uuid) RETURNS SETOF background_jobs
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE v_now timestamptz := clock_timestamp();
BEGIN
 IF p_worker_id IS NULL OR p_worker_id='00000000-0000-0000-0000-000000000000'::uuid THEN
  RAISE EXCEPTION 'Worker identity is required' USING ERRCODE='22023';
 END IF;
 UPDATE public.background_jobs SET state='FAILED',lease_id=NULL,worker_id=NULL,lease_expires_at=NULL,
  last_error_code='lease_expired',updated_at=v_now,version=version+1
 WHERE tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid
  AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE' AND service_identity='invitation-recipient-authority'
  AND state='RUNNING' AND lease_expires_at<=v_now AND attempt_count>=max_attempts;
 RETURN QUERY WITH candidate AS (
  SELECT id FROM public.background_jobs WHERE tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid
   AND job_type='INVITATION_RECIPIENT_AUTHORITY_PAGE' AND service_identity='invitation-recipient-authority'
   AND attempt_count<max_attempts
   AND ((state='PENDING' AND available_at<=v_now) OR (state='RUNNING' AND lease_expires_at<=v_now))
  ORDER BY available_at,created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
 ) UPDATE public.background_jobs j SET state='RUNNING',attempt_count=attempt_count+1,lease_id=gen_random_uuid(),
  worker_id=p_worker_id,lease_expires_at=v_now+interval '2 minutes',updated_at=v_now,version=version+1
 FROM candidate c WHERE j.id=c.id RETURNING j.*;
END $$;
REVOKE ALL ON FUNCTION claim_invitation_recipient_authority_job(uuid) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('106_invitation_recipient_authority_discovery');
COMMIT;
