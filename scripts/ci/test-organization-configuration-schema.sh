#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Configuration schema verification requires disposable CI.' >&2; exit 1; }
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -q -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < scripts/ci/assert-organization-configuration-schema.sql
echo 'Installed configuration keys, relationships, forced tenant isolation and restricted capabilities passed.'
