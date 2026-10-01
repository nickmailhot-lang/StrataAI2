BEGIN;
CREATE INDEX ix_invitations_tenant_cursor ON invitations(tenant_id,id);
INSERT INTO schema_migrations(version) VALUES ('024_invitation_history');
COMMIT;
