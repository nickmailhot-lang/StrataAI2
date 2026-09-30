#!/usr/bin/env bash
set -euo pipefail

COMPOSE_FILE="${COMPOSE_FILE:-compose.release.yml}"
POSTGRES_DB="${POSTGRES_DB:-strataai}"
POSTGRES_USER="${POSTGRES_USER:-strataai}"

for migration in db/migrations/*.sql; do
  echo "Applying $migration"
  docker compose -f "$COMPOSE_FILE" exec -T postgres     psql -v ON_ERROR_STOP=1       -U "$POSTGRES_USER"       -d "$POSTGRES_DB" < "$migration"
done
