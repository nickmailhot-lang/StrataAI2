BEGIN;
-- Keep the canonical clock body, invoker security and RLS unchanged. Small
-- earlier writes must not leave a sequential plan cached for a large graph.
ALTER FUNCTION public.maintain_entity_route_clocks() SET enable_seqscan TO off;
INSERT INTO schema_migrations(version) VALUES('135_card_route_clock_index_plan');
COMMIT;
