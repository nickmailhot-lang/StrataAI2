BEGIN;
CREATE TABLE card_comments (
 id uuid PRIMARY KEY CHECK(id<>'00000000-0000-0000-0000-000000000000'),
 tenant_id uuid NOT NULL,card_id uuid NOT NULL,author_id uuid NOT NULL,
 content text,created_at timestamptz NOT NULL,updated_at timestamptz NOT NULL,
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),edited_at timestamptz,
 deleted_at timestamptz,deleted_by uuid,
 UNIQUE(id,tenant_id,card_id),
 FOREIGN KEY(card_id,tenant_id) REFERENCES cards(id,tenant_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,author_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT,
 CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at),
 CHECK(edited_at IS NULL OR (isfinite(edited_at) AND edited_at BETWEEN created_at AND updated_at)),
 CHECK((deleted_at IS NULL AND deleted_by IS NULL AND content IS NOT NULL
   AND length(content) BETWEEN 1 AND 10000 AND octet_length(content)<=40000
   AND content !~ '^[[:space:]]*$'
   AND regexp_replace(content,E'[\n\t]','','g') !~ '[[:cntrl:]]')
  OR (deleted_at IS NOT NULL AND isfinite(deleted_at) AND deleted_at BETWEEN created_at AND updated_at
   AND deleted_by IS NOT NULL AND deleted_by=author_id AND content IS NULL))
);
CREATE INDEX ix_card_comments_cursor ON card_comments(tenant_id,card_id,created_at DESC,id DESC);
ALTER TABLE card_comments ENABLE ROW LEVEL SECURITY;
ALTER TABLE card_comments FORCE ROW LEVEL SECURITY;
CREATE POLICY card_comments_tenant_isolation ON card_comments
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);

-- This enforces persisted shape/ownership, not session or Board permission.
-- The owning Application command must authorize and atomically publish effects.
CREATE FUNCTION enforce_card_comment_revision() RETURNS trigger LANGUAGE plpgsql
 SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='INSERT' THEN
  IF NEW.version<>1 OR NEW.updated_at<>NEW.created_at OR NEW.edited_at IS NOT NULL
   OR NEW.deleted_at IS NOT NULL OR NEW.deleted_by IS NOT NULL THEN
   RAISE EXCEPTION 'New comment history is invalid' USING ERRCODE='23514';
  END IF;
  RETURN NEW;
 END IF;
 IF NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
 IF OLD.deleted_at IS NOT NULL THEN
  RAISE EXCEPTION 'Deleted comment is immutable' USING ERRCODE='23514';
 END IF;
 IF NEW.id IS DISTINCT FROM OLD.id OR NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
  OR NEW.card_id IS DISTINCT FROM OLD.card_id OR NEW.author_id IS DISTINCT FROM OLD.author_id
  OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
  RAISE EXCEPTION 'Comment ownership is immutable' USING ERRCODE='23514';
 END IF;
 IF OLD.version=9223372036854775807 OR NEW.version<>OLD.version+1 OR NEW.updated_at<OLD.updated_at THEN
  RAISE EXCEPTION 'Comment revision is invalid' USING ERRCODE='23514';
 END IF;
 IF NEW.deleted_at IS NOT NULL AND NEW.deleted_at=NEW.updated_at AND NEW.deleted_by=OLD.author_id
  AND NEW.content IS NULL AND NEW.edited_at IS NOT DISTINCT FROM OLD.edited_at THEN RETURN NEW; END IF;
 IF NEW.deleted_at IS NULL AND NEW.deleted_by IS NULL AND NEW.content IS DISTINCT FROM OLD.content
  AND NEW.content IS NOT NULL AND NEW.edited_at=NEW.updated_at THEN RETURN NEW; END IF;
 RAISE EXCEPTION 'Comment transition is invalid' USING ERRCODE='23514';
END $$;
CREATE TRIGGER card_comments_revision_guard BEFORE INSERT OR UPDATE ON card_comments
 FOR EACH ROW EXECUTE FUNCTION enforce_card_comment_revision();
INSERT INTO schema_migrations(version) VALUES('055_card_comments');
COMMIT;
