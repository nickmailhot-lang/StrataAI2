BEGIN;
CREATE TABLE navigation_interaction_events (
 event_id uuid PRIMARY KEY CHECK(event_id<>'00000000-0000-0000-0000-000000000000'),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 event_type text NOT NULL CHECK(event_type IN ('APPLICATION_CONTEXT_CHANGED','BOARD_OPENED','CARD_OPENED')),
 organization_id uuid REFERENCES organizations(id) ON DELETE RESTRICT,
 board_id uuid,
 entity_type text NOT NULL,
 entity_id uuid NOT NULL CHECK(entity_id<>'00000000-0000-0000-0000-000000000000'),
 entity_version bigint NOT NULL CHECK(entity_version>0),
 metadata jsonb NOT NULL DEFAULT '{}'::jsonb CHECK(metadata='{}'::jsonb),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at) AND created_at>'0001-01-01'::timestamptz),
 FOREIGN KEY(organization_id,board_id) REFERENCES boards(tenant_id,id) ON DELETE RESTRICT,
 CHECK((event_type='APPLICATION_CONTEXT_CHANGED' AND board_id IS NULL AND entity_version=1
        AND ((organization_id IS NULL AND entity_type='ApplicationContext' AND entity_id=event_id)
          OR (organization_id IS NOT NULL AND entity_type='Organization' AND entity_id=organization_id)))
    OR (event_type='BOARD_OPENED' AND organization_id IS NOT NULL AND board_id IS NOT NULL AND entity_type='Board' AND entity_id=board_id)
    OR (event_type='CARD_OPENED' AND organization_id IS NOT NULL AND board_id IS NOT NULL AND entity_type='Card'))
);
CREATE INDEX navigation_interaction_actor_time ON navigation_interaction_events(actor_id,created_at,event_id);
ALTER TABLE navigation_interaction_events ENABLE ROW LEVEL SECURITY;
ALTER TABLE navigation_interaction_events FORCE ROW LEVEL SECURITY;
CREATE POLICY navigation_interaction_subject ON navigation_interaction_events
 USING(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
 WITH CHECK(actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);
CREATE FUNCTION guard_navigation_interaction_history() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN RAISE EXCEPTION 'Navigation history is immutable' USING ERRCODE='23514'; END $$;
REVOKE ALL ON FUNCTION guard_navigation_interaction_history() FROM PUBLIC;
CREATE TRIGGER navigation_interaction_immutable BEFORE UPDATE OR DELETE ON navigation_interaction_events
 FOR EACH ROW EXECUTE FUNCTION guard_navigation_interaction_history();
CREATE FUNCTION append_navigation_interaction(p_event uuid,p_actor uuid,p_type text,p_organization uuid,p_board uuid,
 p_entity uuid,p_version bigint,p_created timestamptz) RETURNS boolean
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE visible text; organization_role text; original public.navigation_interaction_events%ROWTYPE; target_type text;
BEGIN
 IF p_event IS NULL OR p_event='00000000-0000-0000-0000-000000000000' OR p_actor IS NULL
  OR p_actor IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid
  OR p_entity IS NULL OR p_entity='00000000-0000-0000-0000-000000000000'
  OR p_version IS NULL OR p_version<1 OR p_created IS NULL OR NOT isfinite(p_created)
  OR p_created<='0001-01-01'::timestamptz OR p_type IS NULL
  OR p_type NOT IN ('APPLICATION_CONTEXT_CHANGED','BOARD_OPENED','CARD_OPENED') THEN RETURN false; END IF;
 PERFORM 1 FROM public.users WHERE id=p_actor AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RETURN false; END IF;
 IF p_type='APPLICATION_CONTEXT_CHANGED' THEN
  IF p_board IS NOT NULL OR p_version<>1 THEN RETURN false; END IF;
  IF p_organization IS NULL THEN
   IF p_entity<>p_event THEN RETURN false; END IF;
   target_type:='ApplicationContext';
  ELSE
   IF p_entity<>p_organization THEN RETURN false; END IF;
   PERFORM 1 FROM public.organizations o JOIN public.organization_members m ON m.tenant_id=o.id
    WHERE o.id=p_organization AND o.status='ACTIVE' AND m.user_id=p_actor AND m.status='ACTIVE' FOR SHARE OF o,m;
   IF NOT FOUND THEN RETURN false; END IF;
   target_type:='Organization';
  END IF;
 ELSE
  IF p_organization IS NULL OR p_board IS NULL THEN RETURN false; END IF;
  SELECT b.visibility INTO visible FROM public.organizations o
   JOIN public.boards b ON b.tenant_id=o.id
   WHERE o.id=p_organization AND o.status='ACTIVE' AND b.id=p_board AND b.lifecycle_state='ACTIVE'
   FOR SHARE OF o,b;
  IF NOT FOUND THEN RETURN false; END IF;
  SELECT m.role INTO organization_role FROM public.organization_members m
   WHERE m.tenant_id=p_organization AND m.user_id=p_actor AND m.status='ACTIVE' FOR SHARE;
  IF visible<>'PUBLIC' AND NOT FOUND THEN
   RETURN false; END IF;
  IF visible='PRIVATE' AND COALESCE(organization_role,'') NOT IN ('OWNER','ADMIN') THEN
   PERFORM 1 FROM public.board_members m WHERE m.tenant_id=p_organization AND m.board_id=p_board
    AND m.user_id=p_actor AND m.status='ACTIVE' FOR SHARE;
   IF NOT FOUND THEN RETURN false; END IF;
  END IF;
  IF p_type='BOARD_OPENED' THEN
   IF p_entity<>p_board THEN RETURN false; END IF;
   PERFORM 1 FROM public.boards WHERE tenant_id=p_organization AND id=p_board AND version=p_version FOR SHARE;
   IF NOT FOUND THEN RETURN false; END IF;
   target_type:='Board';
  ELSE
   PERFORM 1 FROM public.cards c JOIN public.board_lists l ON l.tenant_id=c.tenant_id AND l.id=c.list_id AND l.board_id=c.board_id
    WHERE c.tenant_id=p_organization AND c.board_id=p_board AND c.id=p_entity AND c.version=p_version
     AND c.lifecycle_state='ACTIVE' AND l.lifecycle_state='ACTIVE' FOR SHARE OF c,l;
   IF NOT FOUND THEN RETURN false; END IF;
   target_type:='Card';
  END IF;
 END IF;
 SELECT * INTO original FROM public.navigation_interaction_events WHERE event_id=p_event;
 IF FOUND THEN
  RETURN ROW(original.actor_id,original.event_type,original.organization_id,original.board_id,original.entity_id,original.entity_version,original.created_at)
   IS NOT DISTINCT FROM ROW(p_actor,p_type,p_organization,p_board,p_entity,p_version,p_created);
 END IF;
 INSERT INTO public.navigation_interaction_events(event_id,actor_id,event_type,organization_id,board_id,entity_type,entity_id,entity_version,created_at)
 VALUES(p_event,p_actor,p_type,p_organization,p_board,target_type,p_entity,p_version,p_created);
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION append_navigation_interaction(uuid,uuid,text,uuid,uuid,uuid,bigint,timestamptz) FROM PUBLIC;
DO $$ BEGIN IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
 GRANT SELECT ON navigation_interaction_events TO strataai_api_runtime;
 GRANT EXECUTE ON FUNCTION append_navigation_interaction(uuid,uuid,text,uuid,uuid,uuid,bigint,timestamptz) TO strataai_api_runtime;
END IF; END $$;
INSERT INTO schema_migrations(version) VALUES('078_navigation_interaction_sources');
COMMIT;
