#!/usr/bin/env bash
set -euo pipefail

for migration in db/migrations/*.sql; do
  echo "Applying $migration"
  psql -v ON_ERROR_STOP=1 -f "$migration"
done
