BEGIN;
CREATE TABLE label_routes (
 label_id uuid PRIMARY KEY,
 tenant_id uuid NOT NULL,
 board_id uuid NOT NULL,
 status text NOT NULL CHECK (status IN ('ACTIVE','DELETED')),
 FOREIGN KEY (label_id,board_id,tenant_id) REFERENCES board_labels(id,board_id,tenant_id) ON DELETE CASCADE
);
INSERT INTO label_routes SELECT id,tenant_id,board_id,status FROM board_labels;
ALTER TABLE label_routes ENABLE ROW LEVEL SECURITY;
ALTER TABLE label_routes FORCE ROW LEVEL SECURITY;
CREATE POLICY route_tenant_scope ON label_routes
 USING (tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK (tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE POLICY route_lookup ON label_routes FOR SELECT USING (
 current_setting('app.route_kind',true)='LABEL' AND label_id::text=current_setting('app.route_key',true));
CREATE FUNCTION sync_label_route() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 INSERT INTO label_routes(label_id,tenant_id,board_id,status) VALUES(NEW.id,NEW.tenant_id,NEW.board_id,NEW.status)
 ON CONFLICT(label_id) DO UPDATE SET status=EXCLUDED.status;
 RETURN NEW;
END;
$$;
CREATE TRIGGER board_labels_route AFTER INSERT OR UPDATE ON board_labels FOR EACH ROW EXECUTE FUNCTION sync_label_route();
ALTER TABLE work_events DROP CONSTRAINT work_events_entity_type_check;
ALTER TABLE work_events ADD CONSTRAINT work_events_entity_type_check CHECK(entity_type IN ('Board','List','Card','Label'));
INSERT INTO schema_migrations(version) VALUES ('029_label_routing');
COMMIT;
