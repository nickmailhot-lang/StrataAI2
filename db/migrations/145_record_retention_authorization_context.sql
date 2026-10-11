BEGIN;

-- NULL is deliberate missing historical evidence, never a current-Board backfill.
ALTER TABLE record_retention_sources
 ADD COLUMN authorization_context_version smallint,
 ADD COLUMN original_board_id uuid,
 ADD COLUMN source_version bigint,
 ADD CONSTRAINT retention_source_context_shape CHECK(
  (authorization_context_version IS NULL AND original_board_id IS NULL AND source_version IS NULL)
  OR (authorization_context_version IS NOT NULL AND authorization_context_version=1 AND original_board_id IS NOT NULL AND source_version IS NOT NULL AND source_version>0
   AND source_kind IN ('CARD','CARD_COMMENT','ATTACHMENT'))),
 ADD CONSTRAINT retention_source_original_board FOREIGN KEY(original_board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT;

CREATE FUNCTION capture_retention_source_context() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE board uuid; revision bigint;
BEGIN
 IF NEW.authorization_context_version IS NOT NULL OR NEW.original_board_id IS NOT NULL OR NEW.source_version IS NOT NULL THEN
  RAISE EXCEPTION 'Retention source context is server captured' USING ERRCODE='23514'; END IF;
 -- Runs after the existing journal guard's owning parent/account/membership
 -- admission. Its source row locks remain held through this same transaction.
 CASE NEW.source_kind
  WHEN 'CARD' THEN SELECT board_id,version INTO board,revision FROM cards WHERE tenant_id=NEW.tenant_id AND id=NEW.record_id;
  WHEN 'CARD_COMMENT' THEN
   SELECT c.board_id,s.version INTO board,revision FROM card_comments s JOIN cards c ON(c.tenant_id,c.id)=(s.tenant_id,s.card_id)
    WHERE s.tenant_id=NEW.tenant_id AND s.id=NEW.record_id AND s.card_id=NEW.parent_card_id;
  WHEN 'ATTACHMENT' THEN
   SELECT c.board_id,s.version INTO board,revision FROM attachments s JOIN cards c ON(c.tenant_id,c.id)=(s.tenant_id,s.card_id)
    WHERE s.tenant_id=NEW.tenant_id AND s.id=NEW.record_id AND s.card_id=NEW.parent_card_id;
  WHEN 'AUDIT_EVENT' THEN RETURN NEW; -- Originating-module authorization still required.
  ELSE RAISE EXCEPTION 'Unknown source authorization context' USING ERRCODE='23514';
 END CASE;
 IF board IS NULL OR revision IS NULL OR revision<1 THEN RAISE EXCEPTION 'Source context unavailable' USING ERRCODE='23514'; END IF;
 NEW.authorization_context_version:=1; NEW.original_board_id:=board; NEW.source_version:=revision;
 RETURN NEW;
END $$;
CREATE TRIGGER record_retention_source_z_context BEFORE INSERT ON record_retention_sources FOR EACH ROW EXECUTE FUNCTION capture_retention_source_context();

ALTER TABLE record_retention_holds
 ADD COLUMN authorization_context_version smallint,
 ADD COLUMN classification_board_id uuid,
 ADD COLUMN application_board_id uuid,
 ADD COLUMN application_source_version bigint,
 ADD CONSTRAINT retention_hold_context_shape CHECK(
  (authorization_context_version IS NULL AND classification_board_id IS NULL AND application_board_id IS NULL AND application_source_version IS NULL)
  OR (authorization_context_version IS NOT NULL AND authorization_context_version=1 AND record_id IS NOT NULL AND classification_board_id IS NOT NULL
   AND application_board_id IS NOT NULL AND application_source_version IS NOT NULL AND application_source_version>0)),
 ADD CONSTRAINT retention_hold_classification_board FOREIGN KEY(classification_board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT,
 ADD CONSTRAINT retention_hold_application_board FOREIGN KEY(application_board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT;
ALTER TABLE record_retention_hold_history
 ADD COLUMN authorization_context_version smallint,
 ADD COLUMN classification_board_id uuid,
 ADD COLUMN application_board_id uuid,
 ADD COLUMN application_source_version bigint,
 ADD CONSTRAINT retention_hold_history_context_shape CHECK(
  (authorization_context_version IS NULL AND classification_board_id IS NULL AND application_board_id IS NULL AND application_source_version IS NULL)
  OR (authorization_context_version IS NOT NULL AND authorization_context_version=1 AND record_id IS NOT NULL AND classification_board_id IS NOT NULL
   AND application_board_id IS NOT NULL AND application_source_version IS NOT NULL AND application_source_version>0)),
 ADD CONSTRAINT retention_hold_history_classification_board FOREIGN KEY(classification_board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT,
 ADD CONSTRAINT retention_hold_history_application_board FOREIGN KEY(application_board_id,tenant_id) REFERENCES boards(id,tenant_id) ON DELETE RESTRICT;

CREATE FUNCTION capture_retention_hold_context() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE source record; board uuid; revision bigint;
BEGIN
 IF TG_OP='UPDATE' THEN
  IF (NEW.authorization_context_version,NEW.classification_board_id,NEW.application_board_id,NEW.application_source_version)
   IS DISTINCT FROM (OLD.authorization_context_version,OLD.classification_board_id,OLD.application_board_id,OLD.application_source_version) THEN
   RAISE EXCEPTION 'Original hold authorization context is retained' USING ERRCODE='23514'; END IF;
  RETURN NEW;
 END IF;
 IF NEW.authorization_context_version IS NOT NULL OR NEW.classification_board_id IS NOT NULL
  OR NEW.application_board_id IS NOT NULL OR NEW.application_source_version IS NOT NULL THEN
  RAISE EXCEPTION 'Hold context is server captured' USING ERRCODE='23514'; END IF;
 IF NEW.record_id IS NULL THEN RETURN NEW; END IF;
 SELECT * INTO source FROM record_retention_sources WHERE tenant_id=NEW.tenant_id AND record_class=NEW.record_class AND record_id=NEW.record_id;
 IF source.authorization_context_version IS NULL THEN RETURN NEW; END IF;
 CASE source.source_kind
  WHEN 'CARD' THEN SELECT board_id,version INTO board,revision FROM cards WHERE tenant_id=NEW.tenant_id AND id=NEW.record_id FOR SHARE;
  WHEN 'CARD_COMMENT' THEN
   SELECT c.board_id,s.version INTO board,revision FROM card_comments s JOIN cards c ON(c.tenant_id,c.id)=(s.tenant_id,s.card_id)
    WHERE s.tenant_id=NEW.tenant_id AND s.id=NEW.record_id AND s.card_id=source.parent_card_id FOR SHARE OF c,s;
  WHEN 'ATTACHMENT' THEN
   SELECT c.board_id,s.version INTO board,revision FROM attachments s JOIN cards c ON(c.tenant_id,c.id)=(s.tenant_id,s.card_id)
    WHERE s.tenant_id=NEW.tenant_id AND s.id=NEW.record_id AND s.card_id=source.parent_card_id FOR SHARE OF c,s;
  ELSE RAISE EXCEPTION 'Hold producer context unavailable' USING ERRCODE='23514';
 END CASE;
 IF board IS NULL OR revision IS NULL OR revision<1 THEN RAISE EXCEPTION 'Hold source context unavailable' USING ERRCODE='23514'; END IF;
 NEW.authorization_context_version:=1; NEW.classification_board_id:=source.original_board_id;
 NEW.application_board_id:=board; NEW.application_source_version:=revision;
 RETURN NEW;
END $$;
CREATE TRIGGER retention_hold_z_context BEFORE INSERT OR UPDATE ON record_retention_holds FOR EACH ROW EXECUTE FUNCTION capture_retention_hold_context();

CREATE FUNCTION capture_retention_hold_history_context() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE source record;
BEGIN
 IF NEW.authorization_context_version IS NOT NULL OR NEW.classification_board_id IS NOT NULL
  OR NEW.application_board_id IS NOT NULL OR NEW.application_source_version IS NOT NULL THEN
  RAISE EXCEPTION 'Hold history context is server captured' USING ERRCODE='23514'; END IF;
 SELECT * INTO source FROM record_retention_holds WHERE tenant_id=NEW.tenant_id AND id=NEW.hold_id;
 NEW.authorization_context_version:=source.authorization_context_version; NEW.classification_board_id:=source.classification_board_id;
 NEW.application_board_id:=source.application_board_id; NEW.application_source_version:=source.application_source_version;
 RETURN NEW;
END $$;
CREATE TRIGGER retention_hold_history_z_context BEFORE INSERT ON record_retention_hold_history FOR EACH ROW EXECUTE FUNCTION capture_retention_hold_history_context();
REVOKE ALL ON FUNCTION capture_retention_source_context(),capture_retention_hold_context(),capture_retention_hold_history_context() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('145_record_retention_authorization_context');
COMMIT;
