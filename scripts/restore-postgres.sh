#!/usr/bin/env bash
set -euo pipefail
# Restore only a trusted archive into an explicitly selected empty database.
if [ "$#" != 6 ]; then
  echo 'Usage: restore-postgres.sh CONTAINER EMPTY_DATABASE CERT KEY BACKUP.cms EXPECTED_SHA256' >&2; exit 1
fi
container=$1; database=$2; certificate=$3; key=$4; archive=$5; expected=${6,,}
admin=${STRATAAI_RESTORE_DB_USER:-}
if [[ ! "$container" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] \
  || [[ -z "$database" || "$database" == *=* || "$database" == *://* || "$database" =~ [[:cntrl:]] ]] \
  || [[ -z "$admin" || "$admin" =~ [[:cntrl:]] ]] || [[ ! "$expected" =~ ^[a-f0-9]{64}$ ]]; then
  echo 'Explicit database identity, STRATAAI_RESTORE_DB_USER and trusted SHA-256 are required.' >&2; exit 1
fi
if [ ! -f "$certificate" ] || [ ! -f "$key" ] || [ ! -f "$archive" ] || [ ! -d "${STRATAAI_RESTORE_TMPDIR:-}" ]; then
  echo 'Provide recovery files and an existing protected STRATAAI_RESTORE_TMPDIR.' >&2; exit 1
fi
for dependency in docker openssl sha256sum mktemp cp rm; do
  command -v "$dependency" > /dev/null || { echo 'Required restore tools are unavailable.' >&2; exit 1; }
done
temp_root=$(cd -- "$STRATAAI_RESTORE_TMPDIR" && pwd -P)
umask 077
scratch=$(mktemp -d "$temp_root/.strataai-restore.XXXXXXXX")
scratch=$(cd "$scratch" && pwd -P)
[[ "$(dirname -- "$scratch")" == "$temp_root" && "$(basename -- "$scratch")" == .strataai-restore.* ]]
cleanup() {
  local code=$?
  trap - EXIT
  rm -rf -- "$scratch" || code=1
  if [ "$code" != 0 ]; then echo 'Encrypted PostgreSQL restore failed.' >&2; fi
  exit "$code"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
host_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }
# Hash and decrypt the same private snapshot, even if input files are rotated.
cp -- "$archive" "$scratch/archive.cms" 2> "$scratch/copy.log"
actual=$(sha256sum < "$scratch/archive.cms"); actual=${actual%% *}
test "$actual" = "$expected"
cp -- "$certificate" "$scratch/recipient.crt" 2> "$scratch/copy.log"
cp -- "$key" "$scratch/recipient.key" 2> "$scratch/copy.log"
key_passphrase=${STRATAAI_RESTORE_KEY_PASSPHRASE-}
unset STRATAAI_RESTORE_KEY_PASSPHRASE
# Historical backup certificates may have expired; decryption still needs their
# original private key. Passphrases never appear in command-line arguments.
STRATAAI_RESTORE_KEY_PASSPHRASE="$key_passphrase" openssl cms -decrypt -binary -inform DER \
  -in "$(host_path "$scratch/archive.cms")" -recip "$(host_path "$scratch/recipient.crt")" \
  -inkey "$(host_path "$scratch/recipient.key")" -passin env:STRATAAI_RESTORE_KEY_PASSPHRASE \
  -out "$(host_path "$scratch/restored.dump")" > "$scratch/decrypt.log" 2>&1
unset key_passphrase
rm -f -- "$scratch/archive.cms" "$scratch/recipient.key"
# GCM can emit plaintext before authentication finishes. No Docker/SQL command
# runs until successful authentication of the entire decrypted file.
docker exec -i "$container" pg_restore --list < "$scratch/restored.dump" > "$scratch/archive.log" 2>&1
empty_sql="SELECT NOT (
 EXISTS(SELECT FROM pg_namespace WHERE nspname<>'public' AND nspname<>'information_schema' AND nspname !~ '^pg_')
 OR EXISTS(SELECT FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname<>'information_schema' AND n.nspname !~ '^pg_')
 OR EXISTS(SELECT FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname<>'information_schema' AND n.nspname !~ '^pg_')
 OR EXISTS(SELECT FROM pg_type t JOIN pg_namespace n ON n.oid=t.typnamespace WHERE n.nspname<>'information_schema' AND n.nspname !~ '^pg_')
 OR EXISTS(SELECT FROM pg_extension WHERE extname<>'plpgsql'))"
empty=$(docker exec "$container" psql -X --username="$admin" --dbname="$database" --no-password \
  -At -v ON_ERROR_STOP=1 -c "$empty_sql" 2> "$scratch/target.log")
if [ "$empty" != t ]; then echo 'Restore target must be a fresh empty database.' >&2; exit 1; fi
docker exec -i "$container" pg_restore --username="$admin" --dbname="$database" --no-password \
  --single-transaction --exit-on-error < "$scratch/restored.dump" > "$scratch/restore.log" 2>&1
echo 'Encrypted PostgreSQL restore completed; keep the target isolated until recovery verification passes.'
