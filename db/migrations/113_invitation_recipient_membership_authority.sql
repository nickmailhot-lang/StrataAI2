BEGIN;
LOCK TABLE work_events,invitation_recipient_authority_sources IN SHARE ROW EXCLUSIVE MODE;
-- Match the owning API's current membership event names. Preserve the legacy
-- update family, canonical references, fixed-cutoff pages and leased dispatcher.
-- Historical source/event identities are not rewritten or synthesized.
CREATE OR REPLACE VIEW invitation_recipient_authority_source_rows AS
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN organization_metadata_events e
 ON e.tenant_id=s.tenant_id AND e.event_id=s.metadata_event_id
 WHERE e.event_type IN ('ORGANIZATION_UPDATED','ORGANIZATION_MEMBER_REMOVED')
 UNION ALL
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN work_events e
 ON e.tenant_id=s.tenant_id AND e.event_id=s.work_event_id
 WHERE e.entity_type='Board' AND e.entity_id=e.board_id
 AND e.event_type IN ('BOARD_UPDATED','BOARD_VISIBILITY_CHANGED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED','BOARD_MEMBER_UPDATED','BOARD_MEMBER_ADDED','BOARD_MEMBER_ROLE_CHANGED','BOARD_MEMBER_REMOVED')
 UNION ALL
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN invitation_recipient_organization_lifecycle_sources e
 ON e.tenant_id=s.tenant_id AND e.event_id=s.organization_lifecycle_source_id
 UNION ALL
 SELECT s.tenant_id,s.event_id,e.actor_id,e.correlation_id,e.event_type,e.entity_type,e.entity_id,e.entity_version,e.created_at
 FROM invitation_recipient_authority_sources s JOIN invitation_issuer_authority_sources e ON e.event_id=s.issuer_source_event_id;
CREATE OR REPLACE FUNCTION publish_invitation_recipient_authority() RETURNS trigger
 LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE job uuid:=gen_random_uuid(); start_id uuid:='00000000-0000-0000-0000-000000000000';
BEGIN
 -- Broad tenant invalidation also covers accepted rows whose cached links/names
 -- must be withdrawn. Present grant admission remains protected discovery's job.
 IF TG_TABLE_NAME='organization_metadata_events' THEN
  IF NEW.event_type NOT IN ('ORGANIZATION_UPDATED','ORGANIZATION_MEMBER_REMOVED') THEN RETURN NEW; END IF;
  INSERT INTO public.invitation_recipient_authority_sources(tenant_id,event_id,metadata_event_id)
  VALUES(NEW.tenant_id,NEW.event_id,NEW.event_id);
 ELSIF TG_TABLE_NAME='invitation_recipient_organization_lifecycle_sources' THEN
  INSERT INTO public.invitation_recipient_authority_sources(tenant_id,event_id,organization_lifecycle_source_id)
  VALUES(NEW.tenant_id,NEW.event_id,NEW.event_id);
 ELSE
  IF NEW.entity_type<>'Board' OR NEW.entity_id<>NEW.board_id OR NEW.event_type NOT IN
   ('BOARD_UPDATED','BOARD_VISIBILITY_CHANGED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED','BOARD_MEMBER_UPDATED','BOARD_MEMBER_ADDED','BOARD_MEMBER_ROLE_CHANGED','BOARD_MEMBER_REMOVED') THEN RETURN NEW; END IF;
  INSERT INTO public.invitation_recipient_authority_sources(tenant_id,event_id,work_event_id)
  VALUES(NEW.tenant_id,NEW.event_id,NEW.event_id);
 END IF;
 INSERT INTO public.background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata)
 VALUES(job,NEW.tenant_id,'INVITATION_RECIPIENT_AUTHORITY_PAGE',
  'invitation-authority/'||replace(NEW.event_id::text,'-','')||'/'||replace(start_id::text,'-',''),
  NEW.actor_id,'invitation-recipient-authority',NEW.correlation_id,jsonb_build_object('eventId',NEW.event_id));
 INSERT INTO public.invitation_recipient_authority_pages(tenant_id,job_id,source_event_id,after_id,after_created_at)
 VALUES(NEW.tenant_id,job,NEW.event_id,start_id,'-infinity');
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION publish_invitation_recipient_authority() FROM PUBLIC;

INSERT INTO schema_migrations(version) VALUES('113_invitation_recipient_membership_authority');
COMMIT;
