BEGIN;
-- Actor-private bounded retry receipts. No criteria, results or response JSON;
-- the intent digest is separate from the canonical empty event metadata.
ALTER TABLE navigation_interaction_events ADD CONSTRAINT navigation_event_actor_identity UNIQUE(actor_id,event_id);
CREATE TABLE navigation_interaction_replays (
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 request_id uuid NOT NULL CHECK(request_id<>'00000000-0000-0000-0000-000000000000'),
 event_id uuid NOT NULL UNIQUE,
 fingerprint text COLLATE "C" NOT NULL CHECK(length(fingerprint)=64 AND fingerprint ~ '^[0-9a-f]{64}$'),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 expires_at timestamptz NOT NULL,
 PRIMARY KEY(actor_id,request_id),
 FOREIGN KEY(actor_id,event_id) REFERENCES navigation_interaction_events(actor_id,event_id) ON DELETE RESTRICT,
 CHECK(isfinite(created_at) AND isfinite(expires_at) AND expires_at=created_at+interval '24 hours')
);
CREATE INDEX ix_navigation_replay_expiry ON navigation_interaction_replays(actor_id,expires_at,request_id);
ALTER TABLE navigation_interaction_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE navigation_interaction_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY navigation_replay_subject ON navigation_interaction_replays
 USING(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
 WITH CHECK(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE FUNCTION guard_navigation_interaction_replay() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN RAISE EXCEPTION 'Navigation retry receipt is immutable' USING ERRCODE='23514'; END $$;
REVOKE ALL ON FUNCTION guard_navigation_interaction_replay() FROM PUBLIC;
CREATE TRIGGER navigation_replay_immutable BEFORE UPDATE ON navigation_interaction_replays
 FOR EACH ROW EXECUTE FUNCTION guard_navigation_interaction_replay();

CREATE FUNCTION append_or_replay_navigation_interaction(p_request uuid,p_fingerprint text,p_event uuid,
 p_actor uuid,p_type text,p_organization uuid,p_board uuid,p_entity uuid,p_version bigint,p_created timestamptz)
 RETURNS TABLE(original_event uuid,original_created timestamptz)
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE receipt public.navigation_interaction_replays%ROWTYPE; original public.navigation_interaction_events%ROWTYPE; receipt_clock timestamptz;
BEGIN
 IF p_request IS NULL OR p_request='00000000-0000-0000-0000-000000000000'
  OR p_fingerprint IS NULL OR length(p_fingerprint)<>64 OR p_fingerprint !~ '^[0-9a-f]{64}$'
  OR p_actor IS NULL OR p_actor IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid
  OR p_event IS NULL OR p_event='00000000-0000-0000-0000-000000000000'
  OR p_entity IS NULL OR p_entity='00000000-0000-0000-0000-000000000000'
  OR p_created IS NULL OR NOT isfinite(p_created) OR p_created<='0001-01-01'::timestamptz THEN RETURN; END IF;
 -- Identity commands already lock this actor; direct restricted calls also
 -- serialize same-actor receipts without granting writes to runtime roles.
 PERFORM 1 FROM public.users WHERE id=p_actor AND status='ACTIVE' FOR UPDATE;
 IF NOT FOUND THEN RETURN; END IF;
 SELECT * INTO receipt FROM public.navigation_interaction_replays WHERE actor_id=p_actor AND request_id=p_request;
 IF FOUND THEN
  SELECT * INTO original FROM public.navigation_interaction_events WHERE actor_id=p_actor AND event_id=receipt.event_id;
  IF NOT FOUND OR original.event_type IS DISTINCT FROM p_type OR original.organization_id IS DISTINCT FROM p_organization
   OR original.board_id IS DISTINCT FROM p_board OR original.entity_version IS DISTINCT FROM p_version
   OR (original.entity_type<>'ApplicationContext' AND original.entity_id IS DISTINCT FROM p_entity)
   OR receipt.fingerprint<>p_fingerprint OR receipt.expires_at<=clock_timestamp() THEN RETURN; END IF;
  IF NOT public.append_navigation_interaction(original.event_id,p_actor,original.event_type,original.organization_id,
    original.board_id,original.entity_id,original.entity_version,original.created_at) THEN RETURN; END IF;
  RETURN QUERY SELECT original.event_id,original.created_at; RETURN;
 END IF;
 IF NOT public.append_navigation_interaction(p_event,p_actor,p_type,p_organization,p_board,p_entity,p_version,p_created) THEN RETURN; END IF;
 DELETE FROM public.navigation_interaction_replays r USING (
  SELECT actor_id,request_id FROM public.navigation_interaction_replays WHERE actor_id=p_actor AND expires_at<=clock_timestamp()
  ORDER BY expires_at,request_id LIMIT 100
 ) expired WHERE r.actor_id=expired.actor_id AND r.request_id=expired.request_id;
 IF (SELECT count(*) FROM public.navigation_interaction_replays WHERE actor_id=p_actor)>=1000 THEN
  RAISE EXCEPTION 'Navigation receipt capacity unavailable' USING ERRCODE='P0001'; END IF;
 receipt_clock:=clock_timestamp();
 INSERT INTO public.navigation_interaction_replays(actor_id,request_id,event_id,fingerprint,created_at,expires_at)
 VALUES(p_actor,p_request,p_event,p_fingerprint,receipt_clock,receipt_clock+interval '24 hours');
 RETURN QUERY SELECT p_event,p_created;
END $$;
REVOKE ALL ON FUNCTION append_or_replay_navigation_interaction(uuid,text,uuid,uuid,text,uuid,uuid,uuid,bigint,timestamptz) FROM PUBLIC;
DO $$ BEGIN IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
 GRANT EXECUTE ON FUNCTION append_or_replay_navigation_interaction(uuid,text,uuid,uuid,text,uuid,uuid,uuid,bigint,timestamptz) TO strataai_api_runtime;
END IF; END $$;
INSERT INTO schema_migrations(version) VALUES('080_navigation_interaction_replays');
COMMIT;
