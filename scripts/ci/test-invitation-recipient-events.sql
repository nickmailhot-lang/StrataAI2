-- PRD-03/PRD-60: future actual source transitions, cross-Organization recipient
-- order, exact origin, rollback and restricted RLS/capabilities. SET ROLE proves
-- database permissions; this does not prove browser/session or Worker delivery.
BEGIN;
DO $$
DECLARE owner_id uuid:=gen_random_uuid(); recipient uuid:=gen_random_uuid(); tenant uuid:=gen_random_uuid();
 other_tenant uuid:=gen_random_uuid(); board uuid:=gen_random_uuid(); email text:=upper('recipient-'||recipient||'@example.test');
 invitation uuid; audit_id uuid; internal_id uuid; portal_id uuid; board_id uuid; revoked_id uuid; before_sequence bigint;
 surface text; refused boolean;
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES(owner_id,'owner-'||owner_id||'@example.test',upper('owner-'||owner_id||'@example.test'),'Source Owner','ACTIVE',true,'unused-source-hash',now(),now()),
 (recipient,lower(email),email,'Source recipient','ACTIVE',true,'unused-source-hash',now(),now());
 INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at)
 VALUES(tenant,'Private Internal source',owner_id,'ACTIVE',now(),now()),(other_tenant,'Private Portal source',owner_id,'ACTIVE',now(),now());
 INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 VALUES(gen_random_uuid(),tenant,owner_id,'OWNER','ACTIVE'),(gen_random_uuid(),other_tenant,owner_id,'OWNER','ACTIVE');
 INSERT INTO boards(id,tenant_id,name,lifecycle_state,created_at,updated_at)
 VALUES(board,tenant,'Private source Board','ACTIVE',now(),now());
 FOREACH surface IN ARRAY ARRAY['INTERNAL','PORTAL','BOARD','REVOKE'] LOOP
  invitation:=gen_random_uuid(); audit_id:=gen_random_uuid();
  INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
   created_by_user_id,created_at,expires_at,target_board_id,target_board_role)
  VALUES(invitation,CASE WHEN surface='PORTAL' THEN other_tenant ELSE tenant END,lower(email),email,
   encode(sha256(invitation::text::bytea),'hex'),CASE WHEN surface='PORTAL' THEN 'PORTAL' ELSE 'INTERNAL' END,
   CASE WHEN surface='PORTAL' THEN 'OWNER' ELSE 'MEMBER' END,owner_id,clock_timestamp(),clock_timestamp()+interval '1 day',
   CASE WHEN surface IN ('BOARD','REVOKE') THEN board END,CASE WHEN surface IN ('BOARD','REVOKE') THEN 'ADMIN' END);
  INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
  VALUES(audit_id,CASE WHEN surface='PORTAL' THEN other_tenant ELSE tenant END,owner_id,
   CASE WHEN surface IN ('BOARD','REVOKE') THEN 'BOARD_MEMBER_INVITED' ELSE 'ORGANIZATION_MEMBER_INVITED' END,
   'Invitation',invitation,'recipient-source-create','{}');
  IF surface='INTERNAL' THEN internal_id:=invitation;
  ELSIF surface='PORTAL' THEN portal_id:=invitation;
  ELSIF surface='BOARD' THEN board_id:=invitation;
  ELSE revoked_id:=invitation; END IF;
 END LOOP;
 INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),tenant,recipient,'MEMBER','ACTIVE');
 UPDATE invitations SET accepted_at=clock_timestamp(),accepted_by_user_id=recipient WHERE id=internal_id;
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 VALUES(gen_random_uuid(),tenant,recipient,'INVITATION_ACCEPTED','Invitation',internal_id,'recipient-internal-accept');
 INSERT INTO portal_access(id,tenant_id,user_id,status,relationship_type) VALUES(gen_random_uuid(),other_tenant,recipient,'ACTIVE','OWNER');
 UPDATE invitations SET accepted_at=clock_timestamp(),accepted_by_user_id=recipient WHERE id=portal_id;
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 VALUES(gen_random_uuid(),other_tenant,recipient,'INVITATION_ACCEPTED','Invitation',portal_id,'recipient-portal-accept');
 IF EXISTS(SELECT 1 FROM organization_members WHERE tenant_id=other_tenant AND user_id=recipient)
  OR EXISTS(SELECT 1 FROM board_members WHERE tenant_id=other_tenant AND user_id=recipient) THEN
  RAISE EXCEPTION 'Portal source manufactured Internal or Board membership';
 END IF;
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),tenant,board,recipient,'ADMIN','ACTIVE',now(),now());
 UPDATE invitations SET accepted_at=clock_timestamp(),accepted_by_user_id=recipient WHERE id=board_id;
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 VALUES(gen_random_uuid(),tenant,recipient,'INVITATION_ACCEPTED','Invitation',board_id,'recipient-board-accept');
 UPDATE invitations SET revoked_at=clock_timestamp() WHERE id=revoked_id;
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 VALUES(gen_random_uuid(),tenant,owner_id,'INVITATION_REVOKED','Invitation',revoked_id,'recipient-board-revoke');
 IF (SELECT count(*) FROM invitation_recipient_events WHERE email_normalized=email)<>8
  OR (SELECT count(*) FROM invitation_recipient_proofs WHERE email_normalized=email)<>8
  OR (SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=email)<>8
  OR (SELECT array_agg(sequence ORDER BY sequence) FROM invitation_recipient_events WHERE email_normalized=email)<>ARRAY[1,2,3,4,5,6,7,8]::bigint[] THEN
  RAISE EXCEPTION 'Recipient source transitions or cross-Organization order are incomplete';
 END IF;
 IF EXISTS(SELECT 1 FROM invitation_recipient_events e JOIN audit_events a ON a.id=e.event_id
   JOIN invitation_recipient_proofs p ON (p.invitation_id,p.tenant_id,p.entity_version)=(e.entity_id,e.tenant_id,e.entity_version)
   WHERE e.email_normalized=email AND (e.actor_id<>a.actor_id OR e.source_event_type<>a.event_type OR e.tenant_id<>a.tenant_id
    OR e.entity_type<>'Invitation' OR e.entity_id<>a.entity_id OR e.correlation_id<>a.correlation_id
    OR e.metadata<>'{}'::jsonb OR e.created_at<>p.created_at OR e.event_type<>p.event_type)) THEN
  RAISE EXCEPTION 'Recipient event canonical source attribution changed';
 END IF;
 -- A duplicate audit cannot publish another event or leave a sequence gap.
 refused:=false;
 BEGIN
  INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
  VALUES(gen_random_uuid(),tenant,owner_id,'INVITATION_REVOKED','Invitation',revoked_id,'duplicate-source');
 EXCEPTION WHEN unique_violation THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Duplicate invitation source was published'; END IF;
 -- Portal's source bypasses no Internal projection checks: its own late
 -- journal insertion failure must roll back command, route, proof and counter.
 invitation:=gen_random_uuid(); before_sequence:=(SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=email); refused:=false;
 BEGIN
  INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
  VALUES(invitation,other_tenant,lower(email),email,encode(sha256(invitation::text::bytea),'hex'),'PORTAL','OWNER',owner_id,clock_timestamp(),clock_timestamp()+interval '1 day');
  INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
  VALUES(gen_random_uuid(),other_tenant,owner_id,'ORGANIZATION_MEMBER_INVITED','Invitation',invitation,repeat('x',65));
 EXCEPTION WHEN check_violation THEN refused:=true; END;
 IF NOT refused OR EXISTS(SELECT 1 FROM invitations WHERE id=invitation)
  OR EXISTS(SELECT 1 FROM invitation_routes WHERE invitation_id=invitation)
  OR EXISTS(SELECT 1 FROM invitation_recipient_proofs WHERE invitation_id=invitation)
  OR (SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=email)<>before_sequence THEN
  RAISE EXCEPTION 'Late recipient source failure did not roll back the complete command';
 END IF;
 -- A persisted accepted marker without the matching Portal grant is not a
 -- publishable acceptance. Failure must also undo its preceding creation.
 invitation:=gen_random_uuid(); refused:=false;
 BEGIN
  INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
  VALUES(invitation,other_tenant,lower(email),email,encode(sha256(invitation::text::bytea),'hex'),'PORTAL','TENANT',owner_id,clock_timestamp(),clock_timestamp()+interval '1 day');
  INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
  VALUES(gen_random_uuid(),other_tenant,owner_id,'ORGANIZATION_MEMBER_INVITED','Invitation',invitation,'missing-portal-grant-create');
  UPDATE invitations SET accepted_at=clock_timestamp(),accepted_by_user_id=recipient WHERE id=invitation;
  INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
  VALUES(gen_random_uuid(),other_tenant,recipient,'INVITATION_ACCEPTED','Invitation',invitation,'missing-portal-grant-accept');
 EXCEPTION WHEN check_violation THEN refused:=true; END;
 IF NOT refused OR EXISTS(SELECT 1 FROM invitations WHERE id=invitation)
  OR EXISTS(SELECT 1 FROM invitation_recipient_events WHERE entity_id=invitation)
  OR (SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=email)<>before_sequence THEN
  RAISE EXCEPTION 'Unproven Portal grant published or left source state';
 END IF;
 refused:=false;
 BEGIN UPDATE invitation_recipient_events SET metadata='{}' WHERE email_normalized=email;
 EXCEPTION WHEN check_violation THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Recipient history is mutable'; END IF;
 refused:=false;
 BEGIN DELETE FROM invitation_recipient_proofs WHERE email_normalized=email;
 EXCEPTION WHEN check_violation THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Recipient transition proof can be erased'; END IF;
 refused:=false;
 BEGIN DELETE FROM invitations WHERE id=portal_id;
 EXCEPTION WHEN check_violation OR foreign_key_violation THEN refused:=true; END;
 IF NOT refused OR NOT EXISTS(SELECT 1 FROM invitations WHERE id=portal_id)
  OR (SELECT count(*) FROM invitation_recipient_events WHERE email_normalized=email)<>8 THEN
  RAISE EXCEPTION 'Published invitation/history can be erased through parent deletion';
 END IF;
 invitation:=gen_random_uuid();
 INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 VALUES(invitation,other_tenant,lower(email),email,encode(sha256(invitation::text::bytea),'hex'),'PORTAL','OWNER',owner_id,clock_timestamp(),clock_timestamp()+interval '1 day');
 IF NOT EXISTS(SELECT 1 FROM invitation_recipient_proofs WHERE invitation_id=invitation)
  OR EXISTS(SELECT 1 FROM invitation_recipient_events WHERE entity_id=invitation) THEN
  RAISE EXCEPTION 'Unpublished parent cleanup fixture has no actual capture';
 END IF;
 refused:=false;
 BEGIN DELETE FROM invitation_recipient_proofs WHERE invitation_id=invitation;
 EXCEPTION WHEN check_violation THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Unpublished proof can be erased independently of its parent'; END IF;
 DELETE FROM invitations WHERE id=invitation;
 IF EXISTS(SELECT 1 FROM invitation_recipient_proofs WHERE invitation_id=invitation)
  OR EXISTS(SELECT 1 FROM invitation_routes WHERE invitation_id=invitation)
  OR (SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=email)<>before_sequence THEN
  RAISE EXCEPTION 'Unpublished parent cleanup failed or altered committed history';
 END IF;
 PERFORM set_config('test.recipient_email',email,true);
