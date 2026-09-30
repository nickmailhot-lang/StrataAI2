#!/usr/bin/env bash
set -euo pipefail

: "${PGHOST:=127.0.0.1}"
: "${PGPORT:=5432}"
: "${PGDATABASE:=strataai_ci}"
: "${PGUSER:=postgres}"
: "${PGPASSWORD:=postgres}"

export PGHOST PGPORT PGDATABASE PGUSER PGPASSWORD

ORG_A="11111111-1111-1111-1111-111111111111"
ORG_B="22222222-2222-2222-2222-222222222222"
BOARD_A="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
BOARD_B="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"

psql -v ON_ERROR_STOP=1 <<SQL
DROP ROLE IF EXISTS strataai_ci_app;
CREATE ROLE strataai_ci_app LOGIN PASSWORD 'strataai-ci-app';

INSERT INTO organizations(id, name, created_at, updated_at)
VALUES
  ('$ORG_A', 'Organization A', now(), now()),
  ('$ORG_B', 'Organization B', now(), now());

INSERT INTO boards(id, tenant_id, name, created_at, updated_at)
VALUES
  ('$BOARD_A', '$ORG_A', 'A Board', now(), now()),
  ('$BOARD_B', '$ORG_B', 'B Board', now(), now());

GRANT USAGE ON SCHEMA public TO strataai_ci_app;
GRANT SELECT, INSERT, UPDATE, DELETE
  ON organizations, boards, board_lists, cards
  TO strataai_ci_app;
SQL

tenant_query() {
  local tenant_id="$1"
  local sql="$2"

  PGOPTIONS="-c app.tenant_id=$tenant_id"   PGPASSWORD="strataai-ci-app"     psql       --username=strataai_ci_app       --tuples-only       --no-align       --quiet       --command="$sql"
}

count_a="$(tenant_query "$ORG_A" "SELECT count(*) FROM boards;")"

if [ "$count_a" != "1" ]; then
  echo "Expected Organization A to see exactly one board; got '$count_a'." >&2
  exit 1
fi

count_b="$(tenant_query "$ORG_A" "SELECT count(*) FROM boards WHERE id='$BOARD_B';")"

if [ "$count_b" != "0" ]; then
  echo "Cross-tenant board leaked through RLS." >&2
  exit 1
fi

set +e
PGOPTIONS="-c app.tenant_id=$ORG_A" PGPASSWORD="strataai-ci-app"   psql     --username=strataai_ci_app     --quiet     --command="INSERT INTO boards(id, tenant_id, name, created_at, updated_at) VALUES (gen_random_uuid(), '$ORG_B', 'Forbidden', now(), now());"     >/tmp/strataai-rls-write.log 2>&1
write_status=$?
set -e

if [ "$write_status" -eq 0 ]; then
  echo "Cross-tenant write unexpectedly succeeded." >&2
  cat /tmp/strataai-rls-write.log >&2
  exit 1
fi

missing_context_count="$(
  PGPASSWORD="strataai-ci-app"     psql       --username=strataai_ci_app       --tuples-only       --no-align       --quiet       --command="SELECT count(*) FROM boards;"
)"

if [ "$missing_context_count" != "0" ]; then
  echo "Missing tenant context did not fail closed." >&2
  exit 1
fi

vector_check="$(
  psql     --tuples-only     --no-align     --quiet     --command="SELECT extversion FROM pg_extension WHERE extname='vector';"
)"

if [ -z "$vector_check" ]; then
  echo "pgvector extension is not installed." >&2
  exit 1
fi

echo "PostgreSQL RLS and pgvector integration checks passed."
