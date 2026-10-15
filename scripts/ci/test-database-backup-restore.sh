#!/usr/bin/env bash
set -euo pipefail
# ARCH-08-FR-009: small recovery contract from the supplied CI service to a
# freshly initialized, network-isolated cluster using its exact provider image.
# Never discover a container or select an existing deployment database.
container="${1:?Pass the isolated PostgreSQL CI service container ID}"
admin="${PGUSER:-postgres}"
umask 077
temp_root="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
scratch="$(mktemp -d "$temp_root/strataai-backup-restore.XXXXXXXX")"
scratch="$(cd "$scratch" && pwd -P)"
[[ "$scratch" == "$temp_root"/strataai-backup-restore.* ]]
suffix=$(openssl rand -hex 8)
source_db="strataai_backup_source_$suffix"
target_db="strataai_backup_target_$suffix"
reader="strataai_backup_reader_$suffix"
target_container="strataai-recovery-target-$suffix"
source_created=0; target_created=0; reader_created=0; stage=initialization
cleanup() {
  local code=$?
  trap - EXIT
  if [ "$code" != 0 ]; then echo "Encrypted database recovery contract failed during $stage." >&2; fi
  if [ "$target_created" = 1 ]; then docker rm --force --volumes "$target_container" > /dev/null 2>&1 || code=1; fi
  if [ "$source_created" = 1 ]; then docker exec "$container" dropdb -U "$admin" "$source_db" > /dev/null 2>&1 || code=1; fi
  if [ "$reader_created" = 1 ]; then docker exec "$container" dropuser -U "$admin" "$reader" > /dev/null 2>&1 || code=1; fi
  rm -rf "$scratch" || code=1
  exit "$code"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
# Translate host-side paths only for native Windows OpenSSL/Python. Docker SQL
# and file streams remain stdin, so Git Bash cannot rewrite container paths.
host_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }
for name in recipient wrong-recipient; do
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
    -subj "/CN=strataai-backup-$name" -keyout "$(host_path "$scratch/$name.key")" \
    -out "$(host_path "$scratch/$name.crt")" > "$scratch/certificate.log" 2>&1
done
docker exec "$container" createdb -U "$admin" --template template0 "$source_db"
source_created=1
stage=fresh-target-cluster
provider_image=$(docker inspect --format '{{.Image}}' "$container")
[[ "$provider_image" =~ ^sha256:[a-f0-9]{64}$ ]]
MSYS_NO_PATHCONV=1 docker run --detach --pull never --name "$target_container" \
  --label "strataai.recovery-source=$container" --network none \
  --tmpfs /var/lib/postgresql/data:rw,nosuid,noexec,size=512m \
  --env "POSTGRES_USER=$admin" --env POSTGRES_DB=postgres \
  --env "POSTGRES_PASSWORD=$(openssl rand -hex 24)" "$provider_image" > "$scratch/target-startup.log" 2>&1
target_created=1
ready=0
for attempt in {1..30}; do
  # The image's temporary initialization server accepts Unix sockets before it
  # restarts. Require the final TCP listener before using the new cluster.
  if docker exec "$target_container" pg_isready -h 127.0.0.1 -U "$admin" -d postgres > "$scratch/target-ready.log" 2>&1; then ready=1; break; fi
  sleep 1
done
test "$ready" = 1
source_identity=$(docker exec "$container" psql -X -U "$admin" -d postgres -At -v ON_ERROR_STOP=1 -c 'SELECT system_identifier FROM pg_control_system()')
target_identity=$(docker exec "$target_container" psql -X -U "$admin" -d postgres -At -v ON_ERROR_STOP=1 -c 'SELECT system_identifier FROM pg_control_system()')
[[ "$source_identity" =~ ^[0-9]+$ && "$target_identity" =~ ^[0-9]+$ && "$source_identity" != "$target_identity" ]]
docker exec "$target_container" createdb -U "$admin" --template template0 "$target_db"
docker exec "$container" createuser -U "$admin" --no-superuser --no-createdb --no-createrole --no-login "$reader"
reader_created=1
stage=migrations-and-seed
scripts/migration-stream.sh | docker exec -i "$container" psql -X -U "$admin" -d "$source_db" -v ON_ERROR_STOP=1 > "$scratch/migrations.log" 2>&1
docker exec -i "$container" psql -X -U "$admin" -d "$source_db" -v ON_ERROR_STOP=1 < scripts/ci/database-backup-fixture.sql > "$scratch/seed.log" 2>&1
docker exec "$container" psql -X -U "$admin" -d "$source_db" -v ON_ERROR_STOP=1 \
  -c "GRANT USAGE ON SCHEMA public TO $reader; GRANT SELECT ON organizations,boards,cards,board_lists TO $reader; GRANT INSERT ON board_lists TO $reader; GRANT SELECT,INSERT,UPDATE ON list_routes TO $reader" > /dev/null

