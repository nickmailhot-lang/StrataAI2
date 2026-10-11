-- Retain every pre-upgrade source/audit field for a complete upgrade comparison.
CREATE TABLE retention_source_receipt_upgrade_fixture AS
 SELECT to_jsonb(s) AS source, to_jsonb(a) AS audit
 FROM record_retention_sources s JOIN audit_events a ON(a.tenant_id,a.id)=(s.tenant_id,s.event_id);
