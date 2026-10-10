BEGIN;
CREATE TABLE configuration_attribution_upgrade_fixture(event_id uuid,source jsonb);
INSERT INTO configuration_attribution_upgrade_fixture
 SELECT event_id,to_jsonb(e) FROM organization_configuration_events e;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM configuration_attribution_upgrade_fixture) THEN
  RAISE EXCEPTION 'Configuration attribution upgrade has no historical source';
 END IF;
END $$;
-- A current caption is deliberately different from every legacy source time.
UPDATE users SET display_name='Current name must not backfill attribution',version=version+1,updated_at=GREATEST(clock_timestamp(),updated_at)
 WHERE id IN (SELECT actor_id FROM organization_configuration_events);
COMMIT;
