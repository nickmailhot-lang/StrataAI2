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
  if ! grep -q "${2:-Tenant} schema invariant failed:" "$scratch/rejection.log"; then
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
for table in navigation_interaction_events navigation_interaction_replays; do
  reject "ALTER TABLE $table DISABLE ROW LEVEL SECURITY" Actor
  reject "ALTER TABLE $table NO FORCE ROW LEVEL SECURITY" Actor
done
reject 'ALTER TABLE navigation_interaction_events ALTER COLUMN actor_id DROP NOT NULL' Actor
reject 'ALTER POLICY navigation_interaction_subject ON navigation_interaction_events WITH CHECK (true)' Actor
reject 'ALTER POLICY navigation_replay_subject ON navigation_interaction_replays USING (true)' Actor
for table in invitation_recipient_streams invitation_recipient_authority_revisions; do
  reject "ALTER TABLE $table DISABLE ROW LEVEL SECURITY" Recipient
  reject "ALTER TABLE $table NO FORCE ROW LEVEL SECURITY" Recipient
  reject "ALTER TABLE $table DROP CONSTRAINT ${table}_pkey CASCADE; ALTER TABLE $table ALTER COLUMN email_normalized DROP NOT NULL" Recipient
  reject "CREATE POLICY schema_guard_recipient_wide ON $table FOR SELECT USING (true)" Recipient
done
reject 'ALTER POLICY invitation_authority_revision_lookup ON invitation_recipient_authority_revisions USING (true)' Recipient
reject "ALTER POLICY invitation_authority_revision_lookup ON invitation_recipient_authority_revisions USING (email_normalized=current_setting('app.route_key',true))" Recipient
reject 'ALTER TABLE invitation_recipient_authority_pages NO FORCE ROW LEVEL SECURITY'
reject 'ALTER TABLE invitation_recipient_authority_effects NO FORCE ROW LEVEL SECURITY'

echo 'Migrated tenant catalog, nullable/missing key, disabled/unforced RLS, missing policy and rollback checks passed.'
