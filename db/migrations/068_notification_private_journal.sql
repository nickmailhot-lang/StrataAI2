BEGIN;
ALTER TABLE card_assignment_notifications ADD CONSTRAINT notification_recipient_identity UNIQUE(tenant_id,id,recipient_id);
CREATE TABLE notification_event_streams (
 tenant_id uuid NOT NULL, recipient_id uuid NOT NULL, last_sequence bigint NOT NULL CHECK(last_sequence>=0),
 PRIMARY KEY(tenant_id,recipient_id),
 FOREIGN KEY(tenant_id,recipient_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT
);
CREATE TABLE notification_events (
 tenant_id uuid NOT NULL, recipient_id uuid NOT NULL, sequence bigint NOT NULL CHECK(sequence>0),
 event_id uuid NOT NULL, notification_id uuid NOT NULL, board_id uuid NOT NULL, actor_id uuid NOT NULL,
 event_type text NOT NULL CHECK(event_type IN ('NOTIFICATION_CREATED','NOTIFICATION_READ')),
 version bigint NOT NULL, created_at timestamptz NOT NULL,
 metadata jsonb NOT NULL DEFAULT '{}'::jsonb CHECK(metadata='{}'::jsonb),
 PRIMARY KEY(tenant_id,recipient_id,sequence), UNIQUE(tenant_id,event_id), UNIQUE(tenant_id,notification_id,event_type),
 CHECK((event_type='NOTIFICATION_CREATED' AND version=1) OR (event_type='NOTIFICATION_READ' AND version=2)),
 FOREIGN KEY(tenant_id,notification_id,recipient_id) REFERENCES card_assignment_notifications(tenant_id,id,recipient_id) ON DELETE RESTRICT,
 FOREIGN KEY(board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,actor_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT
);
ALTER TABLE notification_event_streams ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_event_streams FORCE ROW LEVEL SECURITY;
CREATE POLICY notification_event_streams_tenant_isolation ON notification_event_streams
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
ALTER TABLE notification_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE notification_events FORCE ROW LEVEL SECURITY;
CREATE POLICY notification_events_tenant_isolation ON notification_events
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

-- The parent ALTER lock fences concurrent writes until backfill/trigger commit.
WITH transitions AS (
 SELECT n.*,t.event_type,t.effect_time,t.actor,t.version
 FROM card_assignment_notifications n CROSS JOIN LATERAL (VALUES
  ('NOTIFICATION_CREATED'::text,n.created_at,n.actor_id,1::bigint),
  ('NOTIFICATION_READ'::text,n.read_at,n.recipient_id,2::bigint)) t(event_type,effect_time,actor,version)
 WHERE t.effect_time IS NOT NULL
)
INSERT INTO notification_events(tenant_id,recipient_id,sequence,event_id,notification_id,board_id,actor_id,event_type,version,created_at)
 SELECT tenant_id,recipient_id,row_number() OVER(PARTITION BY tenant_id,recipient_id ORDER BY effect_time,id,event_type),
 gen_random_uuid(),id,board_id,actor,event_type,version,effect_time FROM transitions;
INSERT INTO notification_event_streams(tenant_id,recipient_id,last_sequence)
 SELECT tenant_id,recipient_id,max(sequence) FROM notification_events GROUP BY tenant_id,recipient_id;

-- No caller gets direct journal writes. Trigger capabilities use only their
-- actual parent row and retain the owning notification transaction, including
-- the Worker's narrow Reminder-delivery function.
CREATE FUNCTION append_notification_journal_transition(n public.card_assignment_notifications,p_type text,p_time timestamptz)
 RETURNS void LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE next_sequence bigint;
BEGIN
 IF p_type NOT IN ('NOTIFICATION_CREATED','NOTIFICATION_READ') OR p_time IS NULL THEN
  RAISE EXCEPTION 'Invalid notification transition' USING ERRCODE='23514';
 END IF;
 -- Different Board mutations can notify overlapping recipients in different
 -- orders. Fence journal effects before acquiring any recipient counter so
 -- their owning transactions cannot deadlock on reversed recipient locks.
 PERFORM pg_advisory_xact_lock(hashtextextended('strataai.notification-journal/'||n.tenant_id::text,0));
 INSERT INTO public.notification_event_streams(tenant_id,recipient_id,last_sequence) VALUES(n.tenant_id,n.recipient_id,1)
 ON CONFLICT(tenant_id,recipient_id) DO UPDATE SET last_sequence=notification_event_streams.last_sequence+1
 RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.notification_events(tenant_id,recipient_id,sequence,event_id,notification_id,board_id,actor_id,event_type,version,created_at)
 VALUES(n.tenant_id,n.recipient_id,next_sequence,gen_random_uuid(),n.id,n.board_id,
  CASE WHEN p_type='NOTIFICATION_CREATED' THEN n.actor_id ELSE n.recipient_id END,p_type,
  CASE WHEN p_type='NOTIFICATION_CREATED' THEN 1 ELSE 2 END,p_time);
END $$;
REVOKE ALL ON FUNCTION append_notification_journal_transition(public.card_assignment_notifications,text,timestamptz) FROM PUBLIC;
CREATE FUNCTION journal_notification_transition() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  PERFORM public.append_notification_journal_transition(NEW,'NOTIFICATION_CREATED',NEW.created_at);
  IF NEW.read_at IS NOT NULL THEN PERFORM public.append_notification_journal_transition(NEW,'NOTIFICATION_READ',NEW.read_at); END IF;
 ELSE
  IF OLD.read_at IS NOT NULL AND NEW.read_at IS DISTINCT FROM OLD.read_at THEN
   RAISE EXCEPTION 'First notification read time is immutable' USING ERRCODE='23514';
  END IF;
  IF OLD.read_at IS NULL AND NEW.read_at IS NOT NULL THEN
   PERFORM public.append_notification_journal_transition(NEW,'NOTIFICATION_READ',NEW.read_at);
  END IF;
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION journal_notification_transition() FROM PUBLIC;
CREATE TRIGGER notification_private_journal AFTER INSERT OR UPDATE OF read_at ON card_assignment_notifications
 FOR EACH ROW EXECUTE FUNCTION journal_notification_transition();
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT ON notification_events,notification_event_streams TO strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('068_notification_private_journal');
COMMIT;