END $$;
SET LOCAL ROLE strataai_api_runtime;
DO $$
DECLARE refused boolean;
BEGIN
 IF current_user<>'strataai_api_runtime' OR EXISTS(SELECT 1 FROM pg_roles WHERE rolname=current_user AND (rolsuper OR rolbypassrls)) THEN
  RAISE EXCEPTION 'Recipient capability checks require the restricted API role';
 END IF;
 PERFORM set_config('app.route_kind','INVITATION_RECIPIENT',true);
 PERFORM set_config('app.route_key',current_setting('test.recipient_email'),true);
 IF (SELECT count(event_id) FROM invitation_recipient_events)<>8 OR (SELECT max(last_sequence) FROM invitation_recipient_streams)<>8 THEN
  RAISE EXCEPTION 'Recipient cannot read its own neutral invalidation source';
 END IF;
 PERFORM set_config('app.route_key','OTHER-RECIPIENT@EXAMPLE.TEST',true);
 IF (SELECT count(event_id) FROM invitation_recipient_events)<>0 OR (SELECT count(last_sequence) FROM invitation_recipient_streams)<>0 THEN
  RAISE EXCEPTION 'Recipient source crossed an email routing boundary';
 END IF;
 PERFORM set_config('app.route_key',current_setting('test.recipient_email'),true);
 PERFORM set_config('app.route_kind','INVITATION_ID',true);
 IF (SELECT count(event_id) FROM invitation_recipient_events)<>0 THEN RAISE EXCEPTION 'Invitation-ID routing exposed recipient history'; END IF;
 refused:=false;
 BEGIN PERFORM tenant_id FROM invitation_recipient_events; EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Recipient capability exposes domain scope references'; END IF;
 refused:=false;
 BEGIN PERFORM invitation_id FROM invitation_recipient_proofs; EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'API can inspect private source proofs'; END IF;
 refused:=false;
 BEGIN UPDATE invitation_recipient_streams SET last_sequence=99; EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'API can manufacture recipient sequence'; END IF;
 refused:=false;
 BEGIN INSERT INTO invitation_recipient_streams(email_normalized,last_sequence) VALUES('OTHER-RECIPIENT@EXAMPLE.TEST',1);
 EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'API can manufacture recipient stream'; END IF;
 IF has_function_privilege(current_user,'capture_invitation_recipient_transition()','EXECUTE')
  OR has_function_privilege(current_user,'journal_invitation_recipient_event()','EXECUTE') THEN
  RAISE EXCEPTION 'API has raw source-projection capability';
 END IF;
