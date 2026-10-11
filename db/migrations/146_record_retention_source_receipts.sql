BEGIN;

-- Historical classifications have no invented command identity. New owning
-- commands supply a key and must commit the exact source/audit/receipt graph.
ALTER TABLE record_retention_sources
 ADD COLUMN command_key uuid CHECK(command_key IS NULL OR command_key<>'00000000-0000-0000-0000-000000000000'::uuid),
 ADD CONSTRAINT retention_source_command_context CHECK(command_key IS NULL OR
  (source_kind IN ('CARD','CARD_COMMENT','ATTACHMENT') AND authorization_context_version IS NOT NULL
   AND authorization_context_version=1 AND original_board_id IS NOT NULL AND source_version IS NOT NULL)),
 ADD CONSTRAINT retention_source_actor_key UNIQUE(tenant_id,actor_id,command_key),
 ADD CONSTRAINT retention_source_receipt_identity UNIQUE(tenant_id,record_class,record_id,actor_id,command_key,event_id);

CREATE TABLE record_retention_source_receipts (
 tenant_id uuid NOT NULL,
 actor_id uuid NOT NULL,
 key_id uuid NOT NULL CHECK(key_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 record_class text NOT NULL,
 record_id uuid NOT NULL,
 event_id uuid NOT NULL,
 fingerprint text NOT NULL CHECK(fingerprint COLLATE "C" ~ '^[A-F0-9]{64}$'),
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 expires_at timestamptz NOT NULL CHECK(isfinite(expires_at) AND expires_at=created_at+interval '24 hours'),
 PRIMARY KEY(tenant_id,actor_id,key_id),
 UNIQUE(tenant_id,record_class,record_id),
 FOREIGN KEY(tenant_id,record_class,record_id,actor_id,key_id,event_id)
  REFERENCES record_retention_sources(tenant_id,record_class,record_id,actor_id,command_key,event_id) ON DELETE RESTRICT
);
ALTER TABLE record_retention_sources ADD CONSTRAINT retention_source_command_receipt
 FOREIGN KEY(tenant_id,actor_id,command_key) REFERENCES record_retention_source_receipts(tenant_id,actor_id,key_id)
 ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;

ALTER TABLE record_retention_source_receipts ENABLE ROW LEVEL SECURITY;
ALTER TABLE record_retention_source_receipts FORCE ROW LEVEL SECURITY;
CREATE POLICY retention_source_receipt_actor ON record_retention_source_receipts
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid
  AND actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid
  AND actor_id=NULLIF(current_setting('app.identity_subject',true),'')::uuid);

CREATE FUNCTION guard_retention_source_receipt() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog,public AS $$
DECLARE source_clock timestamptz;
BEGIN
 IF NEW.tenant_id IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR NEW.actor_id IS DISTINCT FROM NULLIF(current_setting('app.identity_subject',true),'')::uuid THEN
  RAISE EXCEPTION 'Classification receipt context mismatch' USING ERRCODE='42501'; END IF;
 SELECT registered_at INTO source_clock FROM record_retention_sources
 WHERE tenant_id=NEW.tenant_id AND record_class=NEW.record_class AND record_id=NEW.record_id
  AND actor_id=NEW.actor_id AND command_key=NEW.key_id AND event_id=NEW.event_id;
 IF source_clock IS NULL OR NEW.created_at<>source_clock THEN
  RAISE EXCEPTION 'Classification receipt source mismatch' USING ERRCODE='23514'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER retention_source_receipt_guard BEFORE INSERT ON record_retention_source_receipts
 FOR EACH ROW EXECUTE FUNCTION guard_retention_source_receipt();
CREATE TRIGGER retention_source_receipt_immutable BEFORE UPDATE OR DELETE ON record_retention_source_receipts
 FOR EACH ROW EXECUTE FUNCTION guard_retention_source_immutable();
REVOKE ALL ON record_retention_source_receipts FROM PUBLIC;
REVOKE ALL ON FUNCTION guard_retention_source_receipt() FROM PUBLIC;
-- No runtime grants. Owning retry commands, current producer authorization and
-- both Production/Demo adapters remain prerequisites for activation.
INSERT INTO schema_migrations(version) VALUES('146_record_retention_source_receipts');
COMMIT;
