BEGIN;
-- Shared actor admission and actor-history serialization avoid Work/account lock inversion.

CREATE OR REPLACE FUNCTION append_search_interaction(p_event uuid,p_actor uuid,p_type text,p_organization uuid,p_board uuid,p_created timestamptz)
 RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE visible text; organization_role text; original public.search_interaction_events%ROWTYPE; next_sequence bigint;
BEGIN
 IF p_event IS NULL OR p_event='00000000-0000-0000-0000-000000000000' OR p_actor IS NULL
   OR p_actor IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid
   OR p_type IS NULL OR p_type NOT IN ('SEARCH_EXECUTED','BOARD_FILTER_CHANGED')
   OR p_created IS NULL OR NOT isfinite(p_created) OR p_created<='0001-01-01'::timestamptz THEN
  RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001';
 END IF;
 IF p_organization IS NOT NULL THEN
  PERFORM 1 FROM public.organizations WHERE id=p_organization AND status='ACTIVE' FOR SHARE;
  IF NOT FOUND THEN RAISE EXCEPTION 'Search interaction unavailable' USING ERRCODE='P0001'; END IF;
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('strataai:interaction:' || p_actor::text,0));
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

CREATE OR REPLACE FUNCTION append_or_replay_board_filter_interaction(p_request uuid,p_fingerprint text,p_event uuid,
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
 IF p_organization IS NOT NULL THEN
  PERFORM 1 FROM public.organizations WHERE id=p_organization AND status='ACTIVE' FOR SHARE;
  IF NOT FOUND THEN RETURN; END IF;
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('strataai:interaction:' || p_actor::text,0));
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

CREATE OR REPLACE FUNCTION append_or_replay_navigation_interaction(p_request uuid,p_fingerprint text,p_event uuid,
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
 -- The interaction advisory gate serializes actor receipts independently
 -- of shared account admission, including direct restricted calls.
 IF p_organization IS NOT NULL THEN
  PERFORM 1 FROM public.organizations WHERE id=p_organization AND status='ACTIVE' FOR SHARE;
  IF NOT FOUND THEN RETURN; END IF;
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('strataai:interaction:' || p_actor::text,0));
 PERFORM 1 FROM public.users WHERE id=p_actor AND status='ACTIVE' FOR SHARE;
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

CREATE OR REPLACE FUNCTION append_navigation_interaction(p_event uuid,p_actor uuid,p_type text,p_organization uuid,p_board uuid,
 p_entity uuid,p_version bigint,p_created timestamptz) RETURNS boolean
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public SET app.tenant_id='' AS $$
DECLARE visible text; organization_role text; original public.navigation_interaction_events%ROWTYPE; target_type text; has_original boolean;
BEGIN
 IF p_event IS NULL OR p_event='00000000-0000-0000-0000-000000000000' OR p_actor IS NULL
  OR p_actor IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid
  OR p_entity IS NULL OR p_entity='00000000-0000-0000-0000-000000000000'
  OR p_version IS NULL OR p_version<1 OR p_created IS NULL OR NOT isfinite(p_created)
  OR p_created<='0001-01-01'::timestamptz OR p_type IS NULL
  OR p_type NOT IN ('APPLICATION_CONTEXT_CHANGED','BOARD_OPENED','CARD_OPENED') THEN RETURN false; END IF;
 IF p_organization IS NOT NULL THEN
  PERFORM 1 FROM public.organizations WHERE id=p_organization AND status='ACTIVE' FOR SHARE;
  IF NOT FOUND THEN RETURN false; END IF;
 END IF;
 PERFORM pg_advisory_xact_lock(hashtextextended('strataai:interaction:' || p_actor::text,0));
 PERFORM 1 FROM public.users WHERE id=p_actor AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RETURN false; END IF;
 SELECT * INTO original FROM public.navigation_interaction_events WHERE event_id=p_event;
 has_original:=FOUND;
 IF has_original AND ROW(original.actor_id,original.event_type,original.organization_id,original.board_id,original.entity_id,original.entity_version,original.created_at)
  IS DISTINCT FROM ROW(p_actor,p_type,p_organization,p_board,p_entity,p_version,p_created) THEN RETURN false; END IF;
 -- Function SET scopes this transaction-local setting and restores it on exit.
 PERFORM set_config('app.tenant_id',COALESCE(p_organization::text,''),true);
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
   PERFORM 1 FROM public.boards WHERE tenant_id=p_organization AND id=p_board AND (has_original OR version=p_version) FOR SHARE;
   IF NOT FOUND THEN RETURN false; END IF;
   target_type:='Board';
  ELSE
   PERFORM 1 FROM public.cards c JOIN public.board_lists l ON l.tenant_id=c.tenant_id AND l.id=c.list_id AND l.board_id=c.board_id
    WHERE c.tenant_id=p_organization AND c.board_id=p_board AND c.id=p_entity AND (has_original OR c.version=p_version)
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

INSERT INTO schema_migrations(version) VALUES('085_interaction_actor_lock_order');
COMMIT;
