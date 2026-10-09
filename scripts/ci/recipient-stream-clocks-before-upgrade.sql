BEGIN;
INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 VALUES('f23a0000-0000-4000-8000-000000000001','f20a0000-0000-4000-8000-000000000010',
 'recipient-clock@example.test','RECIPIENT-CLOCK@EXAMPLE.TEST',repeat('e3',32),'INTERNAL','MEMBER',
 'f20a0000-0000-4000-8000-000000000001','2020-01-02','2030-01-02');
INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 VALUES(gen_random_uuid(),'f20a0000-0000-4000-8000-000000000010','f20a0000-0000-4000-8000-000000000001',
 'ORGANIZATION_MEMBER_INVITED','Invitation','f23a0000-0000-4000-8000-000000000001','recipient-clock-upgrade');
CREATE TABLE recipient_stream_clock_upgrade_fixture AS
 SELECT email_normalized,to_jsonb(s) AS original FROM invitation_recipient_streams s;
INSERT INTO invitation_recipient_streams(email_normalized,last_sequence) VALUES('UNBACKED-RECIPIENT-CLOCK@EXAMPLE.TEST',1);
COMMIT;
