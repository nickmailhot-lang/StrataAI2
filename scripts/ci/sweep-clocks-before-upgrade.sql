INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('12900000-0000-0000-0000-000000000101','Legacy checkpoint',now(),now()),
 ('12900000-0000-0000-0000-000000000102','Legacy empty checkpoint',now(),now());
INSERT INTO attachment_preview_sweeps(tenant_id,cursor_created_at,cursor_id) VALUES
 ('12900000-0000-0000-0000-000000000101','2040-01-01','12900000-0000-0000-0000-000000000103'),
 ('12900000-0000-0000-0000-000000000102',NULL,NULL);
INSERT INTO attachment_scan_sweeps SELECT * FROM attachment_preview_sweeps
 WHERE tenant_id IN ('12900000-0000-0000-0000-000000000101','12900000-0000-0000-0000-000000000102');
CREATE TABLE sweep_clock_upgrade_fixture AS
 SELECT 'attachment_preview_sweeps'::text AS relation,to_jsonb(s) AS body FROM attachment_preview_sweeps s
 WHERE tenant_id IN ('12900000-0000-0000-0000-000000000101','12900000-0000-0000-0000-000000000102')
 UNION ALL
 SELECT 'attachment_scan_sweeps',to_jsonb(s) FROM attachment_scan_sweeps s
 WHERE tenant_id IN ('12900000-0000-0000-0000-000000000101','12900000-0000-0000-0000-000000000102');
