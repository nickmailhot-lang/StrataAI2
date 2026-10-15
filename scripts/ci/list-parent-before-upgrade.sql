BEGIN;
SELECT set_config('app.tenant_id','14700000-0000-0000-0000-000000000001',true);
INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
VALUES('14700000-0000-0000-0000-000000000099','parent-upgrade@example.test','PARENT-UPGRADE@EXAMPLE.TEST','Parent fixture','unusable-fixture','ACTIVE',true,now(),now());
INSERT INTO organizations(id,name,owner_user_id,status,organization_type,created_at,updated_at)
VALUES('14700000-0000-0000-0000-000000000001','Parent upgrade','14700000-0000-0000-0000-000000000099','ACTIVE','STRATA',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at)
VALUES(gen_random_uuid(),'14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000099','OWNER','ACTIVE',now(),now());
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
('14700000-0000-0000-0000-000000000011','14700000-0000-0000-0000-000000000001','Source',now(),now()),
('14700000-0000-0000-0000-000000000012','14700000-0000-0000-0000-000000000001','Destination',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
VALUES('14700000-0000-0000-0000-000000000021','14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000011','Preserved List',repeat('5',30),now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,archived_at,created_at,updated_at)
SELECT ('14700000-0000-0000-0000-'||lpad((30+n)::text,12,'0'))::uuid,
'14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000011','14700000-0000-0000-0000-000000000021',
'Preserved Card '||n,'Historical body',repeat(n::text,30),CASE n WHEN 1 THEN 'ACTIVE' ELSE 'ARCHIVED' END,
CASE WHEN n=2 THEN now() END,now(),now() FROM generate_series(1,2) n;
CREATE TABLE list_parent_upgrade_fixture(kind text,id uuid,value jsonb);
INSERT INTO list_parent_upgrade_fixture SELECT 'list',id,to_jsonb(l) FROM board_lists l WHERE tenant_id='14700000-0000-0000-0000-000000000001';
INSERT INTO list_parent_upgrade_fixture SELECT 'card',id,to_jsonb(c) FROM cards c WHERE tenant_id='14700000-0000-0000-0000-000000000001';
DO $$ BEGIN
 IF NOT EXISTS(SELECT FROM pg_constraint WHERE conrelid='cards'::regclass
  AND conname='cards_list_id_board_id_tenant_id_fkey' AND NOT condeferrable) THEN
  RAISE EXCEPTION 'List parent upgrade did not start from the original immediate constraint'; END IF;
END $$;
COMMIT;
