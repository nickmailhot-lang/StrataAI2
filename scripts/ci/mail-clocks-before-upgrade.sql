INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('13100000-0000-0000-0000-000000000102','mail-clock@example.test','MAIL-CLOCK@EXAMPLE.TEST','Clock fixture','ACTIVE',true,'fixture',now(),now());
INSERT INTO organizations(id,name,created_at,updated_at)
 VALUES('13100000-0000-0000-0000-000000000101','Legacy mail clock',now(),now());
INSERT INTO organization_members(id,user_id,tenant_id,role,status)
 VALUES(gen_random_uuid(),'13100000-0000-0000-0000-000000000102','13100000-0000-0000-0000-000000000101','OWNER','ACTIVE');
INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 SELECT md5('mail-clock-invitation-'||n)::uuid,'13100000-0000-0000-0000-000000000101',
 'mail-clock-recipient-'||n||'@example.test',upper('mail-clock-recipient-'||n||'@example.test'),md5('mail-clock-token-'||n)||md5('mail-clock-token-'||n),
 'INTERNAL','MEMBER','13100000-0000-0000-0000-000000000102',now()-interval '2 days',now()+interval '1 day'
 FROM generate_series(1,3) n;
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,state,attempt_count,worker_id,lease_id,lease_expires_at)
 SELECT md5('mail-clock-job-'||n)::uuid,'13100000-0000-0000-0000-000000000101','INVITATION_EMAIL',
 'mail-clock-job-'||n,'13100000-0000-0000-0000-000000000102','invitation-email-delivery','mail-clock',
 jsonb_build_object('invitationId',md5('mail-clock-invitation-'||n)::uuid),'RUNNING',1,
 '13100000-0000-0000-0000-000000000103','13100000-0000-0000-0000-000000000104',now()+interval '5 minutes'
 FROM generate_series(1,3) n;
INSERT INTO invitation_mail_intents(job_id,tenant_id,invitation_id,issuer_id,recipient_email,target_surface,target_role,expires_at,key_id,sender_address,public_origin,provider_account,template_version,state,provider_receipt_id,finished_at,created_at)
 SELECT md5('mail-clock-job-'||n)::uuid,i.tenant_id,i.id,i.created_by_user_id,i.invited_email,i.target_surface,i.target_role,i.expires_at,
 'clock-key','sender@example.test','https://example.test','clock-provider',1,
 CASE WHEN n=2 THEN 'SENT' ELSE 'PENDING' END,CASE WHEN n=2 THEN md5('mail-clock-receipt')::uuid END,
 CASE WHEN n=2 THEN now()-interval '1 day' END,i.created_at
 FROM generate_series(1,3) n JOIN invitations i ON i.id=md5('mail-clock-invitation-'||n)::uuid;
-- Historically admissible pending payload change without a recorded clock.
UPDATE invitation_mail_intents SET sender_address='later-sender@example.test' WHERE job_id=md5('mail-clock-job-3')::uuid;
CREATE TABLE mail_clock_upgrade_fixture AS
 SELECT job_id,to_jsonb(m) AS body FROM invitation_mail_intents m WHERE tenant_id='13100000-0000-0000-0000-000000000101';
