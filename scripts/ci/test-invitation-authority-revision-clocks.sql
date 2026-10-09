-- PRD-01 FOUND-FR-009 and the complete original membership-source routing fixture.
\set ON_ERROR_STOP on
-- Routing contract; actual API producers are covered by native recipient tests.
BEGIN;
DO $$
DECLARE actor uuid:=gen_random_uuid(); tenant uuid:=(left(gen_random_uuid()::text,35)||'1')::uuid; board uuid:=gen_random_uuid();
 source uuid; job uuid; worker_handle uuid:=gen_random_uuid(); lease uuid; kind text; ordinal integer:=0;
 first_created timestamptz; prior_updated timestamptz; clock_body jsonb;
 recipient text:=upper('membership-authority-'||tenant||'@example.test'); succeeded boolean;
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES(actor,'authority-'||actor||'@example.test',upper('authority-'||actor||'@example.test'),'Authority actor','ACTIVE',true,'unused',now(),now());
 INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at) VALUES(tenant,'Authority fixture',actor,'ACTIVE',now(),now());
 INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),tenant,actor,'OWNER','ACTIVE');
 INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES(board,tenant,'Authority fixture Board',clock_timestamp(),clock_timestamp());
 INSERT INTO work_event_streams(tenant_id,board_id,last_sequence) VALUES(tenant,board,3);
 INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 VALUES(gen_random_uuid(),tenant,lower(recipient),recipient,encode(sha256(recipient::bytea),'hex'),'PORTAL','OWNER',actor,clock_timestamp(),clock_timestamp()+interval '1 day');
 -- Seek immediately before this owned tenant, independent of unrelated pending jobs.
 PERFORM set_config('app.tenant_id',tenant::text,true);
 FOREACH kind IN ARRAY ARRAY['BOARD_MEMBER_ADDED','BOARD_MEMBER_ROLE_CHANGED','BOARD_MEMBER_UPDATED'] LOOP
  ordinal:=ordinal+1; source:=gen_random_uuid(); lease:=gen_random_uuid();
  INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
  VALUES(tenant,source,board,ordinal,actor,kind,'Board',board,1,'membership-authority-fixture',clock_timestamp());
  SELECT job_id INTO job FROM invitation_recipient_authority_pages WHERE tenant_id=tenant AND source_event_id=source;
  IF job IS NULL OR NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_source_rows WHERE tenant_id=tenant AND event_id=source AND event_type=kind)
   OR NOT EXISTS(SELECT 1 FROM discover_invitation_recipient_authority_scopes((left(tenant::text,35)||'0')::uuid,100) WHERE tenant_id=tenant) THEN
   RAISE EXCEPTION 'Current Board membership event lost authority routing: %',kind;
  END IF;
  UPDATE background_jobs SET state='RUNNING',attempt_count=1,worker_id=worker_handle,lease_id=lease,lease_expires_at=clock_timestamp()+interval '2 minutes' WHERE id=job;
  SET LOCAL ROLE strataai_worker_runtime;
  succeeded:=deliver_invitation_recipient_authority(tenant,job,actor,worker_handle,lease,source,100);
  RESET ROLE;
  IF NOT succeeded OR (SELECT revision FROM invitation_recipient_authority_revisions WHERE email_normalized=recipient)<>ordinal
   OR (SELECT count(*) FROM invitation_recipient_authority_effects WHERE tenant_id=tenant AND source_event_id=source)<>1 THEN
   RAISE EXCEPTION 'Current Board membership event lost bounded recipient invalidation: %',kind;
  END IF;
  IF ordinal=1 THEN
   SELECT created_at INTO first_created FROM invitation_recipient_authority_revisions WHERE email_normalized=recipient;
   IF first_created IS NULL OR NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions
    WHERE email_normalized=recipient AND isfinite(created_at) AND updated_at=created_at
     AND created_at>=(SELECT created_at FROM work_events WHERE event_id=source AND tenant_id=tenant)) THEN
    RAISE EXCEPTION 'First actual authority publication clock missing'; END IF;
  ELSE
   IF NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions WHERE email_normalized=recipient
    AND created_at=first_created AND isfinite(updated_at) AND updated_at>=prior_updated
    AND updated_at>=(SELECT created_at FROM work_events WHERE event_id=source AND tenant_id=tenant)) THEN
    RAISE EXCEPTION 'Actual authority increment clock or creation identity lost'; END IF;
  END IF;
  SELECT updated_at,to_jsonb(r) INTO prior_updated,clock_body FROM invitation_recipient_authority_revisions r WHERE email_normalized=recipient;
  SET LOCAL ROLE strataai_worker_runtime;
  succeeded:=deliver_invitation_recipient_authority(tenant,job,actor,worker_handle,lease,source,100);
  RESET ROLE;
  IF NOT succeeded OR (SELECT revision FROM invitation_recipient_authority_revisions WHERE email_normalized=recipient)<>ordinal THEN
   RAISE EXCEPTION 'Current Board membership delivery retry duplicated revision';
  END IF;
  IF (SELECT to_jsonb(r) FROM invitation_recipient_authority_revisions r WHERE email_normalized=recipient) IS DISTINCT FROM clock_body THEN
   RAISE EXCEPTION 'Duplicate delivery restamped authority clocks'; END IF;
  UPDATE invitation_recipient_authority_revisions SET created_at='infinity',updated_at='-infinity' WHERE email_normalized=recipient;
  IF (SELECT to_jsonb(r) FROM invitation_recipient_authority_revisions r WHERE email_normalized=recipient) IS DISTINCT FROM clock_body THEN
   RAISE EXCEPTION 'Clock-only no-op rewrote authority publication time'; END IF;
  BEGIN
   UPDATE invitation_recipient_authority_revisions SET email_normalized=upper('other-'||recipient) WHERE email_normalized=recipient;
   RAISE EXCEPTION 'Authority counter identity rewritten';
  EXCEPTION WHEN check_violation THEN
   IF SQLERRM<>'Invitation authority revision identity is immutable' THEN RAISE; END IF;
  END;
  BEGIN
   UPDATE invitation_recipient_authority_revisions SET revision=0 WHERE email_normalized=recipient;
   RAISE EXCEPTION 'Invalid authority revision admitted';
  EXCEPTION WHEN check_violation THEN NULL; END;
  IF (SELECT to_jsonb(r) FROM invitation_recipient_authority_revisions r WHERE email_normalized=recipient) IS DISTINCT FROM clock_body THEN
   RAISE EXCEPTION 'Refused authority update retained payload/clock effects'; END IF;
 END LOOP;
 IF EXISTS(SELECT 1 FROM invitation_recipient_events WHERE tenant_id=tenant)
  OR EXISTS(SELECT 1 FROM work_events WHERE tenant_id=tenant AND ready_at IS NOT NULL) THEN
  RAISE EXCEPTION 'Membership authority routing invented recipient events or Work readiness';
 END IF;
 IF has_column_privilege('strataai_api_runtime','invitation_recipient_authority_revisions','created_at','SELECT')
  OR has_column_privilege('strataai_api_runtime','invitation_recipient_authority_revisions','updated_at','SELECT')
  OR has_table_privilege('strataai_worker_runtime','invitation_recipient_authority_revisions','UPDATE')
  OR has_function_privilege('strataai_worker_runtime','capture_invitation_authority_revision_clocks()','EXECUTE') THEN
  RAISE EXCEPTION 'Authority clock repair widened recipient/Worker capabilities'; END IF;
 RAISE NOTICE 'Current Board membership authority: three event families, bounded discovery, restricted delivery and idempotent revisions passed';
END $$;
ROLLBACK;
