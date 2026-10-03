#!/usr/bin/env bash
# Emit one psql session: the advisory lock spans every migration transaction.
set -euo pipefail
directory="${1:-db/migrations}"
printf '%s\n' '\set ON_ERROR_STOP on' 'SELECT pg_advisory_lock(1937011316, 1);'
printf '%s\n' "SELECT to_regclass('public.schema_migrations') IS NOT NULL AS ledger_exists \\gset"
for migration in "$directory"/*.sql; do
  version=$(basename "$migration" .sql)
  [[ "$version" =~ ^[0-9]{3}_[a-z0-9_]+$ ]] || { echo 'Invalid migration filename' >&2; exit 1; }
  printf '%s\n' '\if :ledger_exists' "SELECT EXISTS(SELECT 1 FROM public.schema_migrations WHERE version='$version') AS already_applied \\gset" '\else' '\set already_applied false' '\endif' '\if :already_applied' "\\echo Skipping $version" '\else' "\\echo Applying $version"
  cat "$migration"
  printf '\n%s\n' "SELECT EXISTS(SELECT 1 FROM public.schema_migrations WHERE version='$version') AS recorded \\gset" '\if :recorded' '\else' "DO \$\$ BEGIN RAISE EXCEPTION 'Migration did not record its version'; END \$\$;" '\endif' '\set ledger_exists true' '\endif'
done
printf '%s\n' 'SELECT pg_advisory_unlock(1937011316, 1);'
