BEGIN;
CREATE TABLE card_reminders (
 tenant_id uuid NOT NULL, id uuid NOT NULL, user_id uuid NOT NULL, card_id uuid NOT NULL,
 interval_code text NOT NULL CHECK(interval_code IN ('AT_DUE','5_MINUTES','1_HOUR','1_DAY')),
 enabled boolean NOT NULL, due_at timestamptz, trigger_at timestamptz,
 status text NOT NULL CHECK(status IN ('SCHEDULED','SUSPENDED','CANCELLED','FIRED')),
 generation bigint NOT NULL CHECK(generation>0), created_at timestamptz NOT NULL,
 updated_at timestamptz NOT NULL CHECK(updated_at>=created_at), version bigint NOT NULL CHECK(version>0 AND version>=generation),
 PRIMARY KEY(tenant_id,id), UNIQUE(tenant_id,user_id,card_id),
 FOREIGN KEY(tenant_id,user_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT,
 FOREIGN KEY(card_id,tenant_id) REFERENCES cards(id,tenant_id) ON DELETE RESTRICT,
 CHECK((status IN ('SCHEDULED','FIRED') AND enabled AND due_at IS NOT NULL AND trigger_at IS NOT NULL AND trigger_at<=due_at)
  OR (status='SUSPENDED' AND enabled AND trigger_at IS NULL)
  OR (status='CANCELLED' AND NOT enabled AND due_at IS NULL AND trigger_at IS NULL)),
 CHECK(trigger_at IS NULL OR trigger_at=due_at-CASE interval_code WHEN 'AT_DUE' THEN interval '0 seconds'
   WHEN '5_MINUTES' THEN interval '5 minutes' WHEN '1_HOUR' THEN interval '1 hour' WHEN '1_DAY' THEN interval '24 hours' END)
);
CREATE INDEX ix_card_reminders_enabled_card ON card_reminders(tenant_id,card_id,user_id) WHERE enabled;
ALTER TABLE card_reminders ENABLE ROW LEVEL SECURITY;
ALTER TABLE card_reminders FORCE ROW LEVEL SECURITY;
CREATE POLICY card_reminders_tenant_isolation ON card_reminders
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
INSERT INTO schema_migrations(version) VALUES('037_card_reminders');
COMMIT;
