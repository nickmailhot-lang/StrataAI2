CREATE TABLE card_route_clock_index_body_fixture AS
 SELECT 'cards'::text AS relation,id::text AS key,to_jsonb(c) AS body FROM cards c
 UNION ALL SELECT 'card_routes',card_id::text,to_jsonb(r) FROM card_routes r;
CREATE TABLE card_route_clock_index_guard_fixture AS
 SELECT prosrc,prosecdef,proacl,proconfig FROM pg_proc WHERE oid='public.maintain_entity_route_clocks()'::regprocedure;
