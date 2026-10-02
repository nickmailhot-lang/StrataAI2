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
cp db/migrations/024_invitation_history.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 24
test "$(query "SELECT to_regclass('public.ix_invitations_tenant_cursor') IS NOT NULL")" = t
cp db/migrations/025_board_invitation_targets.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 25
test "$(query "SELECT to_regclass('public.ix_invitations_board_pending') IS NOT NULL")" = t
query "INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('02500000-0000-0000-0000-000000000001','02100000-0000-0000-0000-000000000011','Board target',now(),now());
 INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('02500000-0000-0000-0000-000000000002','Foreign target',now(),now());
 INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('02500000-0000-0000-0000-000000000003','02500000-0000-0000-0000-000000000002','Foreign Board',now(),now());
 INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
 created_by_user_id,created_at,expires_at,target_board_id,target_board_role) VALUES
 ('02500000-0000-0000-0000-000000000004','02100000-0000-0000-0000-000000000011','board@example.test','BOARD@EXAMPLE.TEST',
 repeat('b',64),'INTERNAL','MEMBER','02100000-0000-0000-0000-000000000010',now(),now()+interval '1 day',
 '02500000-0000-0000-0000-000000000001','ADMIN');" >/dev/null
test "$(query "SELECT target_board_id='02500000-0000-0000-0000-000000000001' AND target_board_role='ADMIN'
 FROM invitation_routes WHERE invitation_id='02500000-0000-0000-0000-000000000004'")" = t
for change in "target_board_id='02500000-0000-0000-0000-000000000003'" "target_board_role=NULL" \
 "target_board_id=NULL" "target_board_role='OWNER'" "target_surface='PORTAL'" "target_role='OWNER'"; do
 if query "UPDATE invitations SET $change WHERE id='02500000-0000-0000-0000-000000000004';" >/dev/null; then
   echo 'Invalid Board invitation target was admitted'; exit 1
 fi
done
query "UPDATE invitations SET accepted_at=now(),accepted_by_user_id='02100000-0000-0000-0000-000000000010'
 WHERE id='02500000-0000-0000-0000-000000000004';" >/dev/null
if query "UPDATE invitations SET target_board_role='MEMBER' WHERE id='02500000-0000-0000-0000-000000000004';" >/dev/null; then
 echo 'Accepted Board invitation role was rewritten'; exit 1
fi
cp db/migrations/026_board_invitation_mail.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 26
cp db/migrations/027_routing_isolation.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 27
test "$(query "SELECT count(*) FROM pg_class WHERE relname IN ('board_routes','list_routes','card_routes','user_organization_access','invitation_routes') AND relrowsecurity AND relforcerowsecurity")" = 5
cp db/migrations/028_board_labels.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 28
test "$(query "SELECT count(*) FROM pg_class WHERE relname IN ('board_labels','card_labels') AND relrowsecurity AND relforcerowsecurity")" = 2
cp db/migrations/029_label_routing.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 29
cp db/migrations/030_card_members.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 30
test "$(query "SELECT count(*) FROM pg_class WHERE relname='card_members' AND relrowsecurity AND relforcerowsecurity")" = 1
cat > "$scratch/migrations/031_serialization_fixture.sql" <<'SQL'
BEGIN;
SELECT pg_sleep(1);
CREATE TABLE migration_serialization_fixture(id integer);
INSERT INTO schema_migrations(version) VALUES ('031_serialization_fixture');
COMMIT;
SQL
run & first=$!
run & second=$!
wait "$first"
wait "$second"
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='031_serialization_fixture'")" = 1
cat > "$scratch/migrations/032_failure_fixture.sql" <<'SQL'
BEGIN;
CREATE TABLE migration_failure_fixture(id integer);
INSERT INTO schema_migrations(version) VALUES ('032_failure_fixture');
SELECT 1/0;
COMMIT;
SQL
if run; then echo 'Broken migration succeeded'; exit 1; fi
test "$(query "SELECT to_regclass('public.migration_failure_fixture') IS NULL")" = t
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='032_failure_fixture'")" = 0
rm "$scratch/migrations/032_failure_fixture.sql"
run
echo 'Clean, repeat, forward upgrade, serialized runners and failure rollback passed.'
