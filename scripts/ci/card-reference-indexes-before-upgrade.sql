BEGIN;
SELECT set_config('app.tenant_id','14700000-0000-0000-0000-000000000001',true);
DO $$ BEGIN
 IF to_regclass('ix_card_labels_parent_identity') IS NOT NULL OR to_regclass('ix_card_members_parent_identity') IS NOT NULL THEN
  RAISE EXCEPTION 'Reference index upgrade did not start from the previous schema'; END IF;
END $$;
INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank,created_at,updated_at)
VALUES('14900000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000012','Preserved label','blue',repeat('5',30),now(),now());
INSERT INTO card_labels(tenant_id,board_id,card_id,label_id,version,created_at,updated_at)
VALUES('14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000012','14700000-0000-0000-0000-000000000031','14900000-0000-0000-0000-000000000001',3,now(),now());
INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
VALUES(gen_random_uuid(),'14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000012','14700000-0000-0000-0000-000000000099','MEMBER','ACTIVE',now(),now());
INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by,version,created_at,updated_at)
VALUES('14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000012','14700000-0000-0000-0000-000000000031','14700000-0000-0000-0000-000000000099','14700000-0000-0000-0000-000000000099',7,now(),now());
CREATE TABLE card_reference_upgrade_fixture(kind text,value jsonb);
INSERT INTO card_reference_upgrade_fixture SELECT 'label',to_jsonb(c) FROM card_labels c WHERE tenant_id='14700000-0000-0000-0000-000000000001';
INSERT INTO card_reference_upgrade_fixture SELECT 'member',to_jsonb(c) FROM card_members c WHERE tenant_id='14700000-0000-0000-0000-000000000001';
COMMIT;
