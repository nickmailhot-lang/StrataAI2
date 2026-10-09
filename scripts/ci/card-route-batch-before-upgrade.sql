-- Retain exact populated canonical and route bodies before replacing synchronization hooks.
CREATE TABLE card_route_batch_upgrade_fixture AS
 SELECT 'cards'::text AS relation,id::text AS key,to_jsonb(c) AS body FROM cards c
 UNION ALL SELECT 'card_routes',card_id::text,to_jsonb(r) FROM card_routes r;
CREATE TABLE card_route_batch_guard_upgrade_fixture AS
 SELECT pg_get_functiondef('maintain_entity_route_clocks()'::regprocedure) AS clock_guard,
 pg_get_functiondef('sync_card_route()'::regprocedure) AS deletion_guard;
