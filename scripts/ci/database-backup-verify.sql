\set ON_ERROR_STOP on
BEGIN;
DO $$ BEGIN
 IF (SELECT count(*) FROM cards) <> 2 OR (SELECT count(*) FROM audit_events) <> 2 THEN
  RAISE EXCEPTION 'Recovery did not preserve the complete two-Organization fixture'; END IF;
 IF '[1,0,0]'::vector <-> '[0,1,0]'::vector <= 0 THEN
  RAISE EXCEPTION 'Restored pgvector is unavailable'; END IF;
 BEGIN
  UPDATE audit_events SET correlation_id='must-refuse';
  RAISE EXCEPTION 'Restored audit history accepted mutation';
 EXCEPTION WHEN raise_exception THEN
  IF SQLERRM <> 'audit_events are append-only' THEN RAISE; END IF;
 END;
 BEGIN
  UPDATE cards SET list_id='bacc0000-0000-0000-0000-000000000022'
   WHERE tenant_id='bacc0000-0000-0000-0000-000000000001';
  RAISE EXCEPTION 'Restored graph accepted a cross-Organization parent';
 EXCEPTION WHEN foreign_key_violation THEN NULL;
 END;
END $$;
SET ROLE :"reader";
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS(SELECT FROM cards) THEN RAISE EXCEPTION 'Restored missing-context read disclosed Cards'; END IF;
END $$;
SELECT set_config('app.tenant_id','bacc0000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM cards) <> 1 OR
  EXISTS(SELECT FROM cards WHERE tenant_id<>'bacc0000-0000-0000-0000-000000000001') THEN
  RAISE EXCEPTION 'Restored tenant policy disclosed another Organization'; END IF;
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
 VALUES(gen_random_uuid(),'bacc0000-0000-0000-0000-000000000001','bacc0000-0000-0000-0000-000000000011','Permitted own-tenant write',repeat('7',30),now(),now());
 IF (SELECT count(*) FROM board_lists) <> 2 THEN
  RAISE EXCEPTION 'Restored restricted role cannot perform its permitted write'; END IF;
 BEGIN
  INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
  VALUES(gen_random_uuid(),'bacc0000-0000-0000-0000-000000000002','bacc0000-0000-0000-0000-000000000012','Must refuse',repeat('7',30),now(),now());
  RAISE EXCEPTION 'Restored tenant policy accepted a foreign write';
 EXCEPTION WHEN insufficient_privilege THEN
  IF SQLERRM <> 'new row violates row-level security policy for table "board_lists"' THEN RAISE; END IF;
 END;
END $$;
ROLLBACK;
