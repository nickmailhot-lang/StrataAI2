DO $$ BEGIN
 IF (SELECT count(*) FROM deletion_progress_clock_upgrade_fixture)<>(SELECT count(*) FROM organization_deletion_progress)
 OR EXISTS(SELECT 1 FROM deletion_progress_clock_upgrade_fixture f LEFT JOIN organization_deletion_progress p USING(tenant_id)
  WHERE p.tenant_id IS NULL OR to_jsonb(p)-'created_at' IS DISTINCT FROM f.original OR p.created_at IS DISTINCT FROM f.source_created) THEN
  RAISE EXCEPTION 'Deletion progress upgrade changed checkpoint history or source creation';
 END IF;
END $$;
DROP TABLE deletion_progress_clock_upgrade_fixture;
BEGIN;
DO $$ DECLARE original_created timestamptz; original_updated timestamptz;
BEGIN
 SELECT created_at,updated_at INTO STRICT original_created,original_updated FROM organization_deletion_progress
  WHERE tenant_id='f2400000-0000-4000-8000-000000000010';
 BEGIN
  UPDATE organization_deletion_progress SET created_at=created_at+interval '1 second' WHERE tenant_id='f2400000-0000-4000-8000-000000000010';
  RAISE EXCEPTION 'Deletion progress creation replacement was admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE organization_deletion_progress SET updated_at=updated_at-interval '1 second' WHERE tenant_id='f2400000-0000-4000-8000-000000000010';
  RAISE EXCEPTION 'Deletion progress update clock regressed';
 EXCEPTION WHEN check_violation THEN NULL; END;
 BEGIN
  UPDATE organization_deletion_progress SET updated_at='infinity' WHERE tenant_id='f2400000-0000-4000-8000-000000000010';
  RAISE EXCEPTION 'Deletion progress infinite clock was admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 UPDATE organization_deletion_progress SET updated_at=updated_at WHERE tenant_id='f2400000-0000-4000-8000-000000000010';
 IF NOT EXISTS(SELECT 1 FROM organization_deletion_progress WHERE tenant_id='f2400000-0000-4000-8000-000000000010'
  AND created_at=original_created AND updated_at=original_updated) THEN RAISE EXCEPTION 'No-op deletion progress changed clocks'; END IF;
 DELETE FROM organization_deletion_progress WHERE tenant_id='f2400000-0000-4000-8000-000000000010';
 BEGIN
  INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase,created_at)
   VALUES('f2400000-0000-4000-8000-000000000010','f2400000-0000-4000-8000-000000000012','f2400000-0000-4000-8000-000000000012','ATTACHMENTS',clock_timestamp());
  RAISE EXCEPTION 'Deletion progress invented creation was admitted';
 EXCEPTION WHEN check_violation THEN NULL; END;
 INSERT INTO organization_deletion_progress(tenant_id,request_id,step_id,phase)
  VALUES('f2400000-0000-4000-8000-000000000010','f2400000-0000-4000-8000-000000000012','f2400000-0000-4000-8000-000000000012','ATTACHMENTS');
 IF NOT EXISTS(SELECT 1 FROM organization_deletion_progress WHERE tenant_id='f2400000-0000-4000-8000-000000000010'
  AND created_at=original_created AND updated_at>=created_at) THEN RAISE EXCEPTION 'New deletion progress lost exact request creation'; END IF;
END $$;
ROLLBACK;
