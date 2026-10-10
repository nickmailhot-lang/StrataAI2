BEGIN;
LOCK TABLE organization_configuration_events IN ACCESS EXCLUSIVE MODE;
-- The transaction-wide exclusive lock protects the backfill while the old
-- mutation guard is detached; rollback restores its original definition.
DROP TRIGGER organization_configuration_delivery_source ON organization_configuration_events;
ALTER TABLE organization_members ADD CONSTRAINT organization_members_configuration_source_identity
 UNIQUE(tenant_id,id,user_id);

-- Old sources did not capture a caption or administrative capacity. Preserve
-- that absence instead of borrowing a current name/role and inventing history.
ALTER TABLE organization_configuration_events
 ADD COLUMN activity_actor_label text,
 ADD COLUMN acting_capacity text,
 ADD COLUMN source_membership_id uuid,
 ADD COLUMN source_membership_version bigint;
UPDATE organization_configuration_events
 SET activity_actor_label='Historical actor '||actor_id::text,acting_capacity='UNRECORDED';
ALTER TABLE organization_configuration_events
 ALTER COLUMN activity_actor_label SET NOT NULL,
 ALTER COLUMN acting_capacity SET NOT NULL,
 ADD CONSTRAINT organization_configuration_attribution_label CHECK
  (length(activity_actor_label) BETWEEN 1 AND 160 AND activity_actor_label !~ '[[:cntrl:]]'),
 ADD CONSTRAINT organization_configuration_attribution_capacity CHECK
  ((acting_capacity='UNRECORDED' AND source_membership_id IS NULL AND source_membership_version IS NULL)
   OR (acting_capacity IN ('ORGANIZATION_OWNER','ORGANIZATION_ADMIN')
    AND source_membership_id IS NOT NULL AND source_membership_id<>'00000000-0000-0000-0000-000000000000'::uuid
    AND source_membership_version IS NOT NULL AND source_membership_version>0)),
 ADD CONSTRAINT organization_configuration_attribution_membership
  FOREIGN KEY(tenant_id,source_membership_id,actor_id) REFERENCES organization_members(tenant_id,id,user_id) ON DELETE RESTRICT;
CREATE TRIGGER organization_configuration_delivery_source BEFORE INSERT OR UPDATE OR DELETE ON organization_configuration_events
 FOR EACH ROW EXECUTE FUNCTION guard_organization_configuration_delivery_source();

CREATE FUNCTION capture_organization_configuration_attribution() RETURNS trigger
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE caption text; source_role text; source_member uuid; source_version bigint;
BEGIN
 -- The existing delivery-source guard initializes actor_id and validates the
 -- exact immutable history/audit source before this alphabetically later trigger.
 SELECT u.display_name,m.role,m.id,m.version INTO caption,source_role,source_member,source_version
 FROM public.users u JOIN public.organization_members m ON m.user_id=u.id
 WHERE u.id=NEW.actor_id AND m.tenant_id=NEW.tenant_id AND u.status='ACTIVE'
  AND m.status='ACTIVE' AND m.role IN ('OWNER','ADMIN');
 IF source_member IS NULL OR source_version IS NULL OR source_version<1 THEN
  RAISE EXCEPTION 'Configuration source attribution is unavailable' USING ERRCODE='23514';
 END IF;
 IF caption IS NULL OR length(caption) NOT BETWEEN 1 AND 160 OR caption ~ '[[:cntrl:]]' THEN
  caption='Member '||NEW.actor_id::text;
 END IF;
 -- INSERT callers cannot supply their own historical caption or capacity.
 NEW.activity_actor_label=caption;
 NEW.acting_capacity=CASE source_role WHEN 'OWNER' THEN 'ORGANIZATION_OWNER' ELSE 'ORGANIZATION_ADMIN' END;
 NEW.source_membership_id=source_member;NEW.source_membership_version=source_version;
 RETURN NEW;
END $$;
CREATE TRIGGER organization_configuration_event_attribution BEFORE INSERT ON organization_configuration_events
 FOR EACH ROW EXECUTE FUNCTION capture_organization_configuration_attribution();

CREATE FUNCTION guard_organization_configuration_attribution() RETURNS trigger
LANGUAGE plpgsql SECURITY INVOKER SET search_path=pg_catalog AS $$
BEGIN
 -- Readiness and its generated clock are delivery state. Every source field,
 -- including future attribution columns, is immutable across Worker retries.
 IF (to_jsonb(NEW)-'ready_at'-'updated_at') IS DISTINCT FROM (to_jsonb(OLD)-'ready_at'-'updated_at') THEN
  RAISE EXCEPTION 'Configuration source attribution is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER organization_configuration_event_attribution_immutable BEFORE UPDATE ON organization_configuration_events
 FOR EACH ROW EXECUTE FUNCTION guard_organization_configuration_attribution();
REVOKE ALL ON FUNCTION capture_organization_configuration_attribution(),guard_organization_configuration_attribution() FROM PUBLIC;

INSERT INTO schema_migrations(version) VALUES('139_organization_configuration_attribution');
COMMIT;
