BEGIN;
-- Selection stays on the stable Card identity and follows a Board move.
-- Existing Cards start without a cover; no publication proof is invented.
ALTER TABLE cards ADD COLUMN cover_attachment_id uuid;
ALTER TABLE cards ADD CONSTRAINT cards_cover_attachment_fk
 FOREIGN KEY(cover_attachment_id,tenant_id,id) REFERENCES attachments(id,tenant_id,card_id)
 ON DELETE RESTRICT DEFERRABLE INITIALLY IMMEDIATE;
CREATE INDEX ix_cards_cover_attachment ON cards(tenant_id,cover_attachment_id,id)
 WHERE cover_attachment_id IS NOT NULL;

CREATE FUNCTION enforce_card_cover_selection() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='UPDATE' THEN
  IF NEW.cover_attachment_id IS NOT DISTINCT FROM OLD.cover_attachment_id THEN RETURN NEW; END IF;
  IF OLD.version=9223372036854775807 OR NEW.version<>OLD.version+1 OR NEW.updated_at<OLD.updated_at THEN
   RAISE EXCEPTION 'Card cover revision is invalid' USING ERRCODE='23514';
  END IF;
 END IF;
 IF NEW.cover_attachment_id IS NULL THEN RETURN NEW; END IF;
 IF NEW.lifecycle_state<>'ACTIVE' OR NOT EXISTS(
   SELECT 1 FROM public.boards b JOIN public.board_lists l ON l.board_id=b.id AND l.tenant_id=b.tenant_id
   WHERE b.id=NEW.board_id AND b.tenant_id=NEW.tenant_id AND l.id=NEW.list_id
    AND b.lifecycle_state='ACTIVE' AND l.lifecycle_state='ACTIVE'
 ) THEN RAISE EXCEPTION 'Card cover parent is unavailable' USING ERRCODE='23514'; END IF;
 -- Hold the source row while selecting. A concurrent lifecycle change cannot
 -- race a selection and commit a cover referencing an unavailable source.
 PERFORM 1 FROM public.attachments a WHERE a.id=NEW.cover_attachment_id
  AND a.tenant_id=NEW.tenant_id AND a.card_id=NEW.id FOR UPDATE;
 IF NOT EXISTS(
  SELECT 1 FROM public.attachments a
  JOIN public.attachment_previews m ON m.tenant_id=a.tenant_id AND m.card_id=a.card_id AND m.attachment_id=a.id
  JOIN public.attachment_preview_publications p ON p.tenant_id=m.tenant_id AND p.id=m.id
  WHERE a.id=NEW.cover_attachment_id AND a.tenant_id=NEW.tenant_id AND a.card_id=NEW.id
   AND a.kind='FILE' AND a.lifecycle_state='ACTIVE' AND a.deleted_at IS NULL AND a.scan_status='CLEAN'
   AND a.scanned_at IS NOT NULL AND a.mime_type IN ('image/png','image/jpeg','image/webp')
   AND m.policy_version=1 AND p.attachment_version=m.source_version+1 AND a.version>=p.attachment_version
   AND m.source_sha256=a.sha256 AND m.source_size_bytes=a.size_bytes AND m.source_mime_type=a.mime_type
 ) THEN RAISE EXCEPTION 'Card cover source is unavailable' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER cards_cover_selection_guard BEFORE INSERT OR UPDATE ON cards
 FOR EACH ROW EXECUTE FUNCTION enforce_card_cover_selection();

CREATE FUNCTION enforce_selected_cover_source() RETURNS trigger
LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF NOT EXISTS(SELECT 1 FROM public.cards c WHERE c.tenant_id=NEW.tenant_id
  AND c.id=NEW.card_id AND c.cover_attachment_id=NEW.id) THEN RETURN NULL; END IF;
 -- Clearing may occur in either order inside the owning transaction. An
 -- archived/deleted source must not remain selected at its commit boundary.
 IF NEW.lifecycle_state IN ('ARCHIVED','DELETED') OR NOT EXISTS(
  SELECT 1 FROM public.attachments a
  JOIN public.attachment_previews m ON m.tenant_id=a.tenant_id AND m.card_id=a.card_id AND m.attachment_id=a.id
  JOIN public.attachment_preview_publications p ON p.tenant_id=m.tenant_id AND p.id=m.id
  WHERE a.id=NEW.id AND a.tenant_id=NEW.tenant_id AND a.card_id=NEW.card_id
   AND a.kind='FILE' AND a.lifecycle_state='ACTIVE' AND a.deleted_at IS NULL AND a.scan_status='CLEAN'
   AND a.scanned_at IS NOT NULL AND a.mime_type IN ('image/png','image/jpeg','image/webp')
   AND m.policy_version=1 AND p.attachment_version=m.source_version+1 AND a.version>=p.attachment_version
   AND m.source_sha256=a.sha256 AND m.source_size_bytes=a.size_bytes AND m.source_mime_type=a.mime_type
 ) THEN RAISE EXCEPTION 'Unavailable attachment remains selected as a cover' USING ERRCODE='23514'; END IF;
 RETURN NULL;
END $$;
CREATE CONSTRAINT TRIGGER attachments_selected_cover_guard AFTER UPDATE ON attachments
 DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION enforce_selected_cover_source();
REVOKE ALL ON FUNCTION enforce_card_cover_selection(),enforce_selected_cover_source() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('053_card_attachment_covers');
COMMIT;
