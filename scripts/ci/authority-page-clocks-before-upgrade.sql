BEGIN;
DO $$ DECLARE actor uuid:=gen_random_uuid(); event uuid:=gen_random_uuid(); tenant uuid:=gen_random_uuid();
 first_job uuid:=gen_random_uuid(); next_job uuid:=gen_random_uuid(); BEGIN
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Authority page clock fixture','ACTIVE','unused',clock_timestamp(),clock_timestamp());
 UPDATE users SET status='DEACTIVATED',version=2,updated_at=clock_timestamp() WHERE id=actor;
 INSERT INTO identity_event_streams(user_id,last_sequence) VALUES(actor,1);
 INSERT INTO identity_events(event_id,user_id,sequence,actor_id,event_type,entity_id,entity_version,correlation_id)
 VALUES(event,actor,1,actor,'USER_DEACTIVATED',actor,2,'authority-page-clocks');
 INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
 VALUES(tenant,'Authority page clock fixture',actor,'2000-01-01','2000-01-01');
 INSERT INTO invitation_recipient_authority_sources(tenant_id,event_id,issuer_source_event_id) VALUES(tenant,event,event);
 INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,created_at,updated_at)
 VALUES(first_job,tenant,'INVITATION_RECIPIENT_AUTHORITY_PAGE','page-clock-first',actor,'invitation-recipient-authority','authority-page-clocks',jsonb_build_object('eventId',event),'2000-01-02','2000-01-02'),
 (next_job,tenant,'INVITATION_RECIPIENT_AUTHORITY_PAGE','page-clock-next',actor,'invitation-recipient-authority','authority-page-clocks',jsonb_build_object('eventId',event),'2000-01-03','2000-01-03');
 INSERT INTO invitation_recipient_authority_pages(tenant_id,job_id,source_event_id,after_id,after_created_at,completed_at,scanned_count)
 VALUES(tenant,first_job,event,'00000000-0000-0000-0000-000000000000','-infinity',NULL,NULL),
 (tenant,next_job,event,gen_random_uuid(),'2000-01-03','2000-01-04',0);
END $$;
CREATE TABLE authority_page_clock_upgrade_fixture AS SELECT p.tenant_id,p.job_id,to_jsonb(p) AS original,j.created_at AS owner_created_at
 FROM invitation_recipient_authority_pages p JOIN background_jobs j ON j.tenant_id=p.tenant_id AND j.id=p.job_id
 WHERE j.correlation_id='authority-page-clocks';
COMMIT;
