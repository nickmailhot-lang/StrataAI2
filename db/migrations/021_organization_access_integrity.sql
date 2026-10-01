BEGIN;

-- Routing is a global lookup hint; canonical membership remains tenant scoped.
-- Both directions are required: a one-way FK permits deleting a real owner's
-- route, which would make a cross-Organization continuity lock plan incomplete.
ALTER TABLE organization_members
    ADD CONSTRAINT uq_organization_members_access_state UNIQUE (user_id, tenant_id, role, status);
ALTER TABLE user_organization_access
    ADD CONSTRAINT uq_user_organization_access_state UNIQUE (user_id, tenant_id, role, status);

ALTER TABLE user_organization_access
    ADD CONSTRAINT fk_organization_route_membership
    FOREIGN KEY (user_id, tenant_id, role, status)
    REFERENCES organization_members(user_id, tenant_id, role, status)
    DEFERRABLE INITIALLY DEFERRED;
ALTER TABLE organization_members
    ADD CONSTRAINT fk_organization_membership_route
    FOREIGN KEY (user_id, tenant_id, role, status)
    REFERENCES user_organization_access(user_id, tenant_id, role, status)
    DEFERRABLE INITIALLY DEFERRED;

-- Existing AFTER triggers synchronize both rows in the same transaction. Check
-- at commit, after an insert/update/delete has finished synchronizing, without
-- changing tenant RLS, granting a bypass, or introducing a privileged function.
INSERT INTO schema_migrations(version) VALUES ('021_organization_access_integrity');
COMMIT;
