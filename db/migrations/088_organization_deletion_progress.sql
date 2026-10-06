BEGIN;
CREATE TABLE organization_deletion_requests (
 tenant_id uuid PRIMARY KEY REFERENCES organizations(id) ON DELETE RESTRICT,
 request_id uuid NOT NULL CHECK(request_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 accepted_version bigint NOT NULL CHECK(accepted_version>=2 AND accepted_version<9223372036854775807),
 correlation_id text NOT NULL CHECK(correlation_id ~ '^[A-Za-z0-9._-]{1,64}$'),
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 UNIQUE(tenant_id,request_id)
);
CREATE TABLE organization_deletion_progress (
 tenant_id uuid PRIMARY KEY,
 request_id uuid NOT NULL,
 step_id uuid NOT NULL CHECK(step_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 phase text NOT NULL CHECK(phase IN ('ATTACHMENTS','CARDS','LISTS','BOARDS','FINALIZE','COMPLETE')),
 after_id uuid CHECK(after_id IS NULL OR after_id<>'00000000-0000-0000-0000-000000000000'::uuid),
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
 completed_at timestamptz,
 FOREIGN KEY(tenant_id,request_id) REFERENCES organization_deletion_requests(tenant_id,request_id) ON DELETE RESTRICT,
 CHECK((phase='COMPLETE')=(completed_at IS NOT NULL))
);
ALTER TABLE organization_deletion_requests ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_deletion_requests FORCE ROW LEVEL SECURITY;
ALTER TABLE organization_deletion_progress ENABLE ROW LEVEL SECURITY;
ALTER TABLE organization_deletion_progress FORCE ROW LEVEL SECURITY;
CREATE POLICY organization_deletion_requests_tenant ON organization_deletion_requests
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE POLICY organization_deletion_progress_tenant ON organization_deletion_progress
 USING(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid)
 WITH CHECK(tenant_id=NULLIF(current_setting('app.tenant_id',true),'')::uuid);
CREATE FUNCTION check_organization_deletion_request() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF TG_OP<>'INSERT' THEN RAISE EXCEPTION 'Organization deletion requests are immutable'; END IF;
 -- Parent gate precedes membership, matching the owning Organization command.
 PERFORM 1 FROM organizations WHERE id=NEW.tenant_id AND status='DELETING'
  AND version=NEW.accepted_version FOR UPDATE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Organization deletion parent is unavailable'; END IF;
 PERFORM 1 FROM users WHERE id=NEW.actor_id AND status='ACTIVE' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Organization deletion account is unavailable'; END IF;
 PERFORM 1 FROM organization_members WHERE tenant_id=NEW.tenant_id AND user_id=NEW.actor_id
  AND status='ACTIVE' AND role='OWNER' FOR SHARE;
 IF NOT FOUND THEN RAISE EXCEPTION 'Organization deletion owner is unavailable'; END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER organization_deletion_requests_guard BEFORE INSERT OR UPDATE OR DELETE
 ON organization_deletion_requests FOR EACH ROW EXECUTE FUNCTION check_organization_deletion_request();
CREATE FUNCTION check_organization_deletion_initial_progress() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
 IF NEW.phase<>'ATTACHMENTS' OR NEW.step_id<>NEW.request_id OR NEW.after_id IS NOT NULL
  OR NEW.version<>1 OR NEW.completed_at IS NOT NULL THEN
  RAISE EXCEPTION 'Organization deletion initial progress is invalid';
 END IF;
 RETURN NEW;
END $$;
CREATE TRIGGER organization_deletion_progress_initial BEFORE INSERT
 ON organization_deletion_progress FOR EACH ROW EXECUTE FUNCTION check_organization_deletion_initial_progress();
REVOKE ALL ON organization_deletion_requests,organization_deletion_progress FROM PUBLIC;
REVOKE ALL ON FUNCTION check_organization_deletion_request(),check_organization_deletion_initial_progress() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('088_organization_deletion_progress');
COMMIT;
