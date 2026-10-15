#!/usr/bin/env bash
set -euo pipefail
# ARCH-11-FR-052/082: execute only the assembled operator payload and loaded images.
test "$#" -eq 1
script_path=${BASH_SOURCE[0]}
if [[ "$script_path" != /* && "$script_path" != [A-Za-z]:/* ]]; then script_path="$PWD/$script_path"; fi
cd "$1"
test -f build-metadata.json
test ! -e .env
test ! -L .env
for source in .git src apps; do test ! -e "$source"; done
# This is CI orchestration; runtime commands still use only the payload.
verification_dir=${script_path%/*}
revision=$(jq -er '.commitSha' build-metadata.json)
version=$(jq -er '.version' build-metadata.json)
[[ "$revision" =~ ^([a-f0-9]{40}|[a-f0-9]{64})$ ]]
project="strataai-bundle-smoke-$(openssl rand -hex 8)"
export COMPOSE_PROJECT_NAME="$project"
export POSTGRES_DB=strataai_release_smoke POSTGRES_USER=strataai_release_admin
export POSTGRES_PASSWORD=$(openssl rand -hex 24)
STRATAAI_API_DB_PASSWORD=$(openssl rand -hex 24)
STRATAAI_WORKER_DB_PASSWORD=$(openssl rand -hex 24)
export STRATAAI_API_DB_PASSWORD STRATAAI_WORKER_DB_PASSWORD
export STRATAAI_AUTH_RETRY_CURRENT_KEY=release-smoke-v1
key=$(openssl rand -base64 32)
export STRATAAI_AUTH_RETRY_KEYS="{\"release-smoke-v1\":\"$key\"}"
export STRATAAI_WEB_IMAGE="strataai-web:$revision"
export STRATAAI_API_IMAGE="strataai-api:$revision"
export STRATAAI_WORKER_IMAGE="strataai-worker:$revision"
if [ "${GITHUB_ACTIONS:-}" = true ]; then
  for secret in "$POSTGRES_PASSWORD" "$STRATAAI_API_DB_PASSWORD" "$STRATAAI_WORKER_DB_PASSWORD" "$key"; do
    echo "::add-mask::$secret"
  done
fi
compose=(docker compose --project-name "$project" --env-file .env -f compose.release.yml)
inspection_file=''
runtime_file=''
cleanup() {
  local code=$? remaining resource
  trap - EXIT
  if ! "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1; then code=1; fi
  rm -f .env
  if [ -n "$inspection_file" ]; then rm -f "$inspection_file"; fi
  if [ -n "$runtime_file" ]; then rm -f "$runtime_file"; fi
  for resource in container volume network; do
    if [ "$resource" = container ]; then
      if ! remaining=$(docker ps -aq --filter "label=com.docker.compose.project=$project"); then code=1; continue; fi
    else
      if ! remaining=$(docker "$resource" ls -q --filter "label=com.docker.compose.project=$project"); then code=1; continue; fi
    fi
    if [ -n "$remaining" ]; then code=1; fi
  done
  exit "$code"
}
umask 077
# No existing operator settings are replaced; only this disposable test owns .env.
set -o noclobber
: > .env
set +o noclobber
trap cleanup EXIT
write_environment() {
  printf '%s\n' "POSTGRES_DB=$POSTGRES_DB" "POSTGRES_USER=$POSTGRES_USER" "POSTGRES_PASSWORD=$POSTGRES_PASSWORD" \
    "STRATAAI_API_DB_PASSWORD=$STRATAAI_API_DB_PASSWORD" "STRATAAI_WORKER_DB_PASSWORD=$STRATAAI_WORKER_DB_PASSWORD" \
    "STRATAAI_AUTH_RETRY_CURRENT_KEY=$STRATAAI_AUTH_RETRY_CURRENT_KEY" "STRATAAI_AUTH_RETRY_KEYS='$STRATAAI_AUTH_RETRY_KEYS'" \
    "STRATAAI_WEB_IMAGE=$STRATAAI_WEB_IMAGE" "STRATAAI_API_IMAGE=$STRATAAI_API_IMAGE" "STRATAAI_WORKER_IMAGE=$STRATAAI_WORKER_IMAGE"
} > .env
write_environment
declare -A loaded_image_ids
for host in web api worker; do
  gunzip -c "images/strataai-$host.tar.gz" | docker load >/dev/null
  loaded_image_ids[$host]=$(docker image inspect --format '{{.Id}}' "strataai-$host:$revision")
done
inspection_file=$(mktemp "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/strataai-bundle-images.XXXXXX")
docker image inspect "$STRATAAI_WEB_IMAGE" "$STRATAAI_API_IMAGE" "$STRATAAI_WORKER_IMAGE" > "$inspection_file"
python3 "$verification_dir/verify-image-labels.py" --metadata build-metadata.json --images "$inspection_file" \
  --expected images/image-provenance.json --archives images
chmod +x apply-migrations.sh migration-stream.sh provision-runtime-roles.sh health-check.sh
"${compose[@]}" up -d --wait --wait-timeout 120 postgres >/dev/null
COMPOSE_FILE=compose.release.yml bash apply-migrations.sh >/dev/null
COMPOSE_FILE=compose.release.yml bash provision-runtime-roles.sh >/dev/null
"${compose[@]}" up -d --wait --wait-timeout 180 >/dev/null
runtime_file=$(mktemp "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/strataai-bundle-runtime.XXXXXX")
check_runtime() {
  local endpoint status host container actual expected
  bash health-check.sh >/dev/null
  for endpoint in http://127.0.0.1:8088/build-metadata.json http://127.0.0.1:8080/api/runtime http://127.0.0.1:8081/runtime; do
    status=$(curl --silent --show-error --connect-timeout 5 --max-time 15 \
      --output "$runtime_file" --write-out '%{http_code}' "$endpoint")
    test "$status" = 200
    jq -e --arg revision "$revision" --arg version "$version" \
      '.revision == $revision and .version == $version' "$runtime_file" >/dev/null
  done
  for host in web api worker; do
    container=$("${compose[@]}" ps -q "$host")
    actual=$(docker inspect --format '{{.Image}}' "$container")
    expected=${loaded_image_ids[$host]}
    test "$actual" = "$expected"
  done
}
check_runtime
api=$("${compose[@]}" ps -q api)
worker=$("${compose[@]}" ps -q worker)
"${compose[@]}" stop --timeout 20 api worker >/dev/null
for container in "$api" "$worker"; do
  test "$(docker inspect --format '{{.State.ExitCode}}' "$container")" = 0
  test "$(docker inspect --format '{{.State.OOMKilled}}' "$container")" = false
done
# Older Compose v2 runners support waiting on up, but not on start. Restart
# only these existing hosts and keep the same-container assertions below.
"${compose[@]}" up --no-deps --no-recreate --wait --wait-timeout 120 api worker >/dev/null
test "$("${compose[@]}" ps -q api)" = "$api"
test "$("${compose[@]}" ps -q worker)" = "$worker"
check_runtime
# ARCH-12-AC-003: rotate only this gate's disposable runtime-role secrets.
postgres_before=$("${compose[@]}" ps -q postgres)
volume_format='{{range .Mounts}}{{if eq .Destination "/var/lib/postgresql/data"}}{{.Name}}{{end}}{{end}}'
volume_before=$(docker inspect --format "$volume_format" "$postgres_before")
test -n "$volume_before"
ledger_sql="SELECT md5(string_agg(version, ',' ORDER BY version)) FROM schema_migrations"
ledger_before=$("${compose[@]}" exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -v ON_ERROR_STOP=1 -c "$ledger_sql")
[[ "$ledger_before" =~ ^[a-f0-9]{32}$ ]]
old_api_password=$STRATAAI_API_DB_PASSWORD
old_worker_password=$STRATAAI_WORKER_DB_PASSWORD
STRATAAI_API_DB_PASSWORD=$(openssl rand -hex 24)
STRATAAI_WORKER_DB_PASSWORD=$(openssl rand -hex 24)
export STRATAAI_API_DB_PASSWORD STRATAAI_WORKER_DB_PASSWORD
test "$STRATAAI_API_DB_PASSWORD" != "$old_api_password"
test "$STRATAAI_WORKER_DB_PASSWORD" != "$old_worker_password"
test "$STRATAAI_API_DB_PASSWORD" != "$STRATAAI_WORKER_DB_PASSWORD"
if [ "${GITHUB_ACTIONS:-}" = true ]; then
  for secret in "$STRATAAI_API_DB_PASSWORD" "$STRATAAI_WORKER_DB_PASSWORD"; do echo "::add-mask::$secret"; done
fi
write_environment
"${compose[@]}" stop --timeout 20 api worker >/dev/null
# The supplied provisioner reads secrets from the PostgreSQL container's
# environment, so refresh it while keeping its existing named data volume.
"${compose[@]}" up -d --no-deps --force-recreate --wait --wait-timeout 120 postgres >/dev/null
postgres_after=$("${compose[@]}" ps -q postgres)
test "$postgres_after" != "$postgres_before"
volume_after=$(docker inspect --format "$volume_format" "$postgres_after")
test "$volume_after" = "$volume_before"
COMPOSE_FILE=compose.release.yml bash provision-runtime-roles.sh >/dev/null
for host in api worker; do
  password=$STRATAAI_API_DB_PASSWORD; old_password=$old_api_password
  if [ "$host" = worker ]; then password=$STRATAAI_WORKER_DB_PASSWORD; old_password=$old_worker_password; fi
  ledger_after=$("${compose[@]}" exec -T -e "PGPASSWORD=$password" postgres psql \
    -h postgres -U "strataai_${host}_runtime" -d "$POSTGRES_DB" -At -v ON_ERROR_STOP=1 -c "$ledger_sql")
  test "$ledger_after" = "$ledger_before"
  if refusal=$("${compose[@]}" exec -T -e "PGPASSWORD=$old_password" postgres psql \
    -h postgres -U "strataai_${host}_runtime" -d "$POSTGRES_DB" -At -v ON_ERROR_STOP=1 -c 'SELECT 1' 2>&1); then
    echo 'Rotated runtime role still accepts its previous password.' >&2; exit 1
  fi
  [[ "$refusal" == *'password authentication failed'* ]]
done
unset password old_password old_api_password old_worker_password refusal
"${compose[@]}" up -d --no-deps --force-recreate --wait --wait-timeout 120 api worker >/dev/null
test "$("${compose[@]}" ps -q api)" != "$api"
test "$("${compose[@]}" ps -q worker)" != "$worker"
check_runtime

echo 'Assembled bundle migrations, restricted Production startup, exact image identity, health/proxy, graceful restart and runtime password rotation passed.'
