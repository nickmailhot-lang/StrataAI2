#!/usr/bin/env bash
set -euo pipefail
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1" >/dev/null; }
required_versions=()
migration_values=''
for migration in db/migrations/[0-9][0-9][0-9]_*.sql; do
  candidate="${migration##*/}"; candidate="${candidate%.sql}"
  [[ "$candidate" =~ ^[0-9]{3}_[a-z0-9_]+$ ]] || { echo 'Invalid migration fixture identity' >&2; exit 1; }
  if [[ "${candidate:0:3}" > 008 ]]; then
    required_versions+=("$candidate")
    migration_values+="${migration_values:+,}('$candidate')"
  fi
done
restore() { admin "ALTER ROLE strataai_api_runtime NOBYPASSRLS; ALTER ROLE strataai_worker_runtime NOBYPASSRLS; GRANT EXECUTE ON FUNCTION public.runtime_database_role_is_safe() TO strataai_api_runtime; INSERT INTO schema_migrations(version) VALUES $migration_values ON CONFLICT DO NOTHING;"; }
trap 'echo "Runtime database security fixture failed at line $LINENO; migration=${version:-none}; response_status=${code:-none}" >&2' ERR
trap restore EXIT
status() { curl --silent --show-error --output /dev/null --write-out '%{http_code}' "$1"; }
test "$(status http://127.0.0.1:8080/readyz)" = 200
test "$(status http://127.0.0.1:8081/readyz)" = 200
admin 'ALTER ROLE strataai_api_runtime BYPASSRLS; ALTER ROLE strataai_worker_runtime BYPASSRLS;'
test "$(status http://127.0.0.1:8080/readyz)" = 503
test "$(status http://127.0.0.1:8081/readyz)" = 503
test "$(status http://127.0.0.1:8080/api/health)" = 200
body=$(mktemp)
trap 'rm -f "$body"; restore' EXIT
code=$(curl --silent --show-error --output "$body" --write-out '%{http_code}' -H 'Content-Type: application/json' -H 'X-StrataAI-Request: 1' -d '{"email":"guard-check@example.test","password":"not-a-real-password"}' http://127.0.0.1:8080/auth/login)
test "$code" = 503
jq -e '.code == "runtime_database_role_unsafe" and .status == 503' "$body" >/dev/null
code=$(curl --silent --show-error --output "$body" --write-out '%{http_code}' http://127.0.0.1:8080/boards/11111111-1111-1111-1111-111111111111/sync)
test "$code" = 503
jq -e '.code == "runtime_database_role_unsafe"' "$body" >/dev/null
restore
test "$(status http://127.0.0.1:8080/readyz)" = 200
test "$(status http://127.0.0.1:8081/readyz)" = 200
admin 'REVOKE EXECUTE ON FUNCTION public.runtime_database_role_is_safe() FROM strataai_api_runtime;'
test "$(status http://127.0.0.1:8080/readyz)" = 503
restore
test "$(status http://127.0.0.1:8080/readyz)" = 200
for version in "${required_versions[@]}"; do
# Each migration independently tests the protected login boundary. Keep these
# requests below the unchanged production 60/minute budget, including the
# earlier unsafe-role request, rather than exhausting it as the schema grows.
sleep 2
admin "DELETE FROM schema_migrations WHERE version='$version';"
test "$(status http://127.0.0.1:8080/readyz)" = 503
test "$(status http://127.0.0.1:8081/readyz)" = 503
code=$(curl --silent --show-error --output "$body" --write-out '%{http_code}' -H 'Content-Type: application/json' -H 'X-StrataAI-Request: 1' -d '{"email":"schema-check@example.test","password":"not-a-real-password"}' http://127.0.0.1:8080/auth/login)
test "$code" = 503
jq -e '.code == "runtime_database_schema_incompatible"' "$body" >/dev/null
code=$(curl --silent --show-error --output "$body" --write-out '%{http_code}' http://127.0.0.1:8080/boards/11111111-1111-1111-1111-111111111111/sync)
test "$code" = 503
jq -e '.code == "runtime_database_schema_incompatible"' "$body" >/dev/null
restore
test "$(status http://127.0.0.1:8080/readyz)" = 200
done

echo 'Exact images reject unsafe roles and recover after permissions are restored.'
