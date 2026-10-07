BEGIN;
LOCK TABLE invitations IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE invitation_recipient_proofs IN SHARE ROW EXCLUSIVE MODE;
LOCK TABLE invitation_recipient_events IN SHARE ROW EXCLUSIVE MODE;
-- Capture is earlier than audit publication. A never-published fixture/source
-- has no retained history; its proof may retire only with its parent. Published
-- proofs and their actual source identities remain immutable and FK-protected.
ALTER TABLE invitation_recipient_proofs
 DROP CONSTRAINT invitation_recipient_proofs_invitation_id_tenant_id_fkey,
 ADD CONSTRAINT invitation_recipient_proofs_invitation_id_tenant_id_fkey
 FOREIGN KEY(invitation_id,tenant_id) REFERENCES invitations(id,tenant_id) ON DELETE CASCADE;
CREATE FUNCTION protect_invitation_recipient_proof() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
BEGIN
 IF TG_OP='DELETE' AND pg_trigger_depth()>1 AND NOT EXISTS(
  SELECT 1 FROM public.invitation_recipient_events WHERE tenant_id=OLD.tenant_id
   AND entity_id=OLD.invitation_id AND entity_version=OLD.entity_version) THEN
  RETURN OLD;
 END IF;
 RAISE EXCEPTION 'Invitation recipient proof is immutable' USING ERRCODE='23514';
END $$;
REVOKE ALL ON FUNCTION protect_invitation_recipient_proof() FROM PUBLIC;
DROP TRIGGER invitation_recipient_proof_immutable ON invitation_recipient_proofs;
CREATE TRIGGER invitation_recipient_proof_immutable BEFORE UPDATE OR DELETE ON invitation_recipient_proofs
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_recipient_proof();
INSERT INTO schema_migrations(version) VALUES('103_invitation_recipient_unpublished_cleanup');
COMMIT;
