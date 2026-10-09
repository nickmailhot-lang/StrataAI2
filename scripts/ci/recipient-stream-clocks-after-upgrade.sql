DO $$ BEGIN
 IF (SELECT count(*) FROM recipient_stream_clock_upgrade_fixture)<>(SELECT count(*) FROM invitation_recipient_streams)
 OR EXISTS(SELECT 1 FROM recipient_stream_clock_upgrade_fixture f LEFT JOIN invitation_recipient_streams s USING(email_normalized)
  WHERE s.email_normalized IS NULL OR to_jsonb(s)-'created_at'-'updated_at' IS DISTINCT FROM f.original
   OR s.created_at IS DISTINCT FROM (SELECT created_at FROM invitation_recipient_events WHERE email_normalized=s.email_normalized AND sequence=1)
   OR s.updated_at IS DISTINCT FROM (SELECT max(created_at) FROM invitation_recipient_events WHERE email_normalized=s.email_normalized)) THEN
  RAISE EXCEPTION 'Recipient stream upgrade changed retained history';
 END IF;
END $$;
DROP TABLE recipient_stream_clock_upgrade_fixture;
BEGIN;
DO $$
DECLARE creation timestamptz; modified timestamptz;
BEGIN
 SELECT created_at,updated_at INTO STRICT creation,modified FROM invitation_recipient_streams WHERE email_normalized='RECIPIENT-CLOCK@EXAMPLE.TEST';
 BEGIN
  UPDATE invitation_recipient_streams SET created_at=created_at-interval '1 second' WHERE email_normalized='RECIPIENT-CLOCK@EXAMPLE.TEST';
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Recipient creation clock tamper admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE invitation_recipient_streams SET updated_at=updated_at+interval '100 years' WHERE email_normalized='RECIPIENT-CLOCK@EXAMPLE.TEST';
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Recipient update clock tamper admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE invitation_recipient_streams SET last_sequence=last_sequence+1 WHERE email_normalized='RECIPIENT-CLOCK@EXAMPLE.TEST';
  SET CONSTRAINTS ALL IMMEDIATE;
  RAISE EXCEPTION 'Recipient counter without source admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 UPDATE invitation_recipient_streams SET last_sequence=last_sequence WHERE email_normalized='RECIPIENT-CLOCK@EXAMPLE.TEST';
 SET CONSTRAINTS ALL IMMEDIATE;
 IF NOT EXISTS(SELECT 1 FROM invitation_recipient_streams WHERE email_normalized='RECIPIENT-CLOCK@EXAMPLE.TEST' AND created_at=creation AND updated_at=modified) THEN
  RAISE EXCEPTION 'Recipient no-op changed clocks';
 END IF;
 SET CONSTRAINTS ALL DEFERRED;
 UPDATE invitations SET revoked_at=created_at+interval '1 day',version=version+1 WHERE id='f23a0000-0000-4000-8000-000000000001';
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id)
 VALUES(gen_random_uuid(),'f20a0000-0000-4000-8000-000000000010','f20a0000-0000-4000-8000-000000000001',
 'INVITATION_REVOKED','Invitation','f23a0000-0000-4000-8000-000000000001','recipient-clock-command');
 SET CONSTRAINTS ALL IMMEDIATE;
 IF NOT EXISTS(SELECT 1 FROM invitation_recipient_streams s JOIN invitations i ON i.id='f23a0000-0000-4000-8000-000000000001'
  WHERE s.email_normalized='RECIPIENT-CLOCK@EXAMPLE.TEST' AND s.created_at=creation AND s.updated_at=i.updated_at
   AND s.updated_at>modified AND s.last_sequence=2) THEN
  RAISE EXCEPTION 'Recipient revocation did not synchronize source clocks';
 END IF;
END $$;
ROLLBACK;
