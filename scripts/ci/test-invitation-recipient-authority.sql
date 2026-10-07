-- PRD-03-TC-05/07/08 and PRD-60-TC-05/06/07/14: real PostgreSQL
-- private capability evidence, not browser/runtime completion.
BEGIN;
DO $$
DECLARE owner_id uuid:=gen_random_uuid(); tenant uuid:=gen_random_uuid(); source_id uuid:=gen_random_uuid();
 worker_handle uuid:=gen_random_uuid(); lease uuid:=gen_random_uuid(); job uuid; next_job uuid; invitation uuid;
 email text:=upper('authority-'||tenant||'@example.test'); other_email text:=upper('authority-other-'||tenant||'@example.test');
 refused boolean; result boolean; member_id uuid:=gen_random_uuid(); member_source uuid:=gen_random_uuid(); i integer;
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES(owner_id,'authority-owner-'||owner_id||'@example.test',upper('authority-owner-'||owner_id||'@example.test'),
 'Authority Owner','ACTIVE',true,'unused-authority-hash',now(),now());
 INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at)
 VALUES(tenant,'Original private name',owner_id,'ACTIVE',now(),now());
 INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),tenant,owner_id,'OWNER','ACTIVE');
 FOR i IN 1..205 LOOP
  invitation:=('00000000-0000-4000-8000-'||lpad(i::text,12,'0'))::uuid;
  INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
  VALUES(invitation,tenant,lower(CASE WHEN i=205 THEN other_email ELSE email END),CASE WHEN i=205 THEN other_email ELSE email END,
   encode(sha256(invitation::text::bytea),'hex'),'PORTAL','OWNER',owner_id,clock_timestamp(),clock_timestamp()+interval '1 day');
 END LOOP;
 -- Actual membership removal source and its job roll back with the owning command.
 BEGIN
  INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
  VALUES(member_id,'member-'||member_id||'@example.test',upper('member-'||member_id||'@example.test'),'Member','ACTIVE',true,'unused',now(),now());
  INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(member_id,tenant,member_id,'ADMIN','ACTIVE');
  UPDATE organization_members SET status='REMOVED',version=version+1,updated_at=clock_timestamp() WHERE id=member_id;
  INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
  VALUES(member_source,tenant,owner_id,'ORGANIZATION_MEMBER_REMOVED','User',member_id,'authority-member-source','{}');
  IF NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_pages WHERE source_event_id=member_source AND tenant_id=tenant) THEN
   RAISE EXCEPTION 'Membership removal lost its authority page'; END IF;
  RAISE EXCEPTION 'Rollback membership source fixture' USING ERRCODE='P0002';
 EXCEPTION WHEN no_data_found THEN NULL;
 END;
 IF EXISTS(SELECT 1 FROM invitation_recipient_authority_pages WHERE source_event_id=member_source)
  OR EXISTS(SELECT 1 FROM audit_events WHERE id=member_source)
  OR EXISTS(SELECT 1 FROM organization_members WHERE id=member_id) THEN
  RAISE EXCEPTION 'Owning membership command rollback leaked authority publication'; END IF;
 UPDATE organizations SET name='Current private name' ,version=version+1,updated_at=clock_timestamp() WHERE id=tenant;
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
 VALUES(source_id,tenant,owner_id,'ORGANIZATION_UPDATED','Organization',tenant,'authority-original-source','{}');
 SELECT job_id INTO job FROM invitation_recipient_authority_pages WHERE tenant_id=tenant AND source_event_id=source_id;
 IF job IS NULL OR (SELECT count(*) FROM invitation_recipient_authority_pages WHERE tenant_id=tenant)<>1
  OR EXISTS(SELECT 1 FROM invitation_recipient_authority_effects WHERE tenant_id=tenant)
  OR EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions WHERE email_normalized=email) THEN
  RAISE EXCEPTION 'Source publication scanned recipients or lost its initial checkpoint'; END IF;
 -- A future row sorts before existing candidates: source-time cutoff excludes it.
 invitation:='00000000-0000-4000-8000-000000000000';
 INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 VALUES(invitation,tenant,'future@example.test','FUTURE@EXAMPLE.TEST',encode(sha256(invitation::text::bytea),'hex'),
 'PORTAL','OWNER',owner_id,clock_timestamp(),clock_timestamp()+interval '1 day');
 PERFORM set_config('app.tenant_id',tenant::text,true);
 UPDATE background_jobs SET state='RUNNING',attempt_count=1,worker_id=worker_handle,lease_id=lease,lease_expires_at=clock_timestamp()+interval '2 minutes' WHERE id=job;
 -- Canonical sources remain deliverable after actor departure; current grant
 -- checks are reader concerns and must not erase the original source identity.
 UPDATE organization_members SET status='REMOVED',version=version+1 WHERE tenant_id=tenant AND user_id=owner_id;
 SET LOCAL ROLE strataai_worker_runtime;
 refused:=false;
 BEGIN PERFORM * FROM invitation_recipient_authority_pages; EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Worker read private checkpoints'; END IF;
 refused:=false;
 BEGIN PERFORM * FROM invitation_recipient_authority_effects; EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Worker read private recipient effects'; END IF;
 refused:=false;
 BEGIN PERFORM * FROM invitation_recipient_authority_revisions; EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Worker read private revisions'; END IF;
 IF deliver_invitation_recipient_authority(tenant,job,owner_id,gen_random_uuid(),lease,source_id,100)
  OR deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,gen_random_uuid(),source_id,100)
  OR deliver_invitation_recipient_authority(tenant,job,gen_random_uuid(),worker_handle,lease,source_id,100)
  OR deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,gen_random_uuid(),100)
  OR deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,101) THEN
  RAISE EXCEPTION 'Invalid authority capability scope was accepted'; END IF;
 RESET ROLE;
 UPDATE background_jobs SET service_identity='incorrect-authority-service' WHERE id=job;
 SET LOCAL ROLE strataai_worker_runtime;
 IF deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100) THEN
  RAISE EXCEPTION 'Wrong authority service was accepted'; END IF;
 RESET ROLE;
 UPDATE background_jobs SET service_identity='invitation-recipient-authority',safe_metadata=jsonb_build_object('eventId',source_id,'private','forbidden') WHERE id=job;
 SET LOCAL ROLE strataai_worker_runtime;
 IF deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100) THEN
  RAISE EXCEPTION 'Private authority metadata was accepted'; END IF;
 PERFORM set_config('app.tenant_id',gen_random_uuid()::text,true);
 IF deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100) THEN
  RAISE EXCEPTION 'Foreign authority tenant was accepted'; END IF;
 RESET ROLE;
 PERFORM set_config('app.tenant_id',tenant::text,true);
 UPDATE background_jobs SET safe_metadata=jsonb_build_object('eventId',source_id) WHERE id=job;
 -- Late checkpoint failure must roll back effects, counters and continuation.
 CREATE FUNCTION pg_temp.reject_authority_page() RETURNS trigger LANGUAGE plpgsql AS $body$
 BEGIN RAISE EXCEPTION 'Injected late authority failure' USING ERRCODE='23514'; END $body$;
 CREATE TRIGGER authority_late_failure AFTER UPDATE ON invitation_recipient_authority_pages FOR EACH ROW EXECUTE FUNCTION pg_temp.reject_authority_page();
 SET LOCAL ROLE strataai_worker_runtime;
 refused:=false;
 BEGIN PERFORM deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100);
 EXCEPTION WHEN check_violation THEN refused:=true; END;
 RESET ROLE;
 DROP TRIGGER authority_late_failure ON invitation_recipient_authority_pages;
 IF NOT refused OR EXISTS(SELECT 1 FROM invitation_recipient_authority_effects WHERE tenant_id=tenant)
  OR EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions WHERE email_normalized=email)
  OR (SELECT count(*) FROM invitation_recipient_authority_pages WHERE tenant_id=tenant)<>1 THEN
  RAISE EXCEPTION 'Late authority failure leaked effects or continuation'; END IF;
 -- Expiry after effects but before return rolls back the whole page.
 CREATE FUNCTION pg_temp.expire_authority_lease() RETURNS trigger LANGUAGE plpgsql AS $body$
 BEGIN UPDATE public.background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=NEW.job_id; RETURN NEW; END $body$;
 CREATE TRIGGER authority_expiry AFTER UPDATE ON invitation_recipient_authority_pages FOR EACH ROW EXECUTE FUNCTION pg_temp.expire_authority_lease();
 SET LOCAL ROLE strataai_worker_runtime;
 refused:=false;
 BEGIN PERFORM deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100);
 EXCEPTION WHEN check_violation THEN refused:=true; END;
 RESET ROLE;
 DROP TRIGGER authority_expiry ON invitation_recipient_authority_pages;
 IF NOT refused OR EXISTS(SELECT 1 FROM invitation_recipient_authority_effects WHERE tenant_id=tenant)
  OR (SELECT count(*) FROM invitation_recipient_authority_pages WHERE tenant_id=tenant)<>1 THEN
  RAISE EXCEPTION 'Post-effect lease expiry leaked page publication'; END IF;
 SET LOCAL ROLE strataai_worker_runtime;
 result:=deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100);
 RESET ROLE;
 IF NOT result OR (SELECT scanned_count FROM invitation_recipient_authority_pages WHERE job_id=job)<>100
  OR (SELECT revision FROM invitation_recipient_authority_revisions WHERE email_normalized=email)<>1
  OR (SELECT count(*) FROM invitation_recipient_authority_pages WHERE tenant_id=tenant)<>2 THEN
  RAISE EXCEPTION 'First authority page was not bounded, deduplicated and continued'; END IF;
 -- Retry the committed page under a replacement lease, as after Worker death.
 lease:=gen_random_uuid();
 UPDATE background_jobs SET lease_id=lease WHERE id=job;
 SET LOCAL ROLE strataai_worker_runtime;
 result:=deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100);
 RESET ROLE;
 IF NOT result OR (SELECT revision FROM invitation_recipient_authority_revisions WHERE email_normalized=email)<>1
  OR (SELECT count(*) FROM invitation_recipient_authority_pages WHERE tenant_id=tenant)<>2 THEN
  RAISE EXCEPTION 'Replacement lease duplicated committed authority effects'; END IF;
 FOR i IN 1..2 LOOP
  SELECT job_id INTO next_job FROM invitation_recipient_authority_pages WHERE tenant_id=tenant AND completed_at IS NULL ORDER BY after_id LIMIT 1;
  lease:=gen_random_uuid();
  UPDATE background_jobs SET state='RUNNING',attempt_count=1,worker_id=worker_handle,lease_id=lease,lease_expires_at=clock_timestamp()+interval '2 minutes' WHERE id=next_job;
  SET LOCAL ROLE strataai_worker_runtime;
  result:=deliver_invitation_recipient_authority(tenant,next_job,owner_id,worker_handle,lease,source_id,100);
  RESET ROLE;
  IF NOT result THEN RAISE EXCEPTION 'Authority continuation was unavailable'; END IF;
 END LOOP;
 IF (SELECT array_agg(scanned_count ORDER BY after_id) FROM invitation_recipient_authority_pages WHERE tenant_id=tenant)<>ARRAY[100,100,5]
  OR (SELECT count(*) FROM invitation_recipient_authority_effects WHERE tenant_id=tenant)<>2
  OR (SELECT revision FROM invitation_recipient_authority_revisions WHERE email_normalized=email)<>1
  OR (SELECT revision FROM invitation_recipient_authority_revisions WHERE email_normalized=other_email)<>1
  OR EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions WHERE email_normalized='FUTURE@EXAMPLE.TEST')
  OR EXISTS(SELECT 1 FROM invitation_recipient_events WHERE tenant_id=tenant) THEN
  RAISE EXCEPTION 'Paged authority source duplicated effects, included future candidates or invented invitation transitions'; END IF;
 UPDATE background_jobs SET lease_id=lease,lease_expires_at=clock_timestamp()-interval '1 second' WHERE id=job;
 SET LOCAL ROLE strataai_worker_runtime;
 IF deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100) THEN
  RAISE EXCEPTION 'Expired authority lease was accepted'; END IF;
 RESET ROLE;
 SET LOCAL ROLE strataai_api_runtime;
 PERFORM set_config('app.route_kind','INVITATION_RECIPIENT',true); PERFORM set_config('app.route_key',email,true);
 IF (SELECT count(*) FROM invitation_recipient_authority_revisions)<>1 THEN RAISE EXCEPTION 'Recipient revision lookup widened'; END IF;
 refused:=false;
 BEGIN UPDATE invitation_recipient_authority_revisions SET revision=2 WHERE email_normalized=email;
 EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'API wrote private authority revisions'; END IF;
 refused:=false;
 BEGIN PERFORM deliver_invitation_recipient_authority(tenant,job,owner_id,worker_handle,lease,source_id,100);
 EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'API executed Worker authority capability'; END IF;
 RESET ROLE;
 RAISE NOTICE 'Authority source: 205 candidates, pages 100/100/5, two deduplicated revisions, rollback and restricted capability passed';
END $$;
ROLLBACK;
