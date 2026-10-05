BEGIN;
-- Reader discovery has a wider audience than archive administration. Keep its
-- admission revision separate so a personal nonadmin grant cannot manufacture
-- archive invalidations for other users. No source/audit event is synthesized.
LOCK TABLE organization_members,board_members,boards IN SHARE ROW EXCLUSIVE MODE;
ALTER TABLE organization_board_directory_epochs
 ADD COLUMN reader_revision bigint NOT NULL DEFAULT 1 CHECK(reader_revision>0),
 ADD COLUMN reader_updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 ADD CONSTRAINT directory_reader_clock CHECK(reader_updated_at>=created_at);

CREATE FUNCTION revise_board_reader_membership() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='UPDATE' AND ROW(NEW.tenant_id,NEW.board_id,NEW.user_id,NEW.status)
   IS NOT DISTINCT FROM ROW(OLD.tenant_id,OLD.board_id,OLD.user_id,OLD.status) THEN RETURN NEW; END IF;
 IF TG_OP<>'INSERT' AND OLD.status='ACTIVE' THEN
  UPDATE public.organization_board_directory_epochs SET reader_revision=reader_revision+1,
   reader_updated_at=GREATEST(reader_updated_at,clock_timestamp()) WHERE tenant_id=OLD.tenant_id AND user_id=OLD.user_id;
 END IF;
 IF TG_OP<>'DELETE' AND NEW.status='ACTIVE' THEN
  UPDATE public.organization_board_directory_epochs SET reader_revision=reader_revision+1,
   reader_updated_at=GREATEST(reader_updated_at,clock_timestamp()) WHERE tenant_id=NEW.tenant_id AND user_id=NEW.user_id;
 END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION revise_board_reader_membership() FROM PUBLIC;
CREATE TRIGGER board_reader_membership_revision AFTER INSERT OR UPDATE OF tenant_id,board_id,user_id,status OR DELETE ON board_members
 FOR EACH ROW EXECUTE FUNCTION revise_board_reader_membership();

CREATE FUNCTION revise_board_reader_visibility() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF NEW.visibility IS NOT DISTINCT FROM OLD.visibility THEN RETURN NEW; END IF;
 -- Every active ordinary Organization member may gain or lose discovery of
 -- this Board. Owners/Admins already see every Board. Content edits and no-op
 -- visibility writes do not reveal activity on an inaccessible private Board.
 UPDATE public.organization_board_directory_epochs e SET reader_revision=e.reader_revision+1,
  reader_updated_at=GREATEST(e.reader_updated_at,clock_timestamp())
 FROM public.organization_members m WHERE e.tenant_id=NEW.tenant_id
  AND m.tenant_id=e.tenant_id AND m.user_id=e.user_id AND m.status='ACTIVE' AND m.role NOT IN ('OWNER','ADMIN');
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION revise_board_reader_visibility() FROM PUBLIC;
CREATE TRIGGER board_reader_visibility_revision AFTER UPDATE OF visibility ON boards
 FOR EACH ROW EXECUTE FUNCTION revise_board_reader_visibility();
INSERT INTO schema_migrations(version) VALUES('075_organization_board_reader_epochs');
COMMIT;