# No plaintext dump is written during backup. Preserve ACLs and schema objects;
# the target has a different cluster identity and does not inherit source roles.
stage=encrypted-backup
STRATAAI_BACKUP_DB_USER="$admin" bash scripts/backup-postgres.sh "$container" "$source_db" \
  "$scratch/recipient.crt" "$scratch/backup.cms" > "$scratch/backup.log" 2>&1
test -s "$scratch/backup.cms"
python3 - "$(host_path "$scratch/backup.cms")" "$(host_path "$scratch/tampered.cms")" <<'PY'
import pathlib, sys
data = bytearray(pathlib.Path(sys.argv[1]).read_bytes())
assert len(data) > 2048
data[len(data) // 2] ^= 1
pathlib.Path(sys.argv[2]).write_bytes(data)
PY
restore() {
  STRATAAI_RESTORE_DB_USER="$admin" STRATAAI_RESTORE_TMPDIR="$scratch" \
    bash scripts/restore-postgres.sh "$target_container" "$target_db" "$scratch/$2.crt" "$scratch/$2.key" "$1" "$3"
}
fingerprint() {
  local fingerprint_container=${2:-$target_container}
  {
    docker exec "$fingerprint_container" pg_dump -U "$admin" -d "$1" --format plain \
      2> "$scratch/compare.log" | PYTHONIOENCODING=utf-8 python3 scripts/ci/normalize-recovery-dump.py || return 1
    docker exec "$fingerprint_container" psql -X -U "$admin" -d "$1" -At -v ON_ERROR_STOP=1 \
      -c "SELECT jsonb_build_array(conrelid::regclass::text,contypid::regtype::text,conname,pg_get_constraintdef(oid,true)) FROM pg_constraint WHERE contype='c' AND connamespace='public'::regnamespace ORDER BY 1" || return 1
  } | sha256sum
}
query() { docker exec "$target_container" psql -X -U "$admin" -d "$target_db" -At -v ON_ERROR_STOP=1 -c "$1"; }
stage=authentication-refusals
for refusal in wrong-key tampered wrong-digest; do
  archive="$scratch/backup.cms"; recipient=wrong-recipient
  if [ "$refusal" = tampered ]; then archive="$scratch/tampered.cms"; recipient=recipient; fi
  archive_digest=$(sha256sum < "$archive"); archive_digest=${archive_digest%% *}
  if [ "$refusal" = wrong-digest ]; then recipient=recipient; archive_digest=$(printf '%064d' 0); fi
  if restore "$archive" "$recipient" "$archive_digest" > "$scratch/refusal.log" 2>&1; then
    echo 'Encrypted recovery admitted an invalid archive/key/digest.' >&2; exit 1
  fi
done
test "$(docker exec "$target_container" psql -X -U "$admin" -d "$target_db" -At -v ON_ERROR_STOP=1 \
  -c "SELECT count(*) FROM pg_class WHERE relnamespace='public'::regnamespace")" = 0
stage=authenticated-restore
archive_digest=$(sha256sum < "$scratch/backup.cms"); archive_digest=${archive_digest%% *}
stage=nonempty-object-refusals
for kind in schema function type extension; do
  case "$kind" in
    schema) creation='CREATE SCHEMA restore_guard_fixture'; removal='DROP SCHEMA restore_guard_fixture' ;;
    function) creation="CREATE FUNCTION public.restore_guard_fixture() RETURNS integer LANGUAGE sql AS 'SELECT 1'"; removal='DROP FUNCTION public.restore_guard_fixture()' ;;
    type) creation="CREATE TYPE public.restore_guard_fixture AS ENUM ('fixture')"; removal='DROP TYPE public.restore_guard_fixture' ;;
    extension) creation='CREATE EXTENSION vector'; removal='DROP EXTENSION vector' ;;
  esac
  query "$creation" > "$scratch/nonempty-object.log" 2>&1
  before_refusal=$(fingerprint "$target_db")
  if restore "$scratch/backup.cms" recipient "$archive_digest" > "$scratch/nonempty-restore.log" 2>&1; then
    echo 'Recovery accepted an existing target object.' >&2; exit 1
  fi
  after_refusal=$(fingerprint "$target_db")
  test "$before_refusal" = "$after_refusal"
  query "$removal" > "$scratch/nonempty-object.log" 2>&1
