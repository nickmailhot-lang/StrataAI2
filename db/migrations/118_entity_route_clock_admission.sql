BEGIN;
CREATE OR REPLACE FUNCTION maintain_entity_route_clocks() RETURNS trigger
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
  -- BEFORE triggers run before the route's RLS WITH CHECK. Preserve the
  -- existing authorization refusal when discovery has no owning tenant.
  IF row_security_active(TG_RELID)
   AND NULLIF(current_setting('app.tenant_id',true),'') IS DISTINCT FROM NEW.tenant_id::text THEN
   RAISE EXCEPTION 'Entity route write requires its owning tenant context' USING ERRCODE='42501';
  END IF;
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
REVOKE ALL ON FUNCTION maintain_entity_route_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('118_entity_route_clock_admission');
COMMIT;
