BEGIN;
DO $$ BEGIN
 IF (SELECT count(*) FROM authority_page_clock_upgrade_fixture)<>2 OR EXISTS(
  SELECT 1 FROM authority_page_clock_upgrade_fixture f LEFT JOIN invitation_recipient_authority_pages p USING(tenant_id,job_id)
  WHERE p.job_id IS NULL OR (to_jsonb(p)-'created_at'-'updated_at') IS DISTINCT FROM f.original
   OR p.created_at IS DISTINCT FROM f.owner_created_at OR p.updated_at IS DISTINCT FROM coalesce(p.completed_at,f.owner_created_at)
 ) THEN RAISE EXCEPTION 'Authority page upgrade changed historical state or clocks'; END IF;
END $$;
CREATE TEMP TABLE authority_page_clock_capability AS
 SELECT p.tenant_id,gen_random_uuid() AS job_id,p.source_event_id,s.actor_id,gen_random_uuid() AS worker_id,
 gen_random_uuid() AS lease_id,gen_random_uuid() AS after_id
 FROM invitation_recipient_authority_pages p JOIN invitation_issuer_authority_sources s ON s.event_id=p.source_event_id
 WHERE p.job_id=(SELECT job_id FROM authority_page_clock_upgrade_fixture ORDER BY owner_created_at LIMIT 1);
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,state,attempt_count,worker_id,lease_id,lease_expires_at)
 SELECT job_id,tenant_id,'INVITATION_RECIPIENT_AUTHORITY_PAGE',
 'invitation-authority/'||replace(source_event_id::text,'-','')||'/'||replace(after_id::text,'-',''),
 actor_id,'invitation-recipient-authority','authority-page-clocks',jsonb_build_object('eventId',source_event_id),'RUNNING',1,worker_id,lease_id,clock_timestamp()+interval '2 minutes'
 FROM authority_page_clock_capability;
INSERT INTO invitation_recipient_authority_pages(tenant_id,job_id,source_event_id,after_id,after_created_at)
 SELECT tenant_id,job_id,source_event_id,after_id,clock_timestamp() FROM authority_page_clock_capability;
CREATE TEMP TABLE authority_page_clock_pending AS SELECT p.* FROM invitation_recipient_authority_pages p JOIN authority_page_clock_capability c USING(tenant_id,job_id);
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM authority_page_clock_pending p JOIN background_jobs j ON j.tenant_id=p.tenant_id AND j.id=p.job_id
  WHERE p.created_at=j.created_at AND p.updated_at=p.created_at AND p.completed_at IS NULL) THEN
  RAISE EXCEPTION 'New authority page did not inherit its recorded job clock';
 END IF;
END $$;
GRANT SELECT ON authority_page_clock_capability TO strataai_worker_runtime;
SELECT set_config('app.tenant_id',tenant_id::text,true) FROM authority_page_clock_capability;
SAVEPOINT before_delivery;
SET LOCAL ROLE strataai_worker_runtime;
DO $$ DECLARE c record; BEGIN
 SELECT * INTO STRICT c FROM authority_page_clock_capability;
 IF NOT public.deliver_invitation_recipient_authority(c.tenant_id,c.job_id,c.actor_id,c.worker_id,c.lease_id,c.source_event_id,100) THEN
  RAISE EXCEPTION 'Restricted authority page delivery was refused';
 END IF;
END $$;
RESET ROLE;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_pages p JOIN authority_page_clock_pending b USING(tenant_id,job_id)
  WHERE p.created_at=b.created_at AND p.updated_at=p.completed_at AND p.updated_at>b.updated_at AND p.scanned_count=0) THEN
  RAISE EXCEPTION 'Delivered authority page did not retain its first completion clock';
 END IF;
END $$;
ROLLBACK TO SAVEPOINT before_delivery;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_recipient_authority_pages p JOIN authority_page_clock_pending b USING(tenant_id,job_id) WHERE to_jsonb(p)<>to_jsonb(b)) THEN
  RAISE EXCEPTION 'Authority page delivery rollback changed state or clocks';
 END IF;
END $$;
SET LOCAL ROLE strataai_worker_runtime;
DO $$ DECLARE c record; BEGIN
 SELECT * INTO STRICT c FROM authority_page_clock_capability;
 IF public.deliver_invitation_recipient_authority(c.tenant_id,c.job_id,c.actor_id,gen_random_uuid(),c.lease_id,c.source_event_id,100) THEN
  RAISE EXCEPTION 'Authority page substituted Worker was accepted';
 END IF;
 IF NOT public.deliver_invitation_recipient_authority(c.tenant_id,c.job_id,c.actor_id,c.worker_id,c.lease_id,c.source_event_id,100) THEN
  RAISE EXCEPTION 'Authority page completion after rollback was refused';
 END IF;
END $$;
RESET ROLE;
CREATE TEMP TABLE authority_page_clock_completed AS SELECT p.* FROM invitation_recipient_authority_pages p JOIN authority_page_clock_pending b USING(tenant_id,job_id);
SET LOCAL ROLE strataai_worker_runtime;
DO $$ DECLARE c record; BEGIN
 SELECT * INTO STRICT c FROM authority_page_clock_capability;
 IF NOT public.deliver_invitation_recipient_authority(c.tenant_id,c.job_id,c.actor_id,c.worker_id,c.lease_id,c.source_event_id,100) THEN
  RAISE EXCEPTION 'Same authority page retry did not confirm its original completion';
 END IF;
END $$;
RESET ROLE;
DO $$ DECLARE target uuid; BEGIN
 SELECT job_id INTO STRICT target FROM authority_page_clock_capability;
 IF EXISTS(SELECT 1 FROM invitation_recipient_authority_pages p JOIN authority_page_clock_completed b USING(tenant_id,job_id) WHERE to_jsonb(p)<>to_jsonb(b)) THEN
  RAISE EXCEPTION 'Authority page retry changed its original state or clocks';
 END IF;
 BEGIN
  UPDATE invitation_recipient_authority_pages SET created_at=clock_timestamp() WHERE job_id=target;
  RAISE EXCEPTION 'Authority page creation clock replacement was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE invitation_recipient_authority_pages SET completed_at=clock_timestamp() WHERE job_id=target;
  RAISE EXCEPTION 'Authority page completion history replacement was accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
ROLLBACK;
