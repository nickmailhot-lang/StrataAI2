BEGIN;
-- Actor-private bounded retry receipts. No criteria, results or response JSON;
-- the intent digest is separate from the canonical empty event metadata.
ALTER TABLE search_interaction_events ADD CONSTRAINT search_event_actor_identity UNIQUE(actor_id,event_id);
CREATE TABLE board_filter_interaction_replays (
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 request_id uuid NOT NULL CHECK(request_id<>'00000000-0000-0000-0000-000000000000'),
 event_id uuid NOT NULL UNIQUE,
 fingerprint text COLLATE "C" NOT NULL CHECK(length(fingerprint)=64 AND fingerprint ~ '^[0-9a-f]{64}$'),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 expires_at timestamptz NOT NULL,
 PRIMARY KEY(actor_id,request_id),
 FOREIGN KEY(actor_id,event_id) REFERENCES search_interaction_events(actor_id,event_id) ON DELETE RESTRICT,
 CHECK(isfinite(created_at) AND isfinite(expires_at) AND expires_at=created_at+interval '24 hours')
);
CREATE INDEX ix_board_filter_replay_expiry ON board_filter_interaction_replays(actor_id,expires_at,request_id);
ALTER TABLE board_filter_interaction_replays ENABLE ROW LEVEL SECURITY;
ALTER TABLE board_filter_interaction_replays FORCE ROW LEVEL SECURITY;
CREATE POLICY board_filter_replay_subject ON board_filter_interaction_replays
 USING(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
 WITH CHECK(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE FUNCTION guard_board_filter_interaction_replay() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN RAISE EXCEPTION 'Board filter retry receipt is immutable' USING ERRCODE='23514'; END $$;
REVOKE ALL ON FUNCTION guard_board_filter_interaction_replay() FROM PUBLIC;
CREATE TRIGGER board_filter_replay_immutable BEFORE UPDATE ON board_filter_interaction_replays
 FOR EACH ROW EXECUTE FUNCTION guard_board_filter_interaction_replay();

CREATE FUNCTION append_or_replay_board_filter_interaction(p_request uuid,p_fingerprint text,p_event uuid,
 p_actor uuid,p_organization uuid,p_board uuid,p_created timestamptz)
 RETURNS TABLE(original_event uuid,original_created timestamptz)
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE receipt public.board_filter_interaction_replays%ROWTYPE;
 original public.search_interaction_events%ROWTYPE; receipt_clock timestamptz;
BEGIN
 IF p_request IS NULL OR p_request='00000000-0000-0000-0000-000000000000'
  OR p_fingerprint IS NULL OR length(p_fingerprint)<>64 OR p_fingerprint !~ '^[0-9a-f]{64}$'
  OR p_actor IS NULL OR p_actor IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid
  OR p_event IS NULL OR p_event='00000000-0000-0000-0000-000000000000'
  OR p_organization IS NULL OR p_board IS NULL OR p_created IS NULL
  OR NOT isfinite(p_created) OR p_created<='0001-01-01'::timestamptz THEN
  RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
 PERFORM 1 FROM public.users WHERE id=p_actor AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
 -- Same actor stream lock as append: concurrent retries observe one receipt.
 INSERT INTO public.search_interaction_streams(actor_id) VALUES(p_actor) ON CONFLICT DO NOTHING;
 PERFORM 1 FROM public.search_interaction_streams WHERE actor_id=p_actor FOR UPDATE;
 SELECT * INTO receipt FROM public.board_filter_interaction_replays
  WHERE actor_id=p_actor AND request_id=p_request;
 IF FOUND THEN
  SELECT * INTO original FROM public.search_interaction_events
   WHERE actor_id=p_actor AND event_id=receipt.event_id;
  IF NOT FOUND OR original.event_type<>'BOARD_FILTER_CHANGED'
   OR original.organization_id IS DISTINCT FROM p_organization OR original.board_id IS DISTINCT FROM p_board THEN
   RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
  -- A stored receipt never bypasses current Board admission.
  PERFORM public.append_search_interaction(original.event_id,p_actor,'BOARD_FILTER_CHANGED',
   p_organization,p_board,original.created_at);
  IF receipt.fingerprint<>p_fingerprint OR receipt.expires_at<=clock_timestamp() THEN
   RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
  RETURN QUERY SELECT original.event_id,original.created_at; RETURN;
 END IF;
 PERFORM public.append_search_interaction(p_event,p_actor,'BOARD_FILTER_CHANGED',p_organization,p_board,p_created);
 -- Cleanup is bounded, actor-scoped and expired-only. Never evict a live
 -- receipt or delete its immutable source to make room for another request.
 DELETE FROM public.board_filter_interaction_replays r USING (
  SELECT actor_id,request_id FROM public.board_filter_interaction_replays
  WHERE actor_id=p_actor AND expires_at<=clock_timestamp() ORDER BY expires_at,request_id LIMIT 100
 ) expired WHERE r.actor_id=expired.actor_id AND r.request_id=expired.request_id;
 IF (SELECT count(*) FROM public.board_filter_interaction_replays WHERE actor_id=p_actor)>=1000 THEN
  RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
 receipt_clock:=clock_timestamp();
 INSERT INTO public.board_filter_interaction_replays(actor_id,request_id,event_id,fingerprint,created_at,expires_at)
 VALUES(p_actor,p_request,p_event,p_fingerprint,receipt_clock,receipt_clock+interval '24 hours');
 RETURN QUERY SELECT p_event,p_created;
END $$;
REVOKE ALL ON FUNCTION append_or_replay_board_filter_interaction(uuid,text,uuid,uuid,uuid,uuid,timestamptz) FROM PUBLIC;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT EXECUTE ON FUNCTION append_or_replay_board_filter_interaction(uuid,text,uuid,uuid,uuid,uuid,timestamptz) TO strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('077_board_filter_interaction_replays');
COMMIT;
