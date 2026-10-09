BEGIN;
LOCK TABLE cards,card_routes IN ACCESS EXCLUSIVE MODE;
CREATE FUNCTION sync_inserted_card_routes() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 INSERT INTO public.card_routes(card_id,tenant_id,board_id,list_id,lifecycle_state,updated_at)
 SELECT id,tenant_id,board_id,list_id,lifecycle_state,updated_at FROM inserted_cards ORDER BY id
 ON CONFLICT(card_id) DO UPDATE SET tenant_id=EXCLUDED.tenant_id,board_id=EXCLUDED.board_id,
  list_id=EXCLUDED.list_id,lifecycle_state=EXCLUDED.lifecycle_state,updated_at=EXCLUDED.updated_at;
 RETURN NULL;
END $$;
CREATE FUNCTION sync_updated_card_routes() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 INSERT INTO public.card_routes(card_id,tenant_id,board_id,list_id,lifecycle_state,updated_at)
 SELECT id,tenant_id,board_id,list_id,lifecycle_state,updated_at FROM updated_cards ORDER BY id
 ON CONFLICT(card_id) DO UPDATE SET tenant_id=EXCLUDED.tenant_id,board_id=EXCLUDED.board_id,
  list_id=EXCLUDED.list_id,lifecycle_state=EXCLUDED.lifecycle_state,updated_at=EXCLUDED.updated_at;
 RETURN NULL;
END $$;
REVOKE ALL ON FUNCTION sync_inserted_card_routes(),sync_updated_card_routes() FROM PUBLIC;
DROP TRIGGER cards_sync_route ON cards;
CREATE TRIGGER cards_sync_route AFTER DELETE ON cards FOR EACH ROW EXECUTE FUNCTION sync_card_route();
CREATE TRIGGER cards_sync_inserted_routes AFTER INSERT ON cards REFERENCING NEW TABLE AS inserted_cards
 FOR EACH STATEMENT EXECUTE FUNCTION sync_inserted_card_routes();
CREATE TRIGGER cards_sync_updated_routes AFTER UPDATE ON cards REFERENCING NEW TABLE AS updated_cards
 FOR EACH STATEMENT EXECUTE FUNCTION sync_updated_card_routes();
INSERT INTO schema_migrations(version) VALUES('133_card_route_statement_projection');
COMMIT;
