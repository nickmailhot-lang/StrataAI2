BEGIN;
DO $$ DECLARE actor uuid:=gen_random_uuid(); event uuid:=gen_random_uuid(); BEGIN
 IF EXISTS(SELECT 1 FROM invitation_issuer_authority_jobs) THEN
  RAISE EXCEPTION 'Issuer clock upgrade fixture needs an empty job set';
 END IF;
 INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES(actor,actor::text||'@example.test',upper(actor::text||'@example.test'),'Issuer clock fixture','ACTIVE','unused',clock_timestamp(),clock_timestamp());
 UPDATE users SET status='DEACTIVATED',version=2,updated_at=clock_timestamp() WHERE id=actor;
 INSERT INTO identity_event_streams(user_id,last_sequence) VALUES(actor,1);
 INSERT INTO identity_events(event_id,user_id,sequence,actor_id,event_type,entity_id,entity_version,correlation_id)
 VALUES(event,actor,1,actor,'USER_DEACTIVATED',actor,2,'issuer-clock-upgrade');
 INSERT INTO invitation_issuer_authority_jobs(id,event_id,actor_id,after_tenant,state,attempt_count,worker_id,lease_id,lease_expires_at,created_at,completed_at,scanned_count,failed_at,failure_code)
 VALUES
 (gen_random_uuid(),event,actor,gen_random_uuid(),'RUNNING',5,gen_random_uuid(),gen_random_uuid(),'2001-01-01','2000-01-01',NULL,NULL,NULL,NULL),
 (gen_random_uuid(),event,actor,gen_random_uuid(),'SUCCEEDED',1,NULL,NULL,NULL,'2000-01-01','2000-01-02',0,NULL,NULL),
 (gen_random_uuid(),event,actor,gen_random_uuid(),'FAILED',5,NULL,NULL,NULL,'2000-01-01',NULL,NULL,'2000-01-03','LEASE_EXHAUSTED');
END $$;
CREATE TABLE issuer_clock_upgrade_fixture AS SELECT id,to_jsonb(j) AS original FROM invitation_issuer_authority_jobs j;
COMMIT;
