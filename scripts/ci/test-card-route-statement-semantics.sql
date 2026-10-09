-- PRD-01-TC-01/08, FOUND-FR-004/009: statement writes preserve canonical routes and rollback.
-- Transaction-local fixtures; no existing Cards are mutated or deleted.
BEGIN;
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at) VALUES
  ('2ed5da22-72ef-49b4-8dc3-a89b416c76e1','route-a@example.test','ROUTE-A@EXAMPLE.TEST','Route A','ACTIVE','unusable-ci-fixture',now(),now()),
  ('32781d45-276d-4608-8cf7-3521035f1bf3','route-b@example.test','ROUTE-B@EXAMPLE.TEST','Route B','ACTIVE','unusable-ci-fixture',now(),now());
  INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at) VALUES
  ('20626be7-af2f-4408-9f56-3e9b66d31d27','Route A','2ed5da22-72ef-49b4-8dc3-a89b416c76e1',now(),now()),('8c8962ba-0f7d-4f01-be52-3a2a0ca128d0','Route B','32781d45-276d-4608-8cf7-3521035f1bf3',now(),now());
  INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
  (gen_random_uuid(),'20626be7-af2f-4408-9f56-3e9b66d31d27','2ed5da22-72ef-49b4-8dc3-a89b416c76e1','OWNER','ACTIVE'),(gen_random_uuid(),'8c8962ba-0f7d-4f01-be52-3a2a0ca128d0','32781d45-276d-4608-8cf7-3521035f1bf3','OWNER','ACTIVE');
  INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
  ('4fabb28e-7a40-429b-a177-a78f4fa08200','20626be7-af2f-4408-9f56-3e9b66d31d27','Route A',now(),now()),('8f2feec9-4308-495f-8016-60d710e46c6a','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0','Route B',now(),now());
  INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
  ('8702e5df-a6b4-45b9-925e-7c89280a8cf7','20626be7-af2f-4408-9f56-3e9b66d31d27','4fabb28e-7a40-429b-a177-a78f4fa08200','Route A',lpad('1',30,'0'),now(),now()),
  ('6e43a7a9-a52e-4619-a326-465a45c19fb6','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0','8f2feec9-4308-495f-8016-60d710e46c6a','Route B',lpad('1',30,'0'),now(),now());
  INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
  ('896f683d-352c-4b6c-9108-6bd7cfc31ea6','20626be7-af2f-4408-9f56-3e9b66d31d27','4fabb28e-7a40-429b-a177-a78f4fa08200','8702e5df-a6b4-45b9-925e-7c89280a8cf7','Route A',lpad('1',30,'0'),now(),now()),
  ('ae2ea66b-115c-42e5-b0aa-78562c5d1d22','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0','8f2feec9-4308-495f-8016-60d710e46c6a','6e43a7a9-a52e-4619-a326-465a45c19fb6','Route B',lpad('1',30,'0'),now(),now());
CREATE TEMP VIEW statement_cards AS SELECT * FROM cards WHERE tenant_id IN ('20626be7-af2f-4408-9f56-3e9b66d31d27','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0');
CREATE FUNCTION pg_temp.assert_routes() RETURNS void LANGUAGE plpgsql AS $$
BEGIN
 IF EXISTS(SELECT 1 FROM cards c FULL JOIN card_routes r ON r.card_id=c.id
 WHERE (c.id IS NULL OR r.card_id IS NULL OR
 ROW(c.tenant_id,c.board_id,c.list_id,c.lifecycle_state,c.created_at,c.updated_at)
 IS DISTINCT FROM ROW(r.tenant_id,r.board_id,r.list_id,r.lifecycle_state,r.created_at,r.updated_at))
 AND COALESCE(c.tenant_id,r.tenant_id) IN ('20626be7-af2f-4408-9f56-3e9b66d31d27','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0'))
 THEN RAISE EXCEPTION 'Canonical route mismatch'; END IF;
END $$;
SELECT pg_temp.assert_routes();
DO $$ DECLARE inserted_count integer; BEGIN
WITH added AS (
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
 SELECT gen_random_uuid(),tenant_id,board_id,list_id,'Statement fixture',lpad('2',30,'0'),now(),now()
 FROM statement_cards RETURNING id
) SELECT count(*) INTO inserted_count FROM added;
IF inserted_count<>2 THEN RAISE EXCEPTION 'CTE Card insert count changed'; END IF; END $$;
SELECT pg_temp.assert_routes();
UPDATE cards SET title='Changed',updated_at=clock_timestamp(),version=version+1 WHERE tenant_id IN ('20626be7-af2f-4408-9f56-3e9b66d31d27','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0');
SELECT pg_temp.assert_routes();
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
 SELECT id,tenant_id,board_id,list_id,title,rank,created_at,updated_at FROM statement_cards
 ON CONFLICT(id) DO UPDATE SET title='Conflict change',updated_at=clock_timestamp(),version=cards.version+1;
SELECT pg_temp.assert_routes();
UPDATE cards SET title=title WHERE false AND tenant_id IN ('20626be7-af2f-4408-9f56-3e9b66d31d27','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0');
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
 SELECT gen_random_uuid(),tenant_id,board_id,list_id,title,rank,created_at,updated_at FROM statement_cards WHERE false;
SELECT pg_temp.assert_routes();
DO $$ DECLARE before_state jsonb; after_state jsonb;
BEGIN
 SELECT jsonb_agg(to_jsonb(r) ORDER BY card_id) INTO before_state FROM card_routes r WHERE tenant_id IN ('20626be7-af2f-4408-9f56-3e9b66d31d27','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0');
 BEGIN
 UPDATE cards SET updated_at=clock_timestamp(),version=version+1 WHERE tenant_id IN ('20626be7-af2f-4408-9f56-3e9b66d31d27','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0');
 PERFORM pg_temp.assert_routes();
 RAISE EXCEPTION 'Intentional rollback' USING ERRCODE='P1234';
 EXCEPTION WHEN SQLSTATE 'P1234' THEN NULL;
 END;
 SELECT jsonb_agg(to_jsonb(r) ORDER BY card_id) INTO after_state FROM card_routes r WHERE tenant_id IN ('20626be7-af2f-4408-9f56-3e9b66d31d27','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0');
 IF before_state IS DISTINCT FROM after_state THEN RAISE EXCEPTION 'Rollback changed routes'; END IF;
END $$;
DELETE FROM cards WHERE tenant_id IN ('20626be7-af2f-4408-9f56-3e9b66d31d27','8c8962ba-0f7d-4f01-be52-3a2a0ca128d0');
DO $$ BEGIN IF EXISTS(SELECT 1 FROM statement_cards) THEN RAISE EXCEPTION 'Delete left fixture Cards'; END IF; END $$;
SELECT pg_temp.assert_routes();
ROLLBACK;
