#!/usr/bin/env bash
set -euo pipefail
docker compose -f "${COMPOSE_FILE:-compose.release.yml}" exec -T postgres \
  psql -v ON_ERROR_STOP=1 -U "${POSTGRES_USER:-strataai}" -d "${POSTGRES_DB:-strataai}" < db/provision-runtime-roles.sql