done
stage=missing-cluster-role-refusal
test "$(query "SELECT count(*) FROM pg_roles WHERE rolname='$reader'")" = 0
before_missing_role=$(fingerprint "$target_db")
if restore "$scratch/backup.cms" recipient "$archive_digest" > "$scratch/missing-role.log" 2>&1; then
  echo 'Recovery unexpectedly restored ACLs without the required cluster role.' >&2; exit 1
fi
test "$(query "SELECT count(*) FROM pg_class WHERE relnamespace='public'::regnamespace")" = 0
after_missing_role=$(fingerprint "$target_db")
test "$before_missing_role" = "$after_missing_role"
# A logical dump omits cluster-global roles. Recreate only this fixture's
# reviewed least-privilege identity; never copy cluster superusers/passwords.
docker exec "$target_container" createuser -U "$admin" --no-superuser --no-createdb --no-createrole --no-login "$reader"
test "$(query "SELECT NOT (rolsuper OR rolcreatedb OR rolcreaterole OR rolcanlogin OR rolbypassrls OR rolreplication) FROM pg_roles WHERE rolname='$reader'")" = t
stage=authenticated-restore
restore "$scratch/backup.cms" recipient "$archive_digest" > "$scratch/restore.log" 2>&1

# Compare the complete schema, ACLs and data. PostgreSQL flattens nested AND
# check expressions when restoring DDL. Replace only inline CHECK definitions
# with their pretty catalog form (including table/type identity and name), which
# preserves their semantics without comparing redundant parentheses. Session
# restrict keys are random, so exclude those two pg_dump client guard lines.
# COPY row order is physical, not relational; compare sorted complete rows.
stage=complete-dump-comparison
expected=$(fingerprint "$source_db" "$container")
compare_fingerprint() {
  local actual
  actual=$(fingerprint "$target_db") || return 1
  test "$actual" "$1" "$expected"
}
compare_fingerprint =
stage=nonempty-target-refusal
if restore "$scratch/backup.cms" recipient "$archive_digest" > "$scratch/nonempty.log" 2>&1; then
  echo 'Encrypted recovery accepted a nonempty target.' >&2; exit 1
fi
compare_fingerprint =
# Prove the comparison actually detects lost data, ACL changes and a missing
# CHECK; restore each deliberate defect before the final security assertions.
stage=comparison-data-refusal
query "UPDATE schema_migrations SET applied_at=applied_at+interval '1 second' WHERE version='001_foundation'" > /dev/null
compare_fingerprint !=
query "UPDATE schema_migrations SET applied_at=applied_at-interval '1 second' WHERE version='001_foundation'" > /dev/null
compare_fingerprint =
stage=comparison-acl-refusal
query "GRANT INSERT ON cards TO $reader" > /dev/null
compare_fingerprint !=
query "REVOKE INSERT ON cards FROM $reader" > /dev/null
compare_fingerprint =
stage=comparison-check-refusal
check_definition=$(query "SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid='cards'::regclass AND conname='cards_title_check'")
test -n "$check_definition"
query 'ALTER TABLE cards DROP CONSTRAINT cards_title_check' > /dev/null
compare_fingerprint !=
query "ALTER TABLE cards ADD CONSTRAINT cards_title_check $check_definition" > /dev/null
compare_fingerprint =
stage=restored-security-contracts
docker exec -i "$target_container" psql -X -U "$admin" -d "$target_db" -v ON_ERROR_STOP=1 \
  < scripts/ci/check-tenant-schema.sql > "$scratch/catalog.log" 2>&1
docker exec -i "$target_container" psql -X -U "$admin" -d "$target_db" -v ON_ERROR_STOP=1 \
  -v "reader=$reader" < scripts/ci/database-backup-verify.sql > "$scratch/verify.log" 2>&1
echo 'Encrypted PostgreSQL backup/restore passed: distinct fresh cluster, missing-role transaction rollback and least-privilege role recreation, complete migrated schema/data/ACL comparison, twelve deliberate corruption/refusal cases, pgvector, restricted tenant reads/writes, FK and append-only audit protections.'
