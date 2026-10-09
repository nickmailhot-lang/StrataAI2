INSERT INTO invitation_recipient_authority_revisions(email_normalized,revision)
 VALUES('LEGACY-AUTHORITY-CLOCK-A@EXAMPLE.TEST',2),('LEGACY-AUTHORITY-CLOCK-B@EXAMPLE.TEST',7);
CREATE TABLE authority_revision_clock_upgrade_fixture AS
 SELECT email_normalized,to_jsonb(r) AS body FROM invitation_recipient_authority_revisions r
 WHERE email_normalized IN ('LEGACY-AUTHORITY-CLOCK-A@EXAMPLE.TEST','LEGACY-AUTHORITY-CLOCK-B@EXAMPLE.TEST');
