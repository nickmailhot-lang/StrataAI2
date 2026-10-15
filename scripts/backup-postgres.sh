#!/usr/bin/env bash
set -euo pipefail
# Docker-host operator command. Never discovers, starts or modifies a database.
if [ "$#" != 4 ]; then
  echo 'Usage: backup-postgres.sh CONTAINER DATABASE RECIPIENT_CERT NEW_BACKUP.cms' >&2; exit 1
fi
container=$1; database=$2; certificate=$3; output=$4
admin=${STRATAAI_BACKUP_DB_USER:-}
if [[ ! "$container" =~ ^[A-Za-z0-9][A-Za-z0-9_.-]*$ ]] \
  || [[ -z "$database" || "$database" == *=* || "$database" == *://* || "$database" =~ [[:cntrl:]] ]] \
  || [[ -z "$admin" || "$admin" =~ [[:cntrl:]] ]]; then
  echo 'An explicit container, database name and STRATAAI_BACKUP_DB_USER are required.' >&2; exit 1
fi
if [ ! -f "$certificate" ] || [ -e "$output" ] || [ -L "$output" ] || [[ "$output" != *.cms ]]; then
  echo 'Provide a recipient certificate and a new .cms output file.' >&2; exit 1
fi
for dependency in docker openssl mktemp ln rm cp; do
  command -v "$dependency" > /dev/null || { echo 'Required backup tools are unavailable.' >&2; exit 1; }
done
parent=$(cd -- "$(dirname -- "$output")" && pwd -P)
output="$parent/$(basename -- "$output")"
umask 077
scratch=$(mktemp -d "$parent/.strataai-backup.XXXXXXXX")
scratch=$(cd "$scratch" && pwd -P)
[[ "$(dirname -- "$scratch")" == "$parent" && "$(basename -- "$scratch")" == .strataai-backup.* ]]
cleanup() {
  local code=$?
  trap - EXIT
  rm -rf -- "$scratch" || code=1
  if [ "$code" != 0 ]; then echo 'Encrypted PostgreSQL backup failed.' >&2; fi
  exit "$code"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
host_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }
# Certificate pinning/trust and independent recovery-key custody belong to the
# operator. Refuse an unreadable/expired certificate before reading the database.
cp -- "$certificate" "$scratch/recipient.crt" 2> "$scratch/certificate.log"
openssl x509 -in "$(host_path "$scratch/recipient.crt")" -checkend 0 -noout > "$scratch/certificate.log" 2>&1
docker exec "$container" pg_dump --username="$admin" --dbname="$database" \
  --format custom --no-password --lock-wait-timeout=30s 2> "$scratch/dump.log" \
  | openssl cms -encrypt -binary -stream -aes-256-gcm -outform DER \
      -recip "$(host_path "$scratch/recipient.crt")" -out "$(host_path "$scratch/backup.cms")" 2> "$scratch/encrypt.log"
test -s "$scratch/backup.cms"
# Same-directory hard-link publication is atomic and refuses an existing file
# or directory, including one created after the initial check. GNU ln is needed.
ln -T -- "$scratch/backup.cms" "$output" 2> "$scratch/publish.log"
echo 'Encrypted PostgreSQL backup created.'
