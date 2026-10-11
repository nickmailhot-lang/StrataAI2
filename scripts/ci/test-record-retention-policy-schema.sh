#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Retention schema verification requires disposable CI.' >&2; exit 1; }
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < scripts/ci/assert-record-retention-policy-schema.sql
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < scripts/ci/record-retention-policy-after-upgrade.sql
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < scripts/ci/record-retention-source-after-upgrade.sql
echo 'Retention policy schema, private capabilities and immutable source contract passed.'
