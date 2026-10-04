BEGIN;
ALTER TABLE attachment_preview_publications ADD CONSTRAINT uq_preview_publications_id_tenant UNIQUE(id,tenant_id);
CREATE TABLE board_background_images (
 id uuid PRIMARY KEY CHECK(id<>'00000000-0000-0000-0000-000000000000'),
 tenant_id uuid NOT NULL, board_id uuid NOT NULL, preview_id uuid NOT NULL,
 created_by uuid NOT NULL, created_at timestamptz NOT NULL, source_image_id uuid,
 UNIQUE(id,tenant_id), UNIQUE(id,tenant_id,board_id),
 CHECK(source_image_id IS NULL OR source_image_id<>id),
 FOREIGN KEY(board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,created_by) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT,
 FOREIGN KEY(preview_id,tenant_id) REFERENCES attachment_preview_publications(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(source_image_id,tenant_id) REFERENCES board_background_images(id,tenant_id) ON DELETE RESTRICT
);
CREATE INDEX board_background_image_preview_owners ON board_background_images(tenant_id,preview_id);
COMMENT ON TABLE board_background_images IS 'Immutable private PNG owners. Provider garbage collection must retain every referenced preview; attachment lifecycle does not revoke independent Board ownership.';
ALTER TABLE board_background_images ENABLE ROW LEVEL SECURITY;
ALTER TABLE board_background_images FORCE ROW LEVEL SECURITY;
CREATE POLICY board_background_images_tenant_isolation ON board_background_images
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION enforce_board_background_image_owner() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Board image ownership is immutable' USING ERRCODE='23514'; END IF;
 IF NEW.source_image_id IS NULL THEN
  IF NOT EXISTS(SELECT 1 FROM public.attachment_preview_publications p WHERE p.id=NEW.preview_id AND p.tenant_id=NEW.tenant_id
   AND p.board_id=NEW.board_id AND NEW.created_at>=p.published_at) THEN
   RAISE EXCEPTION 'Board image publication scope is invalid' USING ERRCODE='23514';
  END IF;
 ELSE
  IF NOT EXISTS(SELECT 1 FROM public.board_background_images s WHERE s.id=NEW.source_image_id AND s.tenant_id=NEW.tenant_id
   AND s.board_id<>NEW.board_id AND s.preview_id=NEW.preview_id AND NEW.created_at>=s.created_at) THEN
   RAISE EXCEPTION 'Copied Board image scope is invalid' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER board_background_image_owner_guard BEFORE INSERT OR UPDATE OR DELETE ON board_background_images
 FOR EACH ROW EXECUTE FUNCTION enforce_board_background_image_owner();
ALTER TABLE boards ADD COLUMN background_image_id uuid,
 ADD CONSTRAINT board_background_image_owned_scope FOREIGN KEY(background_image_id,tenant_id,id)
  REFERENCES board_background_images(id,tenant_id,board_id) ON DELETE RESTRICT;
CREATE FUNCTION enforce_board_background_selection() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF NEW.background_type='COLOR' THEN NEW.background_image_id=NULL; RETURN NEW; END IF;
 -- Preserve historical typed values only when no image selection changed.
 IF TG_OP='UPDATE' AND OLD.background_image_id IS NULL AND NEW.background_image_id IS NULL
  AND ROW(NEW.background_type,NEW.background_value) IS NOT DISTINCT FROM ROW(OLD.background_type,OLD.background_value) THEN RETURN NEW; END IF;
 IF NEW.background_value IS NULL OR NEW.background_value !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
  OR NEW.background_value='00000000-0000-0000-0000-000000000000' THEN
  RAISE EXCEPTION 'Board background reference is invalid' USING ERRCODE='23514';
 END IF;
 NEW.background_image_id=NEW.background_value::uuid;
 RETURN NEW;
END $$;
CREATE TRIGGER board_background_selection_guard BEFORE INSERT OR UPDATE ON boards
 FOR EACH ROW EXECUTE FUNCTION enforce_board_background_selection();
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT,INSERT ON board_background_images TO strataai_api_runtime;
  REVOKE UPDATE,DELETE ON board_background_images FROM strataai_api_runtime;
 END IF;
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN
  REVOKE ALL ON board_background_images FROM strataai_worker_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('072_board_background_images');
COMMIT;
