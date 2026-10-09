DO $$ BEGIN
 IF EXISTS(
  (SELECT relation,key,body FROM card_route_batch_upgrade_fixture
   EXCEPT SELECT 'cards',id::text,to_jsonb(c) FROM cards c
   EXCEPT SELECT 'card_routes',card_id::text,to_jsonb(r) FROM card_routes r)
  UNION ALL
  ((SELECT 'cards'::text AS relation,id::text AS key,to_jsonb(c) AS body FROM cards c
    UNION ALL SELECT 'card_routes',card_id::text,to_jsonb(r) FROM card_routes r)
   EXCEPT SELECT relation,key,body FROM card_route_batch_upgrade_fixture)
 ) THEN RAISE EXCEPTION 'Route batching migration rewrote populated history'; END IF;
 IF EXISTS(SELECT 1 FROM card_route_batch_guard_upgrade_fixture WHERE
  clock_guard<>pg_get_functiondef('maintain_entity_route_clocks()'::regprocedure)
  OR deletion_guard<>pg_get_functiondef('sync_card_route()'::regprocedure))
 THEN RAISE EXCEPTION 'Route batching replaced canonical clock or deletion guard'; END IF;
END $$;
DROP TABLE card_route_batch_upgrade_fixture,card_route_batch_guard_upgrade_fixture;
