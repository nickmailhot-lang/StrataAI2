\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_upload_intent_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_upload_intent_ci;
GRANT SELECT,INSERT,UPDATE ON attachment_upload_intents TO strataai_upload_intent_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('04300000-0000-0000-0000-000000000001','Checklist A',now(),now()),
 ('04300000-0000-0000-0000-000000000002','Checklist B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('04300000-0000-0000-0000-000000000041','attachment-storage@example.test','ATTACHMENT-STORAGE@EXAMPLE.TEST','Checklist fixture','ACTIVE',true,'unused-checklist-hash',now(),now()),
 ('04300000-0000-0000-0000-000000000042','attachment-foreign@example.test','ATTACHMENT-FOREIGN@EXAMPLE.TEST','Foreign fixture','ACTIVE',true,'unused-checklist-hash',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'04300000-0000-0000-0000-000000000001','04300000-0000-0000-0000-000000000041','MEMBER','ACTIVE'),
 (gen_random_uuid(),'04300000-0000-0000-0000-000000000002','04300000-0000-0000-0000-000000000042','MEMBER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('04300000-0000-0000-0000-000000000011','04300000-0000-0000-0000-000000000001','A Board',now(),now()),
 ('04300000-0000-0000-0000-000000000012','04300000-0000-0000-0000-000000000002','B Board',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('04300000-0000-0000-0000-000000000021','04300000-0000-0000-0000-000000000001','04300000-0000-0000-0000-000000000011','A List','500000000000000000000000000000',now(),now()),
 ('04300000-0000-0000-0000-000000000022','04300000-0000-0000-0000-000000000002','04300000-0000-0000-0000-000000000012','B List','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('04300000-0000-0000-0000-000000000031','04300000-0000-0000-0000-000000000001','04300000-0000-0000-0000-000000000011','04300000-0000-0000-0000-000000000021','A Card','500000000000000000000000000000',now(),now()),
 ('04300000-0000-0000-0000-000000000032','04300000-0000-0000-0000-000000000002','04300000-0000-0000-0000-000000000012','04300000-0000-0000-0000-000000000022','B Card','500000000000000000000000000000',now(),now());
GRANT SELECT,INSERT,UPDATE ON attachments TO strataai_upload_intent_ci;
INSERT INTO attachment_upload_intents(id,tenant_id,card_id,uploader_id,retry_key,original_card_version,display_name,expected_size_bytes,expected_sha256,created_at,updated_at,expires_at)
VALUES ('04300000-0000-0000-0000-000000000051','04300000-0000-0000-0000-000000000001','04300000-0000-0000-0000-000000000031','04300000-0000-0000-0000-000000000041','04300000-0000-0000-0000-000000000061',1,'Quarantine',128,repeat('a',64),now(),now(),now()+interval '1 hour'),
 ('04300000-0000-0000-0000-000000000052','04300000-0000-0000-0000-000000000002','04300000-0000-0000-0000-000000000032','04300000-0000-0000-0000-000000000042','04300000-0000-0000-0000-000000000062',1,'Foreign',128,repeat('b',64),now(),now(),now()+interval '1 hour');
SET LOCAL ROLE strataai_upload_intent_ci;
DO $$ BEGIN
 IF (SELECT count(*) FROM attachment_upload_intents)<>0 THEN RAISE EXCEPTION 'Missing tenant exposed uploads'; END IF;
END $$;
SELECT set_config('app.tenant_id','04300000-0000-0000-0000-000000000001',true);
DO $$ DECLARE affected integer; mutation text; fixture attachment_upload_intents; candidate attachment_upload_intents; BEGIN
 IF (SELECT count(*) FROM attachment_upload_intents)<>1 THEN RAISE EXCEPTION 'Upload tenant read widened'; END IF;
 UPDATE attachment_upload_intents SET display_name='Forbidden' WHERE id='04300000-0000-0000-0000-000000000052';
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Foreign upload updated'; END IF;
 SELECT * INTO STRICT fixture FROM attachment_upload_intents;
 -- Test INSERT shape independently from the immutable UPDATE guard.
 FOREACH mutation IN ARRAY ARRAY[
  '{"expected_size_bytes":0}','{"expected_size_bytes":1073741825}',
  '{"expected_sha256":null}','{"expected_sha256":"INVALID"}',
  '{"original_card_version":0}','{"retry_key":"00000000-0000-0000-0000-000000000000"}',
  '{"display_name":""}','{"display_name":"bad\nname"}',
  '{"status":"STORED"}','{"status":"WRITING"}','{"status":"ABANDONED"}',
  '{"stored_at":"2026-01-01T00:00:00Z"}','{"verified_mime_type":"image/png"}',
  '{"version":2}','{"created_at":"infinity"}',
  '{"expires_at":"infinity"}','{"expires_at":"2020-01-01T00:00:00Z"}'
 ] LOOP
  candidate=jsonb_populate_record(fixture,jsonb_build_object('id',gen_random_uuid(),'retry_key',gen_random_uuid())||mutation::jsonb);
  BEGIN
   INSERT INTO attachment_upload_intents SELECT (candidate).*;
   RAISE EXCEPTION 'Invalid upload insert accepted: %',mutation;
  EXCEPTION WHEN check_violation OR not_null_violation THEN NULL; END;
 END LOOP;
 FOREACH mutation IN ARRAY ARRAY[
  'display_name=''Changed''','expected_size_bytes=129','expected_sha256=repeat(''b'',64)',
  'retry_key=gen_random_uuid()','original_card_version=2','expires_at=expires_at+interval ''1 second''',
  'created_at=created_at-interval ''1 second''','card_id=gen_random_uuid()',
  'uploader_id=gen_random_uuid()','tenant_id=gen_random_uuid()',
  'version=2','status=''PUBLISHED''','status=''RECONCILE'''
 ] LOOP
  BEGIN
   EXECUTE 'UPDATE attachment_upload_intents SET '||mutation||' WHERE id=''04300000-0000-0000-0000-000000000051''';
   RAISE EXCEPTION 'Invalid upload update accepted: %',mutation;
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 BEGIN
  candidate=fixture; candidate.id=gen_random_uuid();
  INSERT INTO attachment_upload_intents SELECT (candidate).*;
  RAISE EXCEPTION 'Retry identity was reused';
 EXCEPTION WHEN unique_violation THEN NULL; END;
 BEGIN
  candidate=fixture; candidate.id=gen_random_uuid(); candidate.retry_key=gen_random_uuid(); candidate.tenant_id='04300000-0000-0000-0000-000000000002';
  INSERT INTO attachment_upload_intents SELECT (candidate).*;
  RAISE EXCEPTION 'Cross-tenant upload inserted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  candidate=fixture; candidate.id=gen_random_uuid(); candidate.retry_key=gen_random_uuid(); candidate.card_id='04300000-0000-0000-0000-000000000032';
  INSERT INTO attachment_upload_intents SELECT (candidate).*;
  RAISE EXCEPTION 'Foreign Card upload inserted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
 BEGIN
  candidate=fixture; candidate.id=gen_random_uuid(); candidate.retry_key=gen_random_uuid(); candidate.uploader_id='04300000-0000-0000-0000-000000000042';
  INSERT INTO attachment_upload_intents SELECT (candidate).*;
  RAISE EXCEPTION 'Foreign uploader inserted';
 EXCEPTION WHEN foreign_key_violation THEN NULL; END;
END $$;
UPDATE attachment_upload_intents SET status='WRITING',write_lease_id='04300000-0000-0000-0000-000000000071',write_lease_until=created_at+interval '5 minutes',version=2;
DO $$ DECLARE mutation text; BEGIN
 FOREACH mutation IN ARRAY ARRAY[
  'write_lease_id=gen_random_uuid(),write_lease_until=write_lease_until+interval ''1 minute''',
  'write_lease_until=write_lease_until-interval ''1 minute''',
  'write_lease_until=created_at+interval ''11 minutes''',
  'status=''ABANDONED'',abandoned_at=updated_at,write_lease_id=NULL,write_lease_until=NULL',
  'status=''STORED'',stored_at=created_at+interval ''5 minutes'',updated_at=created_at+interval ''5 minutes'',verified_mime_type=''image/png'',write_lease_id=NULL,write_lease_until=NULL'
 ] LOOP
  BEGIN
   EXECUTE 'UPDATE attachment_upload_intents SET version=version+1,'||mutation;
   RAISE EXCEPTION 'Unsafe writer transition accepted: %',mutation;
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
END $$;
UPDATE attachment_upload_intents SET write_lease_until=created_at+interval '6 minutes',version=3;
UPDATE attachment_upload_intents SET status='RECONCILE',write_lease_id=NULL,write_lease_until=NULL,version=4;
UPDATE attachment_upload_intents SET status='PREPARED',version=5;
UPDATE attachment_upload_intents SET status='WRITING',write_lease_id='04300000-0000-0000-0000-000000000072',write_lease_until=created_at+interval '5 minutes',version=6;
UPDATE attachment_upload_intents SET status='STORED',write_lease_id=NULL,write_lease_until=NULL,verified_mime_type='image/png',stored_at=created_at+interval '1 minute',updated_at=created_at+interval '1 minute',version=7;
DO $$ BEGIN
 BEGIN
  UPDATE attachment_upload_intents SET status='PUBLISHED',published_attachment_id=id,published_at=updated_at,version=8;
  RAISE EXCEPTION 'Upload published without matching metadata';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,mime_type,size_bytes,sha256,storage_key,scan_status,created_at,updated_at)
SELECT id,tenant_id,card_id,uploader_id,'FILE',display_name,verified_mime_type,expected_size_bytes,expected_sha256,
 'attachments/'||replace(tenant_id::text,'-','')||'/'||replace(id::text,'-',''),'PENDING',stored_at,stored_at FROM attachment_upload_intents;
UPDATE attachment_upload_intents SET status='PUBLISHED',published_attachment_id=id,published_at=updated_at,version=8;
UPDATE attachment_upload_intents SET status=status;
DO $$ BEGIN
 IF (SELECT count(*) FROM attachment_upload_intents WHERE status='PUBLISHED' AND version=8)<>1 THEN RAISE EXCEPTION 'Publication revision changed unexpectedly'; END IF;
 BEGIN
  UPDATE attachment_upload_intents SET version=9;
  RAISE EXCEPTION 'Terminal published intent mutated';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  DELETE FROM attachment_upload_intents;
  RAISE EXCEPTION 'Runtime deleted upload retry identity';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
INSERT INTO attachment_upload_intents(id,tenant_id,card_id,uploader_id,retry_key,original_card_version,display_name,expected_size_bytes,expected_sha256,created_at,updated_at,expires_at)
SELECT gen_random_uuid(),tenant_id,card_id,uploader_id,gen_random_uuid(),original_card_version,display_name,expected_size_bytes,expected_sha256,created_at,created_at,expires_at FROM attachment_upload_intents;
UPDATE attachment_upload_intents SET status='ABANDONED',abandoned_at=expires_at,updated_at=expires_at,version=2 WHERE status='PREPARED';
DO $$ BEGIN
 BEGIN
  UPDATE attachment_upload_intents SET status='PREPARED',abandoned_at=NULL,version=3 WHERE status='ABANDONED';
  RAISE EXCEPTION 'Abandoned upload resurrected';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
-- An unknown provider outcome can be recorded after expiry, but can never
-- become permission to start another write or publish the original claim.
INSERT INTO attachment_upload_intents(id,tenant_id,card_id,uploader_id,retry_key,original_card_version,display_name,expected_size_bytes,expected_sha256,created_at,updated_at,expires_at)
SELECT gen_random_uuid(),tenant_id,card_id,uploader_id,gen_random_uuid(),original_card_version,display_name,expected_size_bytes,expected_sha256,created_at,created_at,expires_at
 FROM attachment_upload_intents WHERE status='PUBLISHED';
UPDATE attachment_upload_intents SET status='WRITING',write_lease_id=gen_random_uuid(),write_lease_until=created_at+interval '5 minutes',version=2 WHERE status='PREPARED';
UPDATE attachment_upload_intents SET status='RECONCILE',write_lease_id=NULL,write_lease_until=NULL,updated_at=expires_at,version=3 WHERE status='WRITING';
DO $$ BEGIN
 BEGIN
  UPDATE attachment_upload_intents SET status='PREPARED',version=4 WHERE status='RECONCILE';
  RAISE EXCEPTION 'Expired unknown upload allowed a new writer';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE attachment_upload_intents SET status='STORED',stored_at=updated_at,verified_mime_type='image/png',version=4 WHERE status='RECONCILE';
  RAISE EXCEPTION 'Expired unknown upload became stored';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;
UPDATE attachment_upload_intents SET status='ABANDONED',abandoned_at=updated_at,version=4 WHERE status='RECONCILE';
ROLLBACK;
