DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM card_route_clock_index_body_fixture) THEN
  RAISE EXCEPTION 'Clock planner upgrade lacks populated history'; END IF;
 IF EXISTS(
  ((SELECT relation,key,body FROM card_route_clock_index_body_fixture)
   EXCEPT (SELECT 'cards',id::text,to_jsonb(c) FROM cards c UNION ALL SELECT 'card_routes',card_id::text,to_jsonb(r) FROM card_routes r))
  UNION ALL
  ((SELECT 'cards',id::text,to_jsonb(c) FROM cards c UNION ALL SELECT 'card_routes',card_id::text,to_jsonb(r) FROM card_routes r)
   EXCEPT SELECT relation,key,body FROM card_route_clock_index_body_fixture)
 ) THEN RAISE EXCEPTION 'Clock planner upgrade rewrote canonical history'; END IF;
 IF EXISTS(SELECT 1 FROM pg_proc p CROSS JOIN card_route_clock_index_guard_fixture f
  WHERE p.oid='public.maintain_entity_route_clocks()'::regprocedure AND
   (p.prosrc IS DISTINCT FROM f.prosrc OR p.prosecdef IS DISTINCT FROM f.prosecdef
    OR p.proacl IS DISTINCT FROM f.proacl OR p.prosecdef
    OR NOT COALESCE(p.proconfig @> ARRAY['enable_seqscan=off'],false)
    OR array_remove(p.proconfig,'enable_seqscan=off') IS DISTINCT FROM f.proconfig))
 THEN RAISE EXCEPTION 'Clock planner upgrade changed guard body, security or unrelated settings'; END IF;
 IF (SELECT count(*) FROM pg_class WHERE oid IN ('cards'::regclass,'card_routes'::regclass)
   AND relrowsecurity AND relforcerowsecurity)<>2 THEN RAISE EXCEPTION 'Clock planner upgrade changed forced RLS'; END IF;
END $$;
DROP TABLE card_route_clock_index_body_fixture,card_route_clock_index_guard_fixture;
-- Reuse the current populated projections for the original clock refusal
-- checks; the pre-117 fixture deliberately seeds legacy orphan routes.
CREATE TABLE entity_route_clock_upgrade_135_fixture AS
 SELECT 'board_routes'::text AS route_kind,board_id AS row_key,to_jsonb(r)-'created_at'-'updated_at' AS body FROM board_routes r
 UNION ALL SELECT 'list_routes',list_id,to_jsonb(r)-'created_at'-'updated_at' FROM list_routes r
 UNION ALL SELECT 'card_routes',card_id,to_jsonb(r)-'created_at'-'updated_at' FROM card_routes r
 UNION ALL SELECT 'label_routes',label_id,to_jsonb(r)-'created_at'-'updated_at' FROM label_routes r;
