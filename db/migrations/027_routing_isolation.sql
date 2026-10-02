BEGIN;

-- Discovery returns only routing metadata; protected records remain separately
-- tenant-authorized. A lookup can select one entity/token or one authorized
-- subject/recipient. It cannot write routes or confer membership.
DO $$
DECLARE route_table text;
BEGIN
  FOREACH route_table IN ARRAY ARRAY['board_routes','list_routes','card_routes','user_organization_access','invitation_routes'] LOOP
    EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', route_table);
    EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', route_table);
    EXECUTE format('CREATE POLICY route_tenant_scope ON %I USING (tenant_id = NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid) WITH CHECK (tenant_id = NULLIF(current_setting(''app.tenant_id'',true),'''')::uuid)', route_table);
  END LOOP;
END;
$$;

CREATE POLICY route_lookup ON board_routes FOR SELECT USING (
  current_setting('app.route_kind',true)='BOARD' AND board_id::text=current_setting('app.route_key',true));
CREATE POLICY route_lookup ON list_routes FOR SELECT USING (
  current_setting('app.route_kind',true)='LIST' AND list_id::text=current_setting('app.route_key',true));
CREATE POLICY route_lookup ON card_routes FOR SELECT USING (
  current_setting('app.route_kind',true)='CARD' AND card_id::text=current_setting('app.route_key',true));
CREATE POLICY route_lookup ON user_organization_access FOR SELECT USING (
  current_setting('app.route_kind',true)='ORGANIZATION_USER' AND user_id::text=current_setting('app.route_key',true));
CREATE POLICY route_lookup ON invitation_routes FOR SELECT USING (
  (current_setting('app.route_kind',true)='INVITATION_TOKEN' AND token_hash=current_setting('app.route_key',true)) OR
  (current_setting('app.route_kind',true)='INVITATION_RECIPIENT' AND email_normalized=current_setting('app.route_key',true)) OR
  (current_setting('app.route_kind',true)='INVITATION_ID' AND invitation_id::text=current_setting('app.route_key',true)));

INSERT INTO schema_migrations(version) VALUES ('027_routing_isolation');
COMMIT;
