BEGIN;
-- Body-free, immutable identity snapshots for consecutive comment revisions.
-- Caller/current-recipient admission belongs to the owning Application command.
CREATE TABLE comment_mention_snapshots (
 tenant_id uuid NOT NULL CHECK(tenant_id<>'00000000-0000-0000-0000-000000000000'),
 comment_id uuid NOT NULL CHECK(comment_id<>'00000000-0000-0000-0000-000000000000'),
 card_id uuid NOT NULL CHECK(card_id<>'00000000-0000-0000-0000-000000000000'),
 comment_version bigint NOT NULL CHECK(comment_version>0),
 recipient_count integer NOT NULL CHECK(recipient_count BETWEEN 0 AND 20),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 PRIMARY KEY(tenant_id,comment_id,comment_version),
 UNIQUE(tenant_id,comment_id,comment_version,card_id),
 FOREIGN KEY(comment_id,tenant_id,card_id) REFERENCES card_comments(id,tenant_id,card_id) ON DELETE RESTRICT
);
CREATE TABLE comment_mention_recipients (
 tenant_id uuid NOT NULL,comment_id uuid NOT NULL,card_id uuid NOT NULL,
 comment_version bigint NOT NULL,recipient_id uuid NOT NULL CHECK(recipient_id<>'00000000-0000-0000-0000-000000000000'),
 PRIMARY KEY(tenant_id,comment_id,comment_version,recipient_id),
 FOREIGN KEY(tenant_id,comment_id,comment_version,card_id)
  REFERENCES comment_mention_snapshots(tenant_id,comment_id,comment_version,card_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,recipient_id) REFERENCES organization_members(tenant_id,user_id) ON DELETE RESTRICT
);
ALTER TABLE comment_mention_snapshots ENABLE ROW LEVEL SECURITY;
ALTER TABLE comment_mention_snapshots FORCE ROW LEVEL SECURITY;
CREATE POLICY comment_mention_snapshots_tenant ON comment_mention_snapshots
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
ALTER TABLE comment_mention_recipients ENABLE ROW LEVEL SECURITY;
ALTER TABLE comment_mention_recipients FORCE ROW LEVEL SECURITY;
CREATE POLICY comment_mention_recipients_tenant ON comment_mention_recipients
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION enforce_comment_mention_snapshot() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE current_comment public.card_comments%ROWTYPE;
BEGIN
 IF TG_OP='UPDATE' THEN
  IF NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'Mention snapshot is immutable' USING ERRCODE='23514'; END IF;
  RETURN NEW;
 END IF;
 SELECT * INTO current_comment FROM public.card_comments
  WHERE tenant_id=NEW.tenant_id AND card_id=NEW.card_id AND id=NEW.comment_id FOR SHARE;
 IF NOT FOUND OR current_comment.version<>NEW.comment_version OR current_comment.updated_at<>NEW.created_at
  OR (current_comment.deleted_at IS NOT NULL AND NEW.recipient_count<>0) THEN
  RAISE EXCEPTION 'Current comment revision is unavailable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER comment_mention_snapshot_revision BEFORE INSERT OR UPDATE ON comment_mention_snapshots
 FOR EACH ROW EXECUTE FUNCTION enforce_comment_mention_snapshot();
CREATE FUNCTION enforce_comment_mention_recipient() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $$
BEGIN
 IF NEW IS DISTINCT FROM OLD THEN RAISE EXCEPTION 'Mention recipient is immutable' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER comment_mention_recipient_immutable BEFORE UPDATE ON comment_mention_recipients
 FOR EACH ROW EXECUTE FUNCTION enforce_comment_mention_recipient();
CREATE FUNCTION enforce_comment_mention_cardinality() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
DECLARE expected integer; actual bigint; own_tenant uuid; own_comment uuid; own_version bigint;
BEGIN
 IF TG_OP='DELETE' THEN own_tenant=OLD.tenant_id; own_comment=OLD.comment_id; own_version=OLD.comment_version;
 ELSE own_tenant=NEW.tenant_id; own_comment=NEW.comment_id; own_version=NEW.comment_version; END IF;
 SELECT recipient_count INTO expected FROM public.comment_mention_snapshots
  WHERE tenant_id=own_tenant AND comment_id=own_comment AND comment_version=own_version;
 IF NOT FOUND THEN RETURN NULL; END IF;
 SELECT count(*) INTO actual FROM public.comment_mention_recipients
  WHERE tenant_id=own_tenant AND comment_id=own_comment AND comment_version=own_version;
 IF expected<>actual THEN RAISE EXCEPTION 'Mention recipient snapshot is incomplete' USING ERRCODE='23514'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER comment_mention_snapshot_complete AFTER INSERT OR UPDATE OR DELETE ON comment_mention_snapshots
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_comment_mention_cardinality();
CREATE CONSTRAINT TRIGGER comment_mention_recipient_complete AFTER INSERT OR UPDATE OR DELETE ON comment_mention_recipients
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_comment_mention_cardinality();
REVOKE ALL ON FUNCTION enforce_comment_mention_snapshot(),enforce_comment_mention_recipient(),enforce_comment_mention_cardinality() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('058_comment_mention_snapshots');
COMMIT;
