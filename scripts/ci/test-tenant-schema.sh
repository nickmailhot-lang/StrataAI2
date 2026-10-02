#!/usr/bin/env bash
set -euo pipefail
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
check() { psql -X -v ON_ERROR_STOP=1 -f scripts/ci/check-tenant-schema.sql >/dev/null; }
check

reject() {
  # A rejected disposable catalog mutation is rolled back when psql closes its
  # failed transaction. Nothing here changes an applied migration or production.
  if { printf 'BEGIN;\n%s;\n' "$1"; cat scripts/ci/check-tenant-schema.sql; printf '\nROLLBACK;\n'; } \
      | psql -X -v ON_ERROR_STOP=1 >"$scratch/rejection.log" 2>&1; then
    echo 'Tenant schema guard accepted an invalid catalog' >&2; exit 1
  fi
  if ! grep -q 'Tenant schema invariant failed:' "$scratch/rejection.log"; then
    echo 'Catalog fixture failed before exercising the tenant schema guard' >&2; exit 1
  fi
  check
  test "$(psql -X -At -v ON_ERROR_STOP=1 -c "SELECT to_regclass('public.schema_guard_fixture') IS NULL")" = t
}
reject 'ALTER TABLE boards ALTER COLUMN tenant_id DROP NOT NULL'
reject 'ALTER TABLE boards DISABLE ROW LEVEL SECURITY'
reject 'ALTER TABLE boards NO FORCE ROW LEVEL SECURITY'
reject 'ALTER TABLE audit_events DISABLE ROW LEVEL SECURITY'
reject 'CREATE TABLE schema_guard_fixture(id uuid PRIMARY KEY)'
reject 'CREATE TABLE schema_guard_fixture(id uuid PRIMARY KEY, tenant_id uuid NOT NULL); ALTER TABLE schema_guard_fixture ENABLE ROW LEVEL SECURITY; ALTER TABLE schema_guard_fixture FORCE ROW LEVEL SECURITY'
echo 'Migrated tenant catalog, nullable/missing key, disabled/unforced RLS, missing policy and rollback checks passed.'
