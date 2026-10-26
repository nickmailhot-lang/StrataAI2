BEGIN;
-- A stable property identity is retained through identifier corrections and
-- deactivation. Directory labels are exact, case-sensitive identifiers.
CREATE TABLE strata_lots (
 tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
 id uuid NOT NULL CHECK(id<>'00000000-0000-0000-0000-000000000000'),
 version bigint NOT NULL CHECK(version>0),
 unit_identifier text NOT NULL CHECK(length(unit_identifier) BETWEEN 1 AND 80 AND unit_identifier=btrim(unit_identifier)),
 lot_identifier text NOT NULL CHECK(length(lot_identifier) BETWEEN 1 AND 80 AND lot_identifier=btrim(lot_identifier)),
 payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
 PRIMARY KEY(tenant_id,id),
 CONSTRAINT strata_lots_unit_key UNIQUE(tenant_id,unit_identifier),
 CONSTRAINT strata_lots_lot_key UNIQUE(tenant_id,lot_identifier),
 CHECK(((payload->>'id')::uuid=id AND (payload->>'organizationId')::uuid=tenant_id AND (payload->>'version')::bigint=version) IS TRUE),
 CHECK((payload->'data'->>'unitIdentifier'=unit_identifier AND payload->'data'->>'lotIdentifier'=lot_identifier) IS TRUE),
 CHECK((payload->'data'->>'status' IN ('ACTIVE','INACTIVE')) IS TRUE),
 CHECK((payload->>'createdAt') IS NOT NULL AND (payload->>'updatedAt') IS NOT NULL
  AND (payload->>'updatedAt')::timestamptz >= (payload->>'createdAt')::timestamptz)
);
CREATE TABLE strata_lot_history (
 tenant_id uuid NOT NULL,
 lot_id uuid NOT NULL,
 version bigint NOT NULL CHECK(version>0),
 event_id uuid NOT NULL UNIQUE,
 payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
 PRIMARY KEY(tenant_id,lot_id,version),
 FOREIGN KEY(tenant_id,lot_id) REFERENCES strata_lots(tenant_id,id) ON DELETE RESTRICT,
 CHECK(((payload->'lot'->>'id')::uuid=lot_id AND (payload->'lot'->>'organizationId')::uuid=tenant_id
  AND (payload->'lot'->>'version')::bigint=version AND (payload->>'eventId')::uuid=event_id) IS TRUE)
);
CREATE TABLE strata_lot_receipts (
 tenant_id uuid NOT NULL,
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 key_id uuid NOT NULL,
 lot_id uuid NOT NULL,
 version bigint NOT NULL,
 payload jsonb NOT NULL CHECK(jsonb_typeof(payload)='object'),
 PRIMARY KEY(tenant_id,actor_id,key_id),
 FOREIGN KEY(tenant_id,lot_id,version) REFERENCES strata_lot_history(tenant_id,lot_id,version) ON DELETE RESTRICT,
 CHECK(((payload->>'actorId')::uuid=actor_id AND (payload->>'key')::uuid=key_id
  AND (payload->'result'->>'id')::uuid=lot_id AND (payload->'result'->>'organizationId')::uuid=tenant_id
  AND (payload->'result'->>'version')::bigint=version) IS TRUE)
);
ALTER TABLE strata_lots ADD CONSTRAINT strata_lot_current_history FOREIGN KEY(tenant_id,id,version)
 REFERENCES strata_lot_history(tenant_id,lot_id,version) DEFERRABLE INITIALLY DEFERRED;
DO $$ DECLARE relation text; BEGIN
 FOREACH relation IN ARRAY ARRAY['strata_lots','strata_lot_history','strata_lot_receipts'] LOOP
  EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY',relation);
  EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY',relation);
  EXECUTE format('CREATE POLICY %I ON %I USING(tenant_id=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)
   WITH CHECK(tenant_id=NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)',relation||'_tenant',relation);
 END LOOP;
END $$;
CREATE FUNCTION guard_strata_lot_identity() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='DELETE' THEN RAISE EXCEPTION 'Strata lot identity is retained' USING ERRCODE='23514'; END IF;
 IF TG_OP='INSERT' THEN
  IF NEW.version<>1 THEN RAISE EXCEPTION 'Strata lot starts at revision one' USING ERRCODE='23514'; END IF;
 ELSE
  IF (NEW.tenant_id,NEW.id,NEW.payload->>'createdAt') IS DISTINCT FROM (OLD.tenant_id,OLD.id,OLD.payload->>'createdAt')
   OR OLD.version=9223372036854775807 OR NEW.version<>OLD.version+1
   OR (NEW.payload->>'updatedAt')::timestamptz<(OLD.payload->>'updatedAt')::timestamptz THEN
   RAISE EXCEPTION 'Strata lot identity/revision is invalid' USING ERRCODE='23514';
  END IF;
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER strata_lot_identity BEFORE INSERT OR UPDATE OR DELETE ON strata_lots FOR EACH ROW EXECUTE FUNCTION guard_strata_lot_identity();
CREATE TRIGGER strata_lot_history_immutable BEFORE UPDATE OR DELETE ON strata_lot_history FOR EACH ROW EXECUTE FUNCTION prevent_audit_event_mutation();
CREATE TRIGGER strata_lot_receipt_immutable BEFORE UPDATE OR DELETE ON strata_lot_receipts FOR EACH ROW EXECUTE FUNCTION prevent_audit_event_mutation();
REVOKE ALL ON FUNCTION guard_strata_lot_identity() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('151_strata_lot_directory');
COMMIT;
