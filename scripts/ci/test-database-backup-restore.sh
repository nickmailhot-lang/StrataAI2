#!/usr/bin/env bash
set -euo pipefail
# ARCH-08-FR-009: small recovery contract, only against the supplied CI service.
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
source_created=0; target_created=0; reader_created=0; stage=initialization
cleanup() {
  local code=$?
  trap - EXIT
  if [ "$code" != 0 ]; then echo "Encrypted database recovery contract failed during $stage." >&2; fi
  if [ "$target_created" = 1 ]; then docker exec "$container" dropdb -U "$admin" "$target_db" > /dev/null 2>&1 || code=1; fi
  if [ "$source_created" = 1 ]; then docker exec "$container" dropdb -U "$admin" "$source_db" > /dev/null 2>&1 || code=1; fi
  if [ "$reader_created" = 1 ]; then docker exec "$container" dropuser -U "$admin" "$reader" > /dev/null 2>&1 || code=1; fi
  rm -rf "$scratch" || code=1
  exit "$code"
}
trap cleanup EXIT
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
docker exec "$container" createdb -U "$admin" --template template0 "$target_db"
target_created=1
docker exec "$container" createuser -U "$admin" --no-superuser --no-createdb --no-createrole --no-login "$reader"
reader_created=1
stage=migrations-and-seed
scripts/migration-stream.sh | docker exec -i "$container" psql -X -U "$admin" -d "$source_db" -v ON_ERROR_STOP=1 > "$scratch/migrations.log" 2>&1
docker exec -i "$container" psql -X -U "$admin" -d "$source_db" -v ON_ERROR_STOP=1 < scripts/ci/database-backup-fixture.sql > "$scratch/seed.log" 2>&1
docker exec "$container" psql -X -U "$admin" -d "$source_db" -v ON_ERROR_STOP=1 \
  -c "GRANT USAGE ON SCHEMA public TO $reader; GRANT SELECT ON organizations,boards,cards,board_lists TO $reader; GRANT INSERT ON board_lists TO $reader; GRANT SELECT,INSERT,UPDATE ON list_routes TO $reader" > /dev/null

# No plaintext dump is written during backup. Preserve ACLs and schema objects;
# the recovery target is this same disposable cluster, whose roles still exist.
stage=encrypted-backup
docker exec "$container" pg_dump -U "$admin" -d "$source_db" --format custom --lock-wait-timeout=30s \
  2> "$scratch/dump.log" | openssl cms -encrypt -binary -stream -aes-256-gcm -outform DER \
  -recip "$(host_path "$scratch/recipient.crt")" -out "$(host_path "$scratch/backup.cms")" 2> "$scratch/encrypt.log"
test -s "$scratch/backup.cms"
python3 - "$(host_path "$scratch/backup.cms")" "$(host_path "$scratch/tampered.cms")" <<'PY'
import pathlib, sys
data = bytearray(pathlib.Path(sys.argv[1]).read_bytes())
assert len(data) > 2048
data[len(data) // 2] ^= 1
pathlib.Path(sys.argv[2]).write_bytes(data)
PY
decrypt() {
  openssl cms -decrypt -binary -inform DER -in "$(host_path "$1")" \
    -recip "$(host_path "$scratch/$2.crt")" -inkey "$(host_path "$scratch/$2.key")" \
    -out "$(host_path "$scratch/restored.dump")" 2> "$scratch/decrypt.log"
}
stage=authentication-refusals
for refusal in wrong-key tampered; do
  archive="$scratch/backup.cms"; recipient=wrong-recipient
  if [ "$refusal" = tampered ]; then archive="$scratch/tampered.cms"; recipient=recipient; fi
  if decrypt "$archive" "$recipient"; then echo 'Encrypted recovery admitted an invalid archive/key.' >&2; exit 1; fi
  # Decryption may emit plaintext before GCM authentication fails. Never pipe
  # that output into pg_restore; discard it before any database operation.
  rm -f "$scratch/restored.dump"
done
test "$(docker exec "$container" psql -X -U "$admin" -d "$target_db" -At -v ON_ERROR_STOP=1 \
  -c "SELECT count(*) FROM pg_class WHERE relnamespace='public'::regnamespace")" = 0
stage=authenticated-restore
decrypt "$scratch/backup.cms" recipient
docker exec -i "$container" pg_restore -U "$admin" -d "$target_db" --single-transaction --exit-on-error \
  < "$scratch/restored.dump" > "$scratch/restore.log" 2>&1
rm -f "$scratch/restored.dump"

# Compare the complete schema, ACLs and data. PostgreSQL flattens nested AND
# check expressions when restoring DDL. Replace only inline CHECK definitions
# with their pretty catalog form (including table/type identity and name), which
# preserves their semantics without comparing redundant parentheses. Session
# restrict keys are random, so exclude those two pg_dump client guard lines.
# COPY row order is physical, not relational; compare sorted complete rows.
stage=complete-dump-comparison
fingerprint() {
  {
    docker exec "$container" pg_dump -U "$admin" -d "$1" --format plain \
      2> "$scratch/compare.log" | PYTHONIOENCODING=utf-8 python3 scripts/ci/normalize-recovery-dump.py || return 1
    docker exec "$container" psql -X -U "$admin" -d "$1" -At -v ON_ERROR_STOP=1 \
      -c "SELECT jsonb_build_array(conrelid::regclass::text,contypid::regtype::text,conname,pg_get_constraintdef(oid,true)) FROM pg_constraint WHERE contype='c' AND connamespace='public'::regnamespace ORDER BY 1" || return 1
  } | sha256sum
}
expected=$(fingerprint "$source_db")
compare_fingerprint() {
  local actual
  actual=$(fingerprint "$target_db") || return 1
  test "$actual" "$1" "$expected"
}
compare_fingerprint =
# Prove the comparison actually detects lost data, ACL changes and a missing
# CHECK; restore each deliberate defect before the final security assertions.
stage=comparison-data-refusal
query() { docker exec "$container" psql -X -U "$admin" -d "$target_db" -At -v ON_ERROR_STOP=1 -c "$1"; }
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
docker exec -i "$container" psql -X -U "$admin" -d "$target_db" -v ON_ERROR_STOP=1 \
  < scripts/ci/check-tenant-schema.sql > "$scratch/catalog.log" 2>&1
docker exec -i "$container" psql -X -U "$admin" -d "$target_db" -v ON_ERROR_STOP=1 \
  -v "reader=$reader" < scripts/ci/database-backup-verify.sql > "$scratch/verify.log" 2>&1
echo 'Encrypted PostgreSQL backup/restore passed: complete migrated schema/data/ACL comparison, five deliberate corruption/refusal cases, pgvector, restricted tenant reads/writes, FK and append-only audit protections.'
