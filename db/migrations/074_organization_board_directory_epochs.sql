BEGIN;
-- Fence grant writers through backfill and trigger installation. This is
-- admission metadata, not a fabricated Board event or audit history.
LOCK TABLE organization_members,board_members IN SHARE ROW EXCLUSIVE MODE;
CREATE TABLE organization_board_directory_epochs (
 tenant_id uuid NOT NULL, user_id uuid NOT NULL,
 generation uuid NOT NULL DEFAULT gen_random_uuid(),
 permission_revision bigint NOT NULL DEFAULT 1 CHECK(permission_revision>0),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 PRIMARY KEY(tenant_id,user_id),
 FOREIGN KEY(tenant_id,user_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE CASCADE,
 CHECK(updated_at>=created_at)
);
ALTER TABLE organization_board_directory_epochs ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_board_directory_epochs FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_board_directory_epochs_tenant_isolation ON organization_board_directory_epochs
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
INSERT INTO organization_board_directory_epochs(tenant_id,user_id)
 SELECT tenant_id,user_id FROM organization_members;

CREATE FUNCTION revise_organization_directory_membership() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  INSERT INTO public.organization_board_directory_epochs(tenant_id,user_id) VALUES(NEW.tenant_id,NEW.user_id);
 ELSIF NEW.role IS DISTINCT FROM OLD.role OR NEW.status IS DISTINCT FROM OLD.status THEN
  UPDATE public.organization_board_directory_epochs SET permission_revision=permission_revision+1,
   updated_at=GREATEST(updated_at,clock_timestamp()) WHERE tenant_id=NEW.tenant_id AND user_id=NEW.user_id;
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION revise_organization_directory_membership() FROM PUBLIC;
CREATE TRIGGER organization_directory_membership_revision AFTER INSERT OR UPDATE OF role,status ON organization_members
 FOR EACH ROW EXECUTE FUNCTION revise_organization_directory_membership();

CREATE FUNCTION revise_board_directory_membership() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 -- Only the affected subject's actual administration transition invalidates
 -- their cursor. Nonadministrative changes do not expose other grant activity.
 IF TG_OP='UPDATE' AND ROW(NEW.tenant_id,NEW.board_id,NEW.user_id,NEW.role,NEW.status)
   IS NOT DISTINCT FROM ROW(OLD.tenant_id,OLD.board_id,OLD.user_id,OLD.role,OLD.status) THEN RETURN NEW; END IF;
 IF TG_OP<>'INSERT' AND OLD.role='ADMIN' AND OLD.status='ACTIVE' THEN
  UPDATE public.organization_board_directory_epochs SET permission_revision=permission_revision+1,
   updated_at=GREATEST(updated_at,clock_timestamp()) WHERE tenant_id=OLD.tenant_id AND user_id=OLD.user_id;
 END IF;
 IF TG_OP<>'DELETE' AND NEW.role='ADMIN' AND NEW.status='ACTIVE' THEN
  UPDATE public.organization_board_directory_epochs SET permission_revision=permission_revision+1,
   updated_at=GREATEST(updated_at,clock_timestamp()) WHERE tenant_id=NEW.tenant_id AND user_id=NEW.user_id;
 END IF;
 IF TG_OP='DELETE' THEN RETURN OLD; END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION revise_board_directory_membership() FROM PUBLIC;
CREATE TRIGGER board_directory_membership_revision AFTER INSERT OR UPDATE OF tenant_id,board_id,user_id,role,status OR DELETE ON board_members
 FOR EACH ROW EXECUTE FUNCTION revise_board_directory_membership();
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN
  GRANT SELECT ON organization_board_directory_epochs TO strataai_api_runtime;
  REVOKE INSERT,UPDATE,DELETE ON organization_board_directory_epochs FROM strataai_api_runtime;
 END IF;
END $$;
INSERT INTO schema_migrations(version) VALUES('074_organization_board_directory_epochs');
COMMIT;
