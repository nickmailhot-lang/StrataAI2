BEGIN;
-- Routes project canonical entity history; migration time is not a historical
-- creation or mutation fact. Serialize the source and projection backfill.
LOCK TABLE boards,board_lists,cards,board_labels IN ACCESS EXCLUSIVE MODE;
LOCK TABLE board_routes,list_routes,card_routes,label_routes IN ACCESS EXCLUSIVE MODE;
ALTER TABLE board_routes ADD COLUMN created_at timestamptz;
ALTER TABLE list_routes ADD COLUMN created_at timestamptz;
ALTER TABLE card_routes ADD COLUMN created_at timestamptz;
ALTER TABLE label_routes ADD COLUMN created_at timestamptz, ADD COLUMN updated_at timestamptz;
UPDATE board_routes r SET created_at=s.created_at,updated_at=s.updated_at
 FROM boards s WHERE s.id=r.board_id AND s.tenant_id=r.tenant_id;
UPDATE list_routes r SET created_at=s.created_at,updated_at=s.updated_at
 FROM board_lists s WHERE s.id=r.list_id AND s.tenant_id=r.tenant_id AND s.board_id=r.board_id;
UPDATE card_routes r SET created_at=s.created_at,updated_at=s.updated_at
 FROM cards s WHERE s.id=r.card_id AND s.tenant_id=r.tenant_id AND s.board_id=r.board_id AND s.list_id=r.list_id;
UPDATE label_routes r SET created_at=s.created_at,updated_at=s.updated_at
 FROM board_labels s WHERE s.id=r.label_id AND s.tenant_id=r.tenant_id AND s.board_id=r.board_id;
-- Missing/mismatched historical sources refuse the entire upgrade atomically.
ALTER TABLE board_routes ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE list_routes ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE card_routes ALTER COLUMN created_at SET NOT NULL;
ALTER TABLE label_routes ALTER COLUMN created_at SET NOT NULL, ALTER COLUMN updated_at SET NOT NULL;
CREATE FUNCTION maintain_entity_route_clocks() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE source_created timestamptz; source_updated timestamptz;
BEGIN
 IF TG_TABLE_NAME='board_routes' THEN
  SELECT s.created_at,s.updated_at INTO source_created,source_updated FROM public.boards s
   WHERE s.id=NEW.board_id AND s.tenant_id=NEW.tenant_id;
 ELSIF TG_TABLE_NAME='list_routes' THEN
  SELECT s.created_at,s.updated_at INTO source_created,source_updated FROM public.board_lists s
   WHERE s.id=NEW.list_id AND s.tenant_id=NEW.tenant_id AND s.board_id=NEW.board_id;
 ELSIF TG_TABLE_NAME='card_routes' THEN
  SELECT s.created_at,s.updated_at INTO source_created,source_updated FROM public.cards s
   WHERE s.id=NEW.card_id AND s.tenant_id=NEW.tenant_id AND s.board_id=NEW.board_id AND s.list_id=NEW.list_id;
 ELSIF TG_TABLE_NAME='label_routes' THEN
  SELECT s.created_at,s.updated_at INTO source_created,source_updated FROM public.board_labels s
   WHERE s.id=NEW.label_id AND s.tenant_id=NEW.tenant_id AND s.board_id=NEW.board_id;
 ELSE RAISE EXCEPTION 'Unsupported entity route clock owner' USING ERRCODE='23514';
 END IF;
 IF source_created IS NULL OR source_updated IS NULL THEN
  RAISE EXCEPTION 'Entity route clock source is unavailable' USING ERRCODE='23514';
 END IF;
 IF TG_OP='UPDATE' THEN
  IF NEW.created_at IS DISTINCT FROM OLD.created_at
   OR NEW.updated_at IS DISTINCT FROM OLD.updated_at AND NEW.updated_at IS DISTINCT FROM source_updated THEN
   RAISE EXCEPTION 'Entity route clocks belong to canonical history' USING ERRCODE='23514';
  END IF;
 ELSE
  IF NEW.created_at IS NOT NULL AND NEW.created_at IS DISTINCT FROM source_created
   OR NEW.updated_at IS NOT NULL AND NEW.updated_at IS DISTINCT FROM source_updated THEN
   RAISE EXCEPTION 'Entity route clocks belong to canonical history' USING ERRCODE='23514';
  END IF;
 END IF;
 NEW.created_at:=source_created; NEW.updated_at:=source_updated;
 RETURN NEW;
END $$;
CREATE TRIGGER board_route_clock BEFORE INSERT OR UPDATE ON board_routes
 FOR EACH ROW EXECUTE FUNCTION maintain_entity_route_clocks();
CREATE TRIGGER list_route_clock BEFORE INSERT OR UPDATE ON list_routes
 FOR EACH ROW EXECUTE FUNCTION maintain_entity_route_clocks();
CREATE TRIGGER card_route_clock BEFORE INSERT OR UPDATE ON card_routes
 FOR EACH ROW EXECUTE FUNCTION maintain_entity_route_clocks();
CREATE TRIGGER label_route_clock BEFORE INSERT OR UPDATE ON label_routes
 FOR EACH ROW EXECUTE FUNCTION maintain_entity_route_clocks();
REVOKE ALL ON FUNCTION maintain_entity_route_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('117_entity_route_clocks');
COMMIT;
