#!/usr/bin/env bash
set -euo pipefail
docker compose -f "${COMPOSE_FILE:-compose.release.yml}" exec -T postgres \
  sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < db/provision-runtime-roles.sql
