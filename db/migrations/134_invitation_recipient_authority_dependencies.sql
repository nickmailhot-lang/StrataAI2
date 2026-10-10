BEGIN;
ALTER TABLE invitation_recipient_events ADD CONSTRAINT invitation_recipient_event_tenant_identity UNIQUE(tenant_id,event_id);
CREATE TABLE invitation_recipient_authority_dependencies (
 tenant_id uuid NOT NULL,recipient_event_id uuid NOT NULL,source_event_id uuid NOT NULL,
 PRIMARY KEY(tenant_id,recipient_event_id,source_event_id),
 FOREIGN KEY(tenant_id,recipient_event_id) REFERENCES invitation_recipient_events(tenant_id,event_id) ON DELETE RESTRICT,
 FOREIGN KEY(tenant_id,source_event_id) REFERENCES invitation_recipient_authority_sources(tenant_id,event_id) ON DELETE RESTRICT
);
ALTER TABLE invitation_recipient_authority_dependencies ENABLE ROW LEVEL SECURITY;
ALTER TABLE invitation_recipient_authority_dependencies FORCE ROW LEVEL SECURITY;
CREATE POLICY invitation_recipient_dependency_tenant ON invitation_recipient_authority_dependencies
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE TRIGGER invitation_recipient_dependency_history BEFORE UPDATE OR DELETE ON invitation_recipient_authority_dependencies
 FOR EACH ROW EXECUTE FUNCTION protect_invitation_authority_history();

-- Both source rows must belong to this transaction. Correlation IDs, wall
-- clocks and previously committed history cannot manufacture causality.
CREATE FUNCTION bind_invitation_recipient_authority_dependency(p_tenant uuid,p_actor uuid,p_invitation uuid,p_source uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE recipient uuid;
BEGIN
 IF p_tenant IS NULL OR p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid THEN RETURN false; END IF;
 SELECT e.event_id INTO recipient FROM public.invitation_recipient_events e
 JOIN public.invitation_recipient_proofs p ON (p.tenant_id,p.invitation_id,p.entity_version)=(e.tenant_id,e.entity_id,e.entity_version)
 JOIN public.invitations i ON (i.tenant_id,i.id,i.version)=(e.tenant_id,e.entity_id,e.entity_version)
 JOIN public.work_events w ON w.tenant_id=e.tenant_id AND w.event_id=p_source
 JOIN public.invitation_recipient_authority_sources s ON s.tenant_id=w.tenant_id AND s.event_id=w.event_id AND s.work_event_id=w.event_id
 WHERE e.tenant_id=p_tenant AND e.entity_id=p_invitation AND e.actor_id=p_actor
  AND e.event_type='INVITATION_ACCEPTED' AND p.event_type='INVITATION_ACCEPTED'
  AND p.accepted_actor_id=p_actor AND i.accepted_by_user_id=p_actor AND i.accepted_at IS NOT NULL AND i.revoked_at IS NULL
  AND p.board_id=i.target_board_id AND p.board_id=w.board_id AND w.entity_type='Board' AND w.entity_id=w.board_id
  AND w.actor_id=p_actor AND w.event_type IN ('BOARD_MEMBER_ADDED','BOARD_MEMBER_ROLE_CHANGED')
  AND e.xmin=pg_current_xact_id()::xid AND w.xmin=pg_current_xact_id()::xid;
 IF recipient IS NULL THEN RETURN false; END IF;
 INSERT INTO public.invitation_recipient_authority_dependencies(tenant_id,recipient_event_id,source_event_id)
 VALUES(p_tenant,recipient,p_source) ON CONFLICT DO NOTHING;
 RETURN true;
END $$;
REVOKE ALL ON FUNCTION bind_invitation_recipient_authority_dependency(uuid,uuid,uuid,uuid) FROM PUBLIC;

-- Returns only readiness of the current admitted recipient's own sequence.
-- Domain references and authority history remain private to this capability.
CREATE FUNCTION invitation_recipient_event_ready(p_sequence bigint)
RETURNS boolean LANGUAGE sql STABLE SECURITY DEFINER SET search_path=pg_catalog,public AS $$
 SELECT current_setting('app.route_kind',true)='INVITATION_RECIPIENT' AND EXISTS(
  SELECT 1 FROM public.invitation_recipient_events e
  WHERE e.email_normalized=current_setting('app.route_key',true) AND e.sequence=p_sequence
   AND NOT EXISTS(SELECT 1 FROM public.invitation_recipient_authority_dependencies d
    WHERE d.tenant_id=e.tenant_id AND d.recipient_event_id=e.event_id
     AND NOT EXISTS(SELECT 1 FROM public.invitation_recipient_authority_effects f
      WHERE f.tenant_id=d.tenant_id AND f.source_event_id=d.source_event_id AND f.email_normalized=e.email_normalized)));
$$;
REVOKE ALL ON FUNCTION invitation_recipient_event_ready(bigint) FROM PUBLIC;
REVOKE ALL ON invitation_recipient_authority_dependencies FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('134_invitation_recipient_authority_dependencies');
COMMIT;
