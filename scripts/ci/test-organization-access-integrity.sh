#!/usr/bin/env bash
set -euo pipefail
# PRD-02/03/20: global routing must be complete and agree with canonical RLS membership.
scratch=$(mktemp -d)
tenant=02100000-0000-0000-0000-000000000001
other=02100000-0000-0000-0000-000000000002
actor=02100000-0000-0000-0000-000000000003
stranger=02100000-0000-0000-0000-000000000004
member=02100000-0000-0000-0000-000000000005
admin() { psql -X -qAt -v ON_ERROR_STOP=1 -c "$1"; }
api() { PGUSER=strataai_api_runtime PGPASSWORD='ci-api-runtime-password' psql -X -qAt -v ON_ERROR_STOP=1 -v VERBOSITY=verbose -c "$1"; }
route() { api "BEGIN; SET LOCAL app.route_kind='ORGANIZATION_USER'; SET LOCAL app.route_key='$actor'; $1; ROLLBACK;"; }
cleanup() {
  admin "DELETE FROM organization_members WHERE tenant_id IN ('$tenant','$other');
    DELETE FROM organizations WHERE id IN ('$tenant','$other');
    DELETE FROM users WHERE id IN ('$actor','$stranger');" >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
admin "INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
  VALUES ('$actor','routing-integrity-1@example.test','ROUTING-INTEGRITY-1@EXAMPLE.TEST','Routing fixture','ACTIVE','unusable-ci-fixture',now(),now()),
    ('$stranger','routing-integrity-2@example.test','ROUTING-INTEGRITY-2@EXAMPLE.TEST','Routing fixture','ACTIVE','unusable-ci-fixture',now(),now());
  INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at)
  VALUES ('$tenant','Routing fixture','$actor',now(),now()),('$other','Other routing fixture','$stranger',now(),now());" >/dev/null
insert_member() {
  api "BEGIN; SET LOCAL app.tenant_id='$tenant';
    INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES ('$member','$tenant','$actor','OWNER','ACTIVE'); COMMIT;" >/dev/null
}
state() {
  admin "SELECT jsonb_build_object('membership',(SELECT jsonb_agg(to_jsonb(m)) FROM organization_members m WHERE tenant_id='$tenant'),
    'route',(SELECT jsonb_agg(to_jsonb(r)) FROM user_organization_access r WHERE tenant_id='$tenant'))::text;"
}
reject() {
  local sql="$1" code="$2" constraint="${3:-}" before
  before="$(state)"
  if api "$sql" > "$scratch/rejection" 2>&1; then echo 'Invalid routing transaction committed'; exit 1; fi
  grep -q "ERROR:  $code:" "$scratch/rejection"
  if test -n "$constraint"; then grep -q "$constraint" "$scratch/rejection"; fi
  test "$before" = "$(state)"
}
insert_member
test "$(api 'SELECT count(*) FROM organization_members')" = 0
test "$(api 'SELECT count(*) FROM user_organization_access')" = 0
test "$(route "SELECT role||':'||status FROM user_organization_access WHERE user_id='$actor' AND tenant_id='$tenant'")" = OWNER:ACTIVE
# Tenant-scoped divergence still reaches the deferred constraints. A cross-tenant
# rewrite is now rejected earlier by the route's forced RLS write policy.
reject "BEGIN; SET LOCAL app.tenant_id='$tenant'; DELETE FROM user_organization_access WHERE user_id='$actor' AND tenant_id='$tenant'; COMMIT;" 23503 fk_organization_membership_route
reject "BEGIN; SET LOCAL app.tenant_id='$tenant'; UPDATE user_organization_access SET role='ADMIN' WHERE user_id='$actor' AND tenant_id='$tenant'; COMMIT;" 23503
reject "BEGIN; SET LOCAL app.tenant_id='$tenant'; UPDATE user_organization_access SET status='REMOVED' WHERE user_id='$actor' AND tenant_id='$tenant'; COMMIT;" 23503
reject "BEGIN; SET LOCAL app.tenant_id='$tenant'; UPDATE user_organization_access SET tenant_id='$other' WHERE user_id='$actor' AND tenant_id='$tenant'; COMMIT;" 42501
reject "BEGIN; SET LOCAL app.tenant_id='$tenant'; INSERT INTO user_organization_access(user_id,tenant_id,role,status) VALUES ('$stranger','$tenant','OWNER','ACTIVE'); COMMIT;" 23503 fk_organization_route_membership
reject "BEGIN; SET LOCAL app.tenant_id='$tenant'; INSERT INTO organization_members(id,tenant_id,user_id,role)
  VALUES (gen_random_uuid(),'$other','$actor','OWNER'); COMMIT;" 42501
before="$(state)"
api "BEGIN; SET LOCAL app.tenant_id='$tenant'; UPDATE organization_members SET role='ADMIN',status='REMOVED',version=version+1 WHERE id='$member'; ROLLBACK;" >/dev/null
test "$before" = "$(state)"
api "BEGIN; SET LOCAL app.tenant_id='$tenant'; UPDATE organization_members SET role='ADMIN',status='REMOVED',version=version+1 WHERE id='$member'; COMMIT;" >/dev/null
test "$(route "SELECT role||':'||status FROM user_organization_access WHERE user_id='$actor' AND tenant_id='$tenant'")" = ADMIN:REMOVED
api "BEGIN; SET LOCAL app.tenant_id='$tenant'; UPDATE organization_members SET role='OWNER',status='ACTIVE',version=version+1 WHERE id='$member'; COMMIT;" >/dev/null
test "$(route "SELECT role||':'||status FROM user_organization_access WHERE user_id='$actor' AND tenant_id='$tenant'")" = OWNER:ACTIVE
api "BEGIN; SET LOCAL app.tenant_id='$tenant'; DELETE FROM organization_members WHERE id='$member'; COMMIT;" >/dev/null
test "$(route "SELECT count(*) FROM user_organization_access WHERE user_id='$actor' AND tenant_id='$tenant'")" = 0
insert_member
test "$(admin "SELECT count(*) FROM organization_members m JOIN user_organization_access r USING(user_id,tenant_id,role,status) WHERE m.id='$member'")" = 1
echo 'Organization routing integrity: restricted-login insert/update/delete synchronization, rollback, deferred divergence rejection and canonical tenant RLS passed.'
