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
cp db/migrations/014_identity_retry_retention.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 14
cp db/migrations/015_identity_revocation_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 15
cp db/migrations/016_invitation_discovery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 16
cp db/migrations/017_identity_login_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 17
cp db/migrations/018_identity_registration_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 18
cp db/migrations/019_identity_recovery_request_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 19
cp db/migrations/020_identity_token_consumption_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 20
# Upgrade populated membership data; a preexisting missing route must refuse
# the upgrade and roll back every constraint rather than inventing membership.
query "INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES ('02100000-0000-0000-0000-000000000010','migration-owner@example.test','MIGRATION-OWNER@EXAMPLE.TEST','Migration owner','ACTIVE','unusable-ci-fixture',now(),now());
 INSERT INTO organizations(id,name,created_at,updated_at) VALUES ('02100000-0000-0000-0000-000000000011','Migration fixture',now(),now());
 INSERT INTO organization_members(id,user_id,tenant_id,role,status) VALUES ('02100000-0000-0000-0000-000000000012','02100000-0000-0000-0000-000000000010','02100000-0000-0000-0000-000000000011','OWNER','ACTIVE');
 DELETE FROM user_organization_access WHERE user_id='02100000-0000-0000-0000-000000000010';" >/dev/null
cp db/migrations/021_organization_access_integrity.sql "$scratch/migrations/"
if run; then echo 'Divergent membership routing accepted during upgrade'; exit 1; fi
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='021_organization_access_integrity'")" = 0
test "$(query "SELECT count(*) FROM pg_constraint WHERE conname IN ('uq_organization_members_access_state','uq_user_organization_access_state','fk_organization_route_membership','fk_organization_membership_route')")" = 0
test "$(query "SELECT count(*) FROM organization_members WHERE id='02100000-0000-0000-0000-000000000012'")" = 1
# Explicitly repair only the disposable fixture using its existing canonical row.
query "UPDATE organization_members SET updated_at=clock_timestamp() WHERE id='02100000-0000-0000-0000-000000000012';" >/dev/null
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 21
test "$(query "SELECT count(*) FROM organization_members m JOIN user_organization_access r USING(user_id,tenant_id,role,status) WHERE m.id='02100000-0000-0000-0000-000000000012'")" = 1
cp db/migrations/022_invitation_creation_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 22
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='invitation_creation_replays'::regclass")" = t
cp db/migrations/023_invitation_mail_intents.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 23
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='invitation_mail_intents'::regclass")" = t
cat > "$scratch/migrations/024_serialization_fixture.sql" <<'SQL'
BEGIN;
SELECT pg_sleep(1);
CREATE TABLE migration_serialization_fixture(id integer);
INSERT INTO schema_migrations(version) VALUES ('024_serialization_fixture');
COMMIT;
SQL
run & first=$!
run & second=$!
wait "$first"
wait "$second"
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='024_serialization_fixture'")" = 1
cat > "$scratch/migrations/025_failure_fixture.sql" <<'SQL'
BEGIN;
CREATE TABLE migration_failure_fixture(id integer);
INSERT INTO schema_migrations(version) VALUES ('025_failure_fixture');
SELECT 1/0;
COMMIT;
SQL
if run; then echo 'Broken migration succeeded'; exit 1; fi
test "$(query "SELECT to_regclass('public.migration_failure_fixture') IS NULL")" = t
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='025_failure_fixture'")" = 0
rm "$scratch/migrations/025_failure_fixture.sql"
run
echo 'Clean, repeat, forward upgrade, serialized runners and failure rollback passed.'
