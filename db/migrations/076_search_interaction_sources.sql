BEGIN;
-- Personal interaction history is actor-owned, not shared Board Work history.
CREATE TABLE search_interaction_streams (
 actor_id uuid PRIMARY KEY REFERENCES users(id) ON DELETE RESTRICT,
 last_sequence bigint NOT NULL DEFAULT 0 CHECK(last_sequence>=0),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp() CHECK(updated_at>=created_at)
);
CREATE TABLE search_interaction_events (
 event_id uuid PRIMARY KEY CHECK(event_id<>'00000000-0000-0000-0000-000000000000'),
 actor_id uuid NOT NULL REFERENCES search_interaction_streams(actor_id) ON DELETE RESTRICT,
 sequence bigint NOT NULL CHECK(sequence>0),
 event_type text NOT NULL CHECK(event_type IN ('SEARCH_EXECUTED','BOARD_FILTER_CHANGED')),
 organization_id uuid REFERENCES organizations(id) ON DELETE RESTRICT,
 board_id uuid,
 entity_type text NOT NULL,
 entity_id uuid NOT NULL CHECK(entity_id=event_id),
 entity_version bigint NOT NULL DEFAULT 1 CHECK(entity_version=1),
 metadata jsonb NOT NULL DEFAULT '{}'::jsonb CHECK(metadata='{}'::jsonb),
 created_at timestamptz NOT NULL CHECK(created_at>'0001-01-01'::timestamptz),
 UNIQUE(actor_id,sequence),
 FOREIGN KEY(organization_id,board_id) REFERENCES boards(tenant_id,id) ON DELETE RESTRICT,
 CHECK((event_type='SEARCH_EXECUTED' AND entity_type='Search' AND organization_id IS NULL AND board_id IS NULL)
    OR (event_type='BOARD_FILTER_CHANGED' AND entity_type='BoardFilter' AND organization_id IS NOT NULL AND board_id IS NOT NULL))
);
ALTER TABLE search_interaction_streams ENABLE ROW LEVEL SECURITY;
ALTER TABLE search_interaction_streams FORCE ROW LEVEL SECURITY;
ALTER TABLE search_interaction_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE search_interaction_events FORCE ROW LEVEL SECURITY;
CREATE POLICY search_stream_subject ON search_interaction_streams
 USING(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
 WITH CHECK(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE POLICY search_event_subject ON search_interaction_events
 USING(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
 WITH CHECK(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE FUNCTION guard_search_interaction_history() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN RAISE EXCEPTION 'Search interaction history is immutable' USING ERRCODE='23514'; END $$;
REVOKE ALL ON FUNCTION guard_search_interaction_history() FROM PUBLIC;
CREATE TRIGGER search_interaction_immutable BEFORE UPDATE OR DELETE ON search_interaction_events
 FOR EACH ROW EXECUTE FUNCTION guard_search_interaction_history();

CREATE FUNCTION append_search_interaction(p_event uuid,p_actor uuid,p_type text,p_organization uuid,p_board uuid,p_created timestamptz)
 RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE visible text; organization_role text; original public.search_interaction_events%ROWTYPE; next_sequence bigint;
BEGIN
 IF p_event IS NULL OR p_event='00000000-0000-0000-0000-000000000000' OR p_actor IS NULL
   OR p_actor IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid
   OR p_type IS NULL OR p_type NOT IN ('SEARCH_EXECUTED','BOARD_FILTER_CHANGED')
   OR p_created IS NULL OR NOT isfinite(p_created) OR p_created<='0001-01-01'::timestamptz THEN
  RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001';
 END IF;
 PERFORM 1 FROM public.users WHERE id=p_actor AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
 IF p_type='SEARCH_EXECUTED' THEN
  IF p_organization IS NOT NULL OR p_board IS NOT NULL THEN
   RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
 ELSE
  IF p_organization IS NULL OR p_board IS NULL THEN
   RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
  SELECT b.visibility INTO visible FROM public.organizations o
   JOIN public.boards b ON b.tenant_id=o.id
   WHERE o.id=p_organization AND o.status='ACTIVE' AND b.id=p_board AND b.lifecycle_state='ACTIVE'
   FOR SHARE OF o,b;
  IF NOT FOUND THEN RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
  SELECT m.role INTO organization_role FROM public.organization_members m
   WHERE m.tenant_id=p_organization AND m.user_id=p_actor AND m.status='ACTIVE' FOR SHARE;
  IF visible<>'PUBLIC' AND NOT FOUND THEN
   RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
  IF visible='PRIVATE' AND organization_role NOT IN ('OWNER','ADMIN') THEN
   PERFORM 1 FROM public.board_members m WHERE m.tenant_id=p_organization AND m.board_id=p_board
    AND m.user_id=p_actor AND m.status='ACTIVE' FOR SHARE;
   IF NOT FOUND THEN RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
  END IF;
 END IF;
 INSERT INTO public.search_interaction_streams(actor_id) VALUES(p_actor) ON CONFLICT DO NOTHING;
 PERFORM 1 FROM public.search_interaction_streams WHERE actor_id=p_actor FOR UPDATE;
 SELECT * INTO original FROM public.search_interaction_events WHERE event_id=p_event;
 IF FOUND THEN
  IF ROW(original.actor_id,original.event_type,original.organization_id,original.board_id,original.created_at)
     IS DISTINCT FROM ROW(p_actor,p_type,p_organization,p_board,p_created) THEN
   RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
  RETURN true;
 END IF;
 UPDATE public.search_interaction_streams SET last_sequence=last_sequence+1,
  updated_at=GREATEST(updated_at,clock_timestamp()) WHERE actor_id=p_actor RETURNING last_sequence INTO next_sequence;
 INSERT INTO public.search_interaction_events(event_id,actor_id,sequence,event_type,organization_id,board_id,entity_type,entity_id,created_at)
 VALUES(p_event,p_actor,next_sequence,p_type,p_organization,p_board,
  CASE p_type WHEN 'SEARCH_EXECUTED' THEN 'Search' ELSE 'BoardFilter' END,p_event,p_created);
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION append_search_interaction(uuid,uuid,text,uuid,uuid,timestamptz) FROM PUBLIC;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT ON search_interaction_streams,search_interaction_events TO strataai_api_runtime;
  GRANT EXECUTE ON FUNCTION append_search_interaction(uuid,uuid,text,uuid,uuid,timestamptz) TO strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('076_search_interaction_sources');
COMMIT;
