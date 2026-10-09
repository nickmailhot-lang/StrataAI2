DO $$
DECLARE kind text; key_column text; source_table text; rows_before bigint; rows_after bigint;
BEGIN
 FOR kind,key_column,source_table IN
  SELECT * FROM (VALUES ('board_routes','board_id','boards'),('list_routes','list_id','board_lists'),
   ('card_routes','card_id','cards'),('label_routes','label_id','board_labels')) AS mapping(kind,key_column,source_table)
 LOOP
  SELECT count(*) INTO rows_before FROM entity_route_clock_upgrade_fixture WHERE route_kind=kind;
  EXECUTE format('SELECT count(*) FROM %I r JOIN entity_route_clock_upgrade_fixture f
   ON f.route_kind=$1 AND f.row_key=r.%I JOIN %I s ON s.id=r.%I AND s.tenant_id=r.tenant_id
   WHERE f.body=to_jsonb(r)-''created_at''-''updated_at'' AND r.created_at=s.created_at AND r.updated_at=s.updated_at',
   kind,key_column,source_table,key_column) INTO rows_after USING kind;
  IF rows_before=0 OR rows_after<>rows_before THEN
   RAISE EXCEPTION 'Historical route body or canonical clocks changed unexpectedly';
  END IF;
 END LOOP;
END $$;
DROP TABLE entity_route_clock_upgrade_fixture;
BEGIN;
DO $$
DECLARE kind text; key_column text; source_table text; row_id uuid; tenant uuid;
 old_created timestamptz; old_updated timestamptz; current_created timestamptz; current_updated timestamptz;
BEGIN
 FOR kind,key_column,source_table IN
  SELECT * FROM (VALUES ('board_routes','board_id','boards'),('list_routes','list_id','board_lists'),
   ('card_routes','card_id','cards'),('label_routes','label_id','board_labels')) AS mapping(kind,key_column,source_table)
 LOOP
  EXECUTE format('SELECT %I,tenant_id,created_at,updated_at FROM %I ORDER BY %I LIMIT 1',key_column,kind,key_column)
   INTO STRICT row_id,tenant,old_created,old_updated;
  PERFORM set_config('app.tenant_id',tenant::text,true);
  BEGIN
   EXECUTE format('UPDATE %I SET created_at=created_at+interval ''1 second'' WHERE %I=$1',kind,key_column) USING row_id;
   RAISE EXCEPTION 'Route creation clock replacement was admitted';
  EXCEPTION WHEN check_violation THEN NULL;
  END;
  BEGIN
   EXECUTE format('UPDATE %I SET updated_at=updated_at+interval ''100 years'' WHERE %I=$1',kind,key_column) USING row_id;
   RAISE EXCEPTION 'Route update clock replacement was admitted';
  EXCEPTION WHEN check_violation THEN NULL;
  END;
  EXECUTE format('UPDATE %I SET updated_at=updated_at WHERE %I=$1',kind,key_column) USING row_id;
  EXECUTE format('SELECT created_at,updated_at FROM %I WHERE %I=$1',kind,key_column)
   INTO current_created,current_updated USING row_id;
  IF current_created<>old_created OR current_updated<>old_updated THEN
   RAISE EXCEPTION 'No-op route update changed canonical clocks';
  END IF;
  EXECUTE format('UPDATE %I SET updated_at=updated_at+interval ''1 second'',version=version+1 WHERE id=$1',source_table) USING row_id;
  EXECUTE format('SELECT created_at,updated_at FROM %I WHERE %I=$1',kind,key_column)
   INTO current_created,current_updated USING row_id;
  IF current_created<>old_created OR current_updated<>old_updated+interval '1 second' THEN
   RAISE EXCEPTION 'Canonical entity mutation did not synchronize route clocks';
  END IF;
 END LOOP;
END $$;
ROLLBACK;
