BEGIN;
-- Fence existing writers through backfill and installation. Live readers must
-- join canonical work_events.ready_at; insertion does not imply delivery.
LOCK TABLE work_events IN SHARE ROW EXCLUSIVE MODE;
CREATE TABLE organization_board_event_streams (
 tenant_id uuid PRIMARY KEY REFERENCES organizations(id) ON DELETE RESTRICT,
 last_sequence bigint NOT NULL CHECK(last_sequence>=0),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 CHECK(updated_at>=created_at)
);
CREATE TABLE organization_board_events (
 tenant_id uuid NOT NULL, sequence bigint NOT NULL CHECK(sequence>0), event_id uuid NOT NULL,
 PRIMARY KEY(tenant_id,sequence), UNIQUE(tenant_id,event_id),
 FOREIGN KEY(tenant_id) REFERENCES organization_board_event_streams(tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,event_id) REFERENCES work_events(tenant_id,event_id) ON DELETE RESTRICT
);
ALTER TABLE organization_board_event_streams ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_board_event_streams FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_board_event_streams_tenant_isolation ON organization_board_event_streams
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
ALTER TABLE organization_board_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_board_events FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_board_events_tenant_isolation ON organization_board_events
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

-- Retain only actual canonical shared Board events. Stars remain in their
-- private actor journal; Card/Watch/Reminder activity does not enter this feed.
INSERT INTO organization_board_event_streams(tenant_id,last_sequence)
 SELECT tenant_id,count(*) FROM work_events WHERE entity_type='Board' AND entity_id=board_id
 AND event_type IN ('BOARD_CREATED','BOARD_UPDATED','BOARD_COPIED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED')
 GROUP BY tenant_id;
INSERT INTO organization_board_events(tenant_id,sequence,event_id)
 SELECT tenant_id,row_number() OVER(PARTITION BY tenant_id ORDER BY created_at,event_id),event_id
 FROM work_events WHERE entity_type='Board' AND entity_id=board_id
 AND event_type IN ('BOARD_CREATED','BOARD_UPDATED','BOARD_COPIED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED');

-- A narrow parent-row trigger capability projects only the actually inserted
-- source. No runtime role receives direct journal/counter writes. The counter
-- lock serializes commits across Boards and rolls back with the source command.
CREATE FUNCTION journal_organization_board_event() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE next_sequence bigint;
BEGIN
 IF NEW.entity_type<>'Board' OR NEW.entity_id<>NEW.board_id OR NEW.event_type NOT IN
  ('BOARD_CREATED','BOARD_UPDATED','BOARD_COPIED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED') THEN RETURN NEW; END IF;
 INSERT INTO public.organization_board_event_streams(tenant_id,last_sequence) VALUES(NEW.tenant_id,1)
 ON CONFLICT(tenant_id) DO UPDATE SET last_sequence=organization_board_event_streams.last_sequence+1,
 updated_at=GREATEST(organization_board_event_streams.updated_at,clock_timestamp())
 RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.organization_board_events(tenant_id,sequence,event_id) VALUES(NEW.tenant_id,next_sequence,NEW.event_id);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_organization_board_event() FROM PUBLIC;
CREATE TRIGGER organization_board_journal AFTER INSERT ON work_events
 FOR EACH ROW EXECUTE FUNCTION journal_organization_board_event();
CREATE FUNCTION guard_organization_board_event_history() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 RAISE EXCEPTION 'Organization Board event history is immutable' USING ERRCODE='23514';
END $$;
REVOKE ALL ON FUNCTION guard_organization_board_event_history() FROM PUBLIC;
CREATE TRIGGER organization_board_event_history BEFORE UPDATE OR DELETE ON organization_board_events
 FOR EACH ROW EXECUTE FUNCTION guard_organization_board_event_history();
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT ON organization_board_events,organization_board_event_streams TO strataai_api_runtime;
  REVOKE INSERT,UPDATE,DELETE ON organization_board_events,organization_board_event_streams FROM strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('073_organization_board_journal');
COMMIT;
