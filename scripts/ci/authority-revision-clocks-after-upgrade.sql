DO $$ BEGIN
 IF (SELECT count(*) FROM authority_revision_clock_upgrade_fixture)<>2 OR EXISTS(
  SELECT 1 FROM authority_revision_clock_upgrade_fixture f LEFT JOIN invitation_recipient_authority_revisions r USING(email_normalized)
  WHERE r.email_normalized IS NULL OR r.created_at IS NOT NULL OR r.updated_at IS NOT NULL
   OR (to_jsonb(r)-'created_at'-'updated_at') IS DISTINCT FROM f.body) THEN
  RAISE EXCEPTION 'Legacy authority counter or unknown clocks changed';
 END IF;
END $$;
BEGIN;
UPDATE invitation_recipient_authority_revisions SET created_at='infinity',updated_at='-infinity'
 WHERE email_normalized IN ('LEGACY-AUTHORITY-CLOCK-A@EXAMPLE.TEST','LEGACY-AUTHORITY-CLOCK-B@EXAMPLE.TEST');
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions
  WHERE email_normalized IN ('LEGACY-AUTHORITY-CLOCK-A@EXAMPLE.TEST','LEGACY-AUTHORITY-CLOCK-B@EXAMPLE.TEST')
   AND (created_at IS NOT NULL OR updated_at IS NOT NULL)) THEN
  RAISE EXCEPTION 'No-op invented legacy authority counter clocks';
 END IF;
END $$;
UPDATE invitation_recipient_authority_revisions SET revision=revision+1
 WHERE email_normalized='LEGACY-AUTHORITY-CLOCK-A@EXAMPLE.TEST';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_revisions
  WHERE email_normalized='LEGACY-AUTHORITY-CLOCK-A@EXAMPLE.TEST' AND revision=3
   AND created_at IS NULL AND updated_at IS NOT NULL AND isfinite(updated_at)) THEN
  RAISE EXCEPTION 'Actual legacy revision update lost unknown creation or known update';
 END IF;
END $$;
ROLLBACK;
