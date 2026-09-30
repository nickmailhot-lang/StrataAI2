#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:=127.0.0.1}"
: "${PGPORT:=5432}"
: "${PGDATABASE:=strataai_ci}"
: "${PGUSER:=postgres}"
: "${PGPASSWORD:=postgres}"

export PGHOST PGPORT PGDATABASE PGUSER PGPASSWORD

ORG_A="11111111-1111-1111-1111-111111111111"
AUDIT_ID="33333333-3333-3333-3333-333333333333"

psql -v ON_ERROR_STOP=1 --quiet <<SQL
INSERT INTO audit_events(
  id, tenant_id, event_type, entity_type, entity_id, correlation_id, safe_metadata)
VALUES (
  '$AUDIT_ID',
  '$ORG_A',
  'FOUNDATION_TEST',
  'Organization',
  '$ORG_A',
  'arch-08-ci',
  '{"safe":true}'::jsonb
);
SQL

set +e
psql --quiet --command="UPDATE audit_events SET event_type='MUTATED' WHERE id='$AUDIT_ID';" >/tmp/audit-update.log 2>&1
update_status=$?
psql --quiet --command="DELETE FROM audit_events WHERE id='$AUDIT_ID';" >/tmp/audit-delete.log 2>&1
delete_status=$?
set -e

if [ "$update_status" -eq 0 ] || [ "$delete_status" -eq 0 ]; then
  echo "Append-only audit protection failed." >&2
  exit 1
fi

echo "Append-only audit checks passed."
