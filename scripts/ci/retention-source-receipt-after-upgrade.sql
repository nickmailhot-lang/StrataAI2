BEGIN;
DO $$
BEGIN
 IF NOT EXISTS(SELECT FROM retention_source_receipt_upgrade_fixture) THEN
  RAISE EXCEPTION 'Receipt upgrade fixture lacked historical classifications'; END IF;
 IF EXISTS(SELECT FROM record_retention_source_receipts)
  OR EXISTS(SELECT FROM record_retention_sources WHERE command_key IS NOT NULL)
  OR (SELECT count(*) FROM record_retention_sources)<>(SELECT count(*) FROM retention_source_receipt_upgrade_fixture)
  OR EXISTS(SELECT FROM retention_source_receipt_upgrade_fixture old
    WHERE NOT EXISTS(SELECT FROM record_retention_sources s JOIN audit_events a ON(a.tenant_id,a.id)=(s.tenant_id,s.event_id)
      WHERE to_jsonb(s)-'command_key'=old.source AND to_jsonb(a)=old.audit)) THEN
  RAISE EXCEPTION 'Receipt upgrade invented command evidence or rewrote historical source/audit'; END IF;
 IF NOT EXISTS(SELECT FROM pg_class WHERE oid='record_retention_source_receipts'::regclass
    AND relrowsecurity AND relforcerowsecurity)
  OR NOT EXISTS(SELECT FROM pg_constraint WHERE conrelid='record_retention_sources'::regclass
    AND conname='retention_source_command_receipt' AND condeferrable AND condeferred) THEN
  RAISE EXCEPTION 'Receipt graph lacks forced RLS or atomic commit binding'; END IF;
END $$;
ROLLBACK;
