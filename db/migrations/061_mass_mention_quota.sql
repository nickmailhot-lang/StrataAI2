BEGIN;
CREATE TABLE mass_mention_reservations (
 tenant_id uuid NOT NULL, event_id uuid NOT NULL, board_id uuid NOT NULL,
 actor_id uuid NOT NULL, card_id uuid NOT NULL, card_version bigint NOT NULL CHECK(card_version>0),
 source_created_at timestamptz NOT NULL CHECK(isfinite(source_created_at)),
 reserved_at timestamptz NOT NULL CHECK(isfinite(reserved_at)),
 source_event_type text GENERATED ALWAYS AS ('MENTION_CREATED'::text) STORED,
 PRIMARY KEY(tenant_id,event_id),
 FOREIGN KEY(tenant_id,board_id,event_id,source_event_type)
  REFERENCES work_events(tenant_id,board_id,event_id,event_type) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,actor_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT
);
CREATE INDEX ix_mass_mention_reservations_window ON mass_mention_reservations(tenant_id,board_id,actor_id,reserved_at DESC);
ALTER TABLE mass_mention_reservations ENABLE ROW LEVEL SECURITY;
ALTER TABLE mass_mention_reservations FORCE ROW LEVEL SECURITY;
CREATE POLICY mass_mention_reservations_tenant ON mass_mention_reservations
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION enforce_mass_mention_reservation() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 IF TG_OP='UPDATE' THEN
  RAISE EXCEPTION 'Mass mention reservation is immutable' USING ERRCODE='23514';
 END IF;
 -- The canonical Board gate serializes count-and-insert, including direct
 -- runtime writes. Time comes from the database, never a caller-supplied field.
 PERFORM id FROM public.boards WHERE tenant_id=NEW.tenant_id AND id=NEW.board_id FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Mass mention Board is unavailable' USING ERRCODE='23514'; END IF;
 IF NOT EXISTS(SELECT 1 FROM public.work_events WHERE tenant_id=NEW.tenant_id AND board_id=NEW.board_id
  AND event_id=NEW.event_id AND event_type='MENTION_CREATED' AND entity_type='Card'
  AND entity_id=NEW.card_id AND actor_id=NEW.actor_id AND entity_version=NEW.card_version AND created_at=NEW.source_created_at) THEN
  RAISE EXCEPTION 'Mass mention source is unavailable' USING ERRCODE='23514';
 END IF;
 NEW.reserved_at=clock_timestamp();
 IF (SELECT count(*) FROM public.mass_mention_reservations WHERE tenant_id=NEW.tenant_id AND board_id=NEW.board_id
  AND actor_id=NEW.actor_id AND reserved_at>NEW.reserved_at-interval '10 minutes')>=3 THEN
  RAISE EXCEPTION 'Mass mention rate limit exceeded' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER mass_mention_reservation_guard BEFORE INSERT OR UPDATE ON mass_mention_reservations
 FOR EACH ROW EXECUTE FUNCTION enforce_mass_mention_reservation();
REVOKE ALL ON FUNCTION enforce_mass_mention_reservation() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('061_mass_mention_quota');
COMMIT;
