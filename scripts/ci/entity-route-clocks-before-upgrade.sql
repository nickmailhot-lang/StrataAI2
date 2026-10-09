-- Earlier storage checks roll their fixture rows back. Populate an explicit
-- historical hierarchy so every projection is exercised by this upgrade.
INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
 SELECT 'f17a0000-0000-4000-8000-000000000001',id,'Route clock upgrade',
 '2020-01-01T00:00:00Z','2021-01-01T00:00:00Z'
 FROM organizations WHERE status='ACTIVE' ORDER BY id LIMIT 1;
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
 SELECT 'f17a0000-0000-4000-8000-000000000002',tenant_id,id,'Route clock list',
 repeat('1',30),created_at,updated_at FROM boards WHERE id='f17a0000-0000-4000-8000-000000000001';
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
 SELECT 'f17a0000-0000-4000-8000-000000000003',tenant_id,board_id,id,'Route clock card',
 repeat('1',30),created_at,updated_at FROM board_lists WHERE id='f17a0000-0000-4000-8000-000000000002';
INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank,created_at,updated_at)
 SELECT 'f17a0000-0000-4000-8000-000000000004',tenant_id,id,'Route clock label','blue',
 repeat('1',30),created_at,updated_at FROM boards WHERE id='f17a0000-0000-4000-8000-000000000001';
CREATE TABLE entity_route_clock_upgrade_fixture(route_kind text,row_key uuid,body jsonb,PRIMARY KEY(route_kind,row_key));
INSERT INTO entity_route_clock_upgrade_fixture
 SELECT 'board_routes',board_id,to_jsonb(r)-'created_at'-'updated_at' FROM board_routes r
 UNION ALL SELECT 'list_routes',list_id,to_jsonb(r)-'created_at'-'updated_at' FROM list_routes r
 UNION ALL SELECT 'card_routes',card_id,to_jsonb(r)-'created_at'-'updated_at' FROM card_routes r
 UNION ALL SELECT 'label_routes',label_id,to_jsonb(r)-'created_at'-'updated_at' FROM label_routes r;
DO $$ BEGIN
 IF (SELECT count(DISTINCT route_kind) FROM entity_route_clock_upgrade_fixture)<>4 THEN
  RAISE EXCEPTION 'Route clock forward fixture must cover all four populated projections';
 END IF;
END $$;
-- A legacy route can lack a canonical source. The upgrade must refuse without
-- partially installing clocks or altering any historical route body.
INSERT INTO board_routes(board_id,tenant_id,visibility,lifecycle_state,updated_at)
 SELECT 'f17a0000-0000-4000-8000-000000000099',tenant_id,'PRIVATE','ACTIVE',updated_at
 FROM boards WHERE id='f17a0000-0000-4000-8000-000000000001';
