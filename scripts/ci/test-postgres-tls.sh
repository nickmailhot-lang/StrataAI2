#!/usr/bin/env bash
set -euo pipefail

# The caller supplies its isolated PostgreSQL CI service, never a deployment.
# All earlier migration/RLS contracts have finished before this optional server
# transport is enabled; existing explicit local plaintext clients remain valid.
container="${1:?Pass the isolated PostgreSQL service container ID}"
configuration="${2:-Release}"
case "$configuration" in Debug|Release) ;; *) exit 1 ;; esac
# Git Bash local verification uses native Windows Docker/.NET executables.
host_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s\n' "$1"; fi; }
if command -v cygpath >/dev/null 2>&1; then export MSYS_NO_PATHCONV=1; fi
temp_root="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
scratch="$(mktemp -d "$temp_root/strataai-postgres-tls.XXXXXXXX")"
scratch="$(cd "$scratch" && pwd -P)"
[[ "$scratch" == "$temp_root"/strataai-postgres-tls.* ]] || exit 1
trap 'rm -rf "$scratch"' EXIT
umask 077
openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
  -subj '/CN=localhost' -addext 'subjectAltName=DNS:localhost' \
  -keyout "$(host_path "$scratch/server.key")" -out "$(host_path "$scratch/server.crt")" > "$scratch/certificate.log" 2>&1 \
  || { echo 'TLS fixture server certificate generation failed.' >&2; exit 1; }
openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
  -subj '/CN=unrelated-test-root' \
  -keyout "$(host_path "$scratch/unrelated.key")" -out "$(host_path "$scratch/unrelated.crt")" >> "$scratch/certificate.log" 2>&1 \
  || { echo 'TLS fixture unrelated root generation failed.' >&2; exit 1; }
docker exec "$container" mkdir -m 700 /tmp/strataai-ci-tls
docker cp "$(host_path "$scratch/server.key")" "$container:/tmp/strataai-ci-tls/server.key" > /dev/null
docker cp "$(host_path "$scratch/server.crt")" "$container:/tmp/strataai-ci-tls/server.crt" > /dev/null
docker exec "$container" chown -R postgres:postgres /tmp/strataai-ci-tls
docker exec "$container" chmod 600 /tmp/strataai-ci-tls/server.key
docker exec -i "$container" psql -U postgres -d "${PGDATABASE:-strataai_ci}" -v ON_ERROR_STOP=1 <<'SQL'
ALTER SYSTEM SET ssl_cert_file = '/tmp/strataai-ci-tls/server.crt';
ALTER SYSTEM SET ssl_key_file = '/tmp/strataai-ci-tls/server.key';
ALTER SYSTEM SET ssl_min_protocol_version = 'TLSv1.2';
ALTER SYSTEM SET ssl = on;
SELECT pg_reload_conf();
SQL
ready=0
for attempt in $(seq 1 20); do
  if [ "$(docker exec "$container" psql -U postgres -d "${PGDATABASE:-strataai_ci}" -At -v ON_ERROR_STOP=1 -c 'SHOW ssl')" = on ]; then ready=1; break; fi
  sleep 1
done
test "$ready" = 1
export STRATAAI_CONTRACT_TLS_ROOT="$(host_path "$scratch/server.crt")"
export STRATAAI_CONTRACT_TLS_UNTRUSTED_ROOT="$(host_path "$scratch/unrelated.crt")"
dotnet run --project tests/StrataAI.Persistence.Contracts/StrataAI.Persistence.Contracts.csproj \
  --configuration "$configuration" --no-build -- --postgres-transport-only
