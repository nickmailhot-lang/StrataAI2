\set ON_ERROR_STOP on
BEGIN;
CREATE ROLE strataai_attachment_storage_ci NOSUPERUSER NOBYPASSRLS NOLOGIN;
GRANT USAGE ON SCHEMA public TO strataai_attachment_storage_ci;
GRANT SELECT,INSERT,UPDATE ON attachments TO strataai_attachment_storage_ci;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('04100000-0000-0000-0000-000000000001','Checklist A',now(),now()),
 ('04100000-0000-0000-0000-000000000002','Checklist B',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('04100000-0000-0000-0000-000000000041','attachment-storage@example.test','ATTACHMENT-STORAGE@EXAMPLE.TEST','Checklist fixture','ACTIVE',true,'unused-checklist-hash',now(),now()),
 ('04100000-0000-0000-0000-000000000042','attachment-foreign@example.test','ATTACHMENT-FOREIGN@EXAMPLE.TEST','Foreign fixture','ACTIVE',true,'unused-checklist-hash',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'04100000-0000-0000-0000-000000000001','04100000-0000-0000-0000-000000000041','MEMBER','ACTIVE'),
 (gen_random_uuid(),'04100000-0000-0000-0000-000000000002','04100000-0000-0000-0000-000000000042','MEMBER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('04100000-0000-0000-0000-000000000011','04100000-0000-0000-0000-000000000001','A Board',now(),now()),
 ('04100000-0000-0000-0000-000000000012','04100000-0000-0000-0000-000000000002','B Board',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('04100000-0000-0000-0000-000000000021','04100000-0000-0000-0000-000000000001','04100000-0000-0000-0000-000000000011','A List','500000000000000000000000000000',now(),now()),
 ('04100000-0000-0000-0000-000000000022','04100000-0000-0000-0000-000000000002','04100000-0000-0000-0000-000000000012','B List','500000000000000000000000000000',now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('04100000-0000-0000-0000-000000000031','04100000-0000-0000-0000-000000000001','04100000-0000-0000-0000-000000000011','04100000-0000-0000-0000-000000000021','A Card','500000000000000000000000000000',now(),now()),
 ('04100000-0000-0000-0000-000000000032','04100000-0000-0000-0000-000000000002','04100000-0000-0000-0000-000000000012','04100000-0000-0000-0000-000000000022','B Card','500000000000000000000000000000',now(),now());
INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at) VALUES
 ('04100000-0000-0000-0000-000000000051','04100000-0000-0000-0000-000000000001','04100000-0000-0000-0000-000000000031','04100000-0000-0000-0000-000000000041','URL','External link','https://example.test/path','NOT_APPLICABLE',now(),now()),
 ('04100000-0000-0000-0000-000000000053','04100000-0000-0000-0000-000000000002','04100000-0000-0000-0000-000000000032','04100000-0000-0000-0000-000000000042','URL','Foreign link','https://example.test/foreign','NOT_APPLICABLE',now(),now());
INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,mime_type,size_bytes,storage_key,scan_status,created_at,updated_at,sha256) VALUES
 ('04100000-0000-0000-0000-000000000052','04100000-0000-0000-0000-000000000001','04100000-0000-0000-0000-000000000031','04100000-0000-0000-0000-000000000041','FILE','Quarantined image','image/png',128,'ci/quarantine/unique-object','PENDING',now(),now(),repeat('a',64));
SET LOCAL ROLE strataai_attachment_storage_ci;
SELECT set_config('app.tenant_id','04100000-0000-0000-0000-000000000001',true);
DO $$ DECLARE affected integer; mutation text; BEGIN
 IF (SELECT count(*) FROM attachments)<>2 THEN RAISE EXCEPTION 'Attachment tenant read widened'; END IF;
 UPDATE attachments SET display_name='Forbidden' WHERE id='04100000-0000-0000-0000-000000000053';
 GET DIAGNOSTICS affected=ROW_COUNT;
 IF affected<>0 THEN RAISE EXCEPTION 'Foreign attachment was updated'; END IF;
 BEGIN
  UPDATE attachments SET tenant_id='04100000-0000-0000-0000-000000000002' WHERE id='04100000-0000-0000-0000-000000000051';
  RAISE EXCEPTION 'Cross-tenant attachment write accepted';
 EXCEPTION WHEN insufficient_privilege OR check_violation THEN NULL; END;
 BEGIN
  INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
   VALUES(gen_random_uuid(),'04100000-0000-0000-0000-000000000002','04100000-0000-0000-0000-000000000032',
    '04100000-0000-0000-0000-000000000042','URL','Forbidden tenant','https://example.test/','NOT_APPLICABLE',now(),now());
  RAISE EXCEPTION 'Cross-tenant attachment insert accepted';
 EXCEPTION WHEN insufficient_privilege THEN NULL; END;
 BEGIN
  UPDATE attachments SET card_id='04100000-0000-0000-0000-000000000032' WHERE id='04100000-0000-0000-0000-000000000051';
  RAISE EXCEPTION 'Cross-tenant Card attachment accepted';
 EXCEPTION WHEN foreign_key_violation OR check_violation THEN NULL; END;
 BEGIN
  UPDATE attachments SET uploader_id='04100000-0000-0000-0000-000000000042' WHERE id='04100000-0000-0000-0000-000000000051';
  RAISE EXCEPTION 'Cross-tenant uploader accepted';
 EXCEPTION WHEN foreign_key_violation OR check_violation THEN NULL; END;
 FOREACH mutation IN ARRAY ARRAY[
   'mime_type=NULL','size_bytes=NULL','storage_key=NULL','size_bytes=0','size_bytes=-1',
   'sha256=NULL','sha256=''invalid''','sha256=repeat(''A'',64)','sha256=repeat(''b'',64)',
   'size_bytes=129','storage_key=''ci/other-object''','mime_type=''image/jpeg''',
   'mime_type=''image/png; injected''','mime_type=''IMAGE/PNG''',
   'storage_key=''/absolute''','storage_key=''../escape''','storage_key=''a/../b''',
   'storage_key=''a//b''','storage_key=''a/''','storage_key=''''',
   'storage_key=repeat(''x'',513)','storage_key=chr(92)||''escape''',
   'url=''https://example.test/''','scan_status=''NOT_APPLICABLE''',
   'scan_status=''CLEAN''','scan_status=''REJECTED''','scan_status=''FAILED''',
   'scanned_at=now()','version=0','display_name=repeat(''x'',256)',
   'display_name=chr(10)||''bad''','display_name=''''',
   'updated_at=created_at-interval ''1 second''',
   'deleted_at=created_at-interval ''1 second''',
   'deleted_at=updated_at+interval ''1 second''',
   'scan_status=''CLEAN'',scanned_at=updated_at+interval ''1 second'''
 ] LOOP
  BEGIN
   EXECUTE 'UPDATE attachments SET '||mutation||' WHERE id=''04100000-0000-0000-0000-000000000052''';
   RAISE EXCEPTION 'Invalid file attachment shape accepted: %',mutation;
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 FOREACH mutation IN ARRAY ARRAY[
   'url=NULL','url=''javascript:alert(1)''','url=''https://user:pass@example.test/''',
   'url=repeat(''x'',2049)','url=''https://example.test/''||chr(10)',
   'storage_key=''unexpected''','size_bytes=100','mime_type=''image/png''',
   'sha256=repeat(''a'',64)',
   'scan_status=''PENDING''','scanned_at=now()'
 ] LOOP
  BEGIN
   EXECUTE 'UPDATE attachments SET '||mutation||' WHERE id=''04100000-0000-0000-0000-000000000051''';
   RAISE EXCEPTION 'Invalid URL attachment shape accepted: %',mutation;
  EXCEPTION WHEN check_violation THEN NULL; END;
 END LOOP;
 BEGIN
  INSERT INTO attachments SELECT gen_random_uuid(),tenant_id,card_id,uploader_id,kind,display_name,mime_type,size_bytes,'ci/no-digest/'||gen_random_uuid(),url,scan_status,scanned_at,created_at,updated_at,version,deleted_at,NULL
    FROM attachments WHERE id='04100000-0000-0000-0000-000000000052';
  RAISE EXCEPTION 'New file without measured digest accepted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  INSERT INTO attachments SELECT gen_random_uuid(),tenant_id,card_id,uploader_id,kind,display_name,mime_type,size_bytes,storage_key,url,scan_status,scanned_at,created_at,updated_at,version,deleted_at,sha256
    FROM attachments WHERE id='04100000-0000-0000-0000-000000000052';
  RAISE EXCEPTION 'Duplicate binary storage key accepted';
 EXCEPTION WHEN unique_violation THEN NULL; END;
END $$;
UPDATE attachments SET scan_status='CLEAN',scanned_at=now(),updated_at=now(),version=version+1
 WHERE id='04100000-0000-0000-0000-000000000052';
UPDATE attachments SET lifecycle_state='ARCHIVED',archived_at=now(),updated_at=now(),version=version+1
 WHERE id='04100000-0000-0000-0000-000000000052';
UPDATE attachments SET lifecycle_state='DELETED',deleted_by=uploader_id,deleted_at=now(),updated_at=now(),version=version+1
 WHERE id='04100000-0000-0000-0000-000000000052';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM attachments WHERE id='04100000-0000-0000-0000-000000000052'
   AND scan_status='CLEAN' AND lifecycle_state='DELETED' AND archived_at IS NOT NULL AND deleted_by=uploader_id
   AND deleted_at IS NOT NULL AND storage_key='ci/quarantine/unique-object' AND sha256=repeat('a',64) AND version=4)
 THEN RAISE EXCEPTION 'Attachment tombstone discarded scan/storage history'; END IF;
 IF (SELECT count(*) FROM attachments WHERE deleted_at IS NULL)<>1 THEN RAISE EXCEPTION 'Attachment active read includes tombstone'; END IF;
END $$;
SELECT set_config('app.tenant_id','04100000-0000-0000-0000-000000000002',true);
DO $$ BEGIN
 IF (SELECT count(*) FROM attachments)<>1 OR (SELECT display_name FROM attachments)<>'Foreign link'
 THEN RAISE EXCEPTION 'Foreign attachment changed or second scope widened'; END IF;
END $$;
SELECT set_config('app.tenant_id','',true);
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM attachments) THEN RAISE EXCEPTION 'Missing tenant exposed attachment metadata'; END IF;
END $$;
RESET ROLE;
ROLLBACK;
\echo 'Attachment metadata: tenant RLS, composite Card/uploader identity, variant/quarantine shape, unique keys, timestamps and retained tombstones passed.'
