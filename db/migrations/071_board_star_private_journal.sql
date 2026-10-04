BEGIN;
ALTER TABLE user_board_preferences ADD COLUMN id uuid NOT NULL DEFAULT gen_random_uuid(),
 ADD CONSTRAINT board_preference_event_identity UNIQUE(tenant_id,id,user_id,board_id);
CREATE TABLE board_star_events (
 tenant_id uuid NOT NULL, event_id uuid NOT NULL, actor_id uuid NOT NULL, board_id uuid NOT NULL,
 entity_id uuid NOT NULL, version bigint NOT NULL CHECK(version>0),
 event_type text NOT NULL DEFAULT 'BOARD_STARRED' CHECK(event_type='BOARD_STARRED'),
 entity_type text NOT NULL DEFAULT 'UserBoardPreference' CHECK(entity_type='UserBoardPreference'),
 metadata jsonb NOT NULL DEFAULT '{}'::jsonb CHECK(metadata='{}'::jsonb),
 created_at timestamptz NOT NULL,
 PRIMARY KEY(tenant_id,entity_id,version), UNIQUE(tenant_id,event_id),
 FOREIGN KEY(tenant_id,entity_id,actor_id,board_id)
  REFERENCES user_board_preferences(tenant_id,id,user_id,board_id) ON DELETE RESTRICT
);
CREATE INDEX board_star_events_actor_board ON board_star_events(tenant_id,actor_id,board_id,version);
ALTER TABLE board_star_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE board_star_events FORCE ROW LEVEL SECURITY;
CREATE POLICY board_star_events_tenant_isolation ON board_star_events
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

-- Only actual new transitions produce events. Legacy preferences acquire
-- identity but no invented history or creation attribution.
CREATE FUNCTION journal_board_star_transition() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  IF NEW.version<>1 OR NEW.created_at IS NULL OR NEW.created_at IS DISTINCT FROM NEW.updated_at THEN
   RAISE EXCEPTION 'Invalid preference baseline' USING ERRCODE='23514';
  END IF;
 ELSE
  IF ROW(NEW.tenant_id,NEW.id,NEW.user_id,NEW.board_id,NEW.created_at)
   IS DISTINCT FROM ROW(OLD.tenant_id,OLD.id,OLD.user_id,OLD.board_id,OLD.created_at)
   OR NEW.updated_at<OLD.updated_at THEN
   RAISE EXCEPTION 'Immutable preference identity or clock' USING ERRCODE='23514';
  END IF;
  IF NEW.starred IS NOT DISTINCT FROM OLD.starred THEN
   IF NEW.version<>OLD.version OR NEW.updated_at IS DISTINCT FROM OLD.updated_at THEN
    RAISE EXCEPTION 'Invalid preference no-op' USING ERRCODE='23514';
   END IF;
   RETURN NEW;
  END IF;
  IF NEW.version<>OLD.version+1 THEN
   RAISE EXCEPTION 'Invalid preference revision' USING ERRCODE='23514';
  END IF;
 END IF;
 INSERT INTO public.board_star_events(tenant_id,event_id,actor_id,board_id,entity_id,version,created_at)
 VALUES(NEW.tenant_id,gen_random_uuid(),NEW.user_id,NEW.board_id,NEW.id,NEW.version,NEW.updated_at);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_board_star_transition() FROM PUBLIC;
CREATE TRIGGER board_star_private_journal AFTER INSERT OR UPDATE ON user_board_preferences
 FOR EACH ROW EXECUTE FUNCTION journal_board_star_transition();
CREATE FUNCTION guard_board_star_event_history() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 RAISE EXCEPTION 'Preference event history is immutable' USING ERRCODE='23514';
END $$;
REVOKE ALL ON FUNCTION guard_board_star_event_history() FROM PUBLIC;
CREATE TRIGGER board_star_event_history BEFORE UPDATE OR DELETE ON board_star_events
 FOR EACH ROW EXECUTE FUNCTION guard_board_star_event_history();
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT ON board_star_events TO strataai_api_runtime;
  REVOKE INSERT,UPDATE,DELETE ON board_star_events FROM strataai_api_runtime;
  REVOKE DELETE ON user_board_preferences FROM strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('071_board_star_private_journal');
COMMIT;