END $$;
SET LOCAL ROLE strataai_worker_runtime;
DO $$
DECLARE refused boolean:=false;
BEGIN
 BEGIN PERFORM event_id FROM invitation_recipient_events; EXCEPTION WHEN insufficient_privilege THEN refused:=true; END;
 IF NOT refused THEN RAISE EXCEPTION 'Worker can inspect the global recipient source'; END IF;
END $$;
RESET ROLE;
DO $$
DECLARE owner_id uuid:=gen_random_uuid(); recipient uuid:=gen_random_uuid(); tenant uuid:=gen_random_uuid();
 board uuid:=gen_random_uuid(); invitation uuid; email text:=upper('retained-admin-'||recipient||'@example.test');
 scenario text; refused boolean; before_sequence bigint;
BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES(owner_id,'retained-owner-'||owner_id||'@example.test',upper('retained-owner-'||owner_id||'@example.test'),'Owner','ACTIVE',true,'fixture',now(),now()),
  (recipient,lower(email),email,'Recipient','ACTIVE',true,'fixture',now(),now());
 INSERT INTO organizations(id,name,owner_user_id,status,created_at,updated_at) VALUES(tenant,'Retained grant source',owner_id,'ACTIVE',now(),now());
 INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 VALUES(gen_random_uuid(),tenant,owner_id,'OWNER','ACTIVE'),(gen_random_uuid(),tenant,recipient,'MEMBER','ACTIVE');
 INSERT INTO boards(id,tenant_id,name,lifecycle_state,created_at,updated_at) VALUES(board,tenant,'Retained Admin Board','ACTIVE',now(),now());
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),tenant,board,recipient,'ADMIN','ACTIVE',now(),now());
 FOREACH scenario IN ARRAY ARRAY['retained','insufficient','inactive'] LOOP
  invitation:=gen_random_uuid();
  INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
   target_board_id,target_board_role,created_by_user_id,created_at,expires_at)
  VALUES(invitation,tenant,lower(email),email,encode(sha256(invitation::text::bytea),'hex'),'INTERNAL','MEMBER',
   board,CASE WHEN scenario='insufficient' THEN 'ADMIN' ELSE 'MEMBER' END,owner_id,clock_timestamp(),now()+interval '1 day');
  INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
  VALUES(gen_random_uuid(),tenant,owner_id,'BOARD_MEMBER_INVITED','Invitation',invitation,'retained-grant-create');
  before_sequence:=(SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=email); refused:=false;
  BEGIN
   UPDATE board_members SET role=CASE WHEN scenario='insufficient' THEN 'MEMBER' ELSE 'ADMIN' END,
    status=CASE WHEN scenario='inactive' THEN 'INACTIVE' ELSE 'ACTIVE' END WHERE tenant_id=tenant AND board_id=board AND user_id=recipient;
   UPDATE invitations SET accepted_at=clock_timestamp(),accepted_by_user_id=recipient WHERE id=invitation;
   INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
   VALUES(gen_random_uuid(),tenant,recipient,'INVITATION_ACCEPTED','Invitation',invitation,'retained-grant-accept');
  EXCEPTION WHEN check_violation THEN refused:=true; END;
  IF scenario='retained' THEN
   IF refused OR (SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=email)<>before_sequence+1
    OR (SELECT count(*) FROM invitation_recipient_events WHERE entity_id=invitation AND event_type='INVITATION_ACCEPTED')<>1 THEN
    RAISE EXCEPTION 'Retained Board Admin cannot publish a Member invitation acceptance';
   END IF;
  ELSIF NOT refused OR EXISTS(SELECT 1 FROM invitations WHERE id=invitation AND accepted_at IS NOT NULL)
   OR (SELECT count(*) FROM invitation_recipient_proofs WHERE invitation_id=invitation)<>1
   OR (SELECT last_sequence FROM invitation_recipient_streams WHERE email_normalized=email)<>before_sequence THEN
   RAISE EXCEPTION 'Insufficient or inactive Board grant manufactured accepted source history';
  END IF;
 END LOOP;
END $$;
ROLLBACK;
