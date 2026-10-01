#!/usr/bin/env bash
set -euo pipefail
scratch=$(mktemp -d)
database=strataai_migration_runner_ci
cleanup() { rm -rf "$scratch"; dropdb --if-exists "$database" >/dev/null; }
trap cleanup EXIT
createdb "$database"
mkdir "$scratch/migrations"
cp db/migrations/00[1-8]_*.sql "$scratch/migrations/"
run() { scripts/migration-stream.sh "$scratch/migrations" | PGDATABASE="$database" psql -X -v ON_ERROR_STOP=1 >/dev/null; }
query() { PGDATABASE="$database" psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 8
cp db/migrations/009_runtime_role_guard.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 9
cp db/migrations/010_work_command_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 10
cp db/migrations/011_work_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 11
cp db/migrations/012_identity_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 12
cp db/migrations/013_identity_profile_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 13
cat > "$scratch/migrations/014_serialization_fixture.sql" <<'SQL'
BEGIN;
SELECT pg_sleep(1);
CREATE TABLE migration_serialization_fixture(id integer);
INSERT INTO schema_migrations(version) VALUES ('014_serialization_fixture');
COMMIT;
SQL
run & first=$!
run & second=$!
wait "$first"
wait "$second"
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='014_serialization_fixture'")" = 1
cat > "$scratch/migrations/015_failure_fixture.sql" <<'SQL'
BEGIN;
CREATE TABLE migration_failure_fixture(id integer);
INSERT INTO schema_migrations(version) VALUES ('015_failure_fixture');
SELECT 1/0;
COMMIT;
SQL
if run; then echo 'Broken migration succeeded'; exit 1; fi
test "$(query "SELECT to_regclass('public.migration_failure_fixture') IS NULL")" = t
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='015_failure_fixture'")" = 0
rm "$scratch/migrations/015_failure_fixture.sql"
run
echo 'Clean, repeat, forward upgrade, serialized runners and failure rollback passed.'
