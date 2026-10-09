BEGIN;
LOCK TABLE invitation_recipient_authority_pages IN ACCESS EXCLUSIVE MODE;
ALTER TABLE invitation_recipient_authority_pages ADD COLUMN created_at timestamptz,
 ADD COLUMN updated_at timestamptz;
-- A page and its owning durable job are published in the same transaction.
-- Preserve that recorded creation fact and the page's first completion fact.
DROP TRIGGER invitation_authority_page_history ON invitation_recipient_authority_pages;
UPDATE invitation_recipient_authority_pages p SET created_at=j.created_at,
 updated_at=coalesce(p.completed_at,j.created_at)
 FROM background_jobs j WHERE j.tenant_id=p.tenant_id AND j.id=p.job_id;
CREATE TRIGGER invitation_authority_page_history BEFORE UPDATE OR DELETE ON invitation_recipient_authority_pages
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();
ALTER TABLE invitation_recipient_authority_pages ALTER COLUMN created_at SET NOT NULL,
 ALTER COLUMN updated_at SET NOT NULL,
 ADD CONSTRAINT invitation_authority_page_clocks CHECK(isfinite(created_at) AND isfinite(updated_at)
  AND updated_at>=created_at AND updated_at=coalesce(completed_at,created_at));
CREATE FUNCTION maintain_invitation_authority_page_clocks() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$ BEGIN
 IF TG_OP='INSERT' THEN
  SELECT j.created_at INTO NEW.created_at FROM public.background_jobs j
   WHERE j.tenant_id=NEW.tenant_id AND j.id=NEW.job_id;
  NEW.updated_at:=coalesce(NEW.completed_at,NEW.created_at);
 ELSE
  IF NEW.created_at IS DISTINCT FROM OLD.created_at OR NEW.updated_at IS DISTINCT FROM OLD.updated_at THEN
   RAISE EXCEPTION 'Invitation authority page clocks belong to their recorded lifecycle' USING ERRCODE='23514';
  END IF;
  NEW.updated_at:=coalesce(NEW.completed_at,NEW.created_at);
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER invitation_authority_page_clock BEFORE INSERT OR UPDATE ON invitation_recipient_authority_pages
 FOR EACH ROW EXECUTE FUNCTION maintain_invitation_authority_page_clocks();
REVOKE ALL ON FUNCTION maintain_invitation_authority_page_clocks() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('116_invitation_authority_page_clocks');
COMMIT;
