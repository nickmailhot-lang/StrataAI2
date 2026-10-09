#!/usr/bin/env bash
set -euo pipefail
# ARCH-11-FR-052/082: execute only the assembled operator payload and loaded images.
test "$#" -eq 1
cd "$1"
test -f build-metadata.json
test ! -e .env
test ! -L .env
for source in .git src apps; do test ! -e "$source"; done
revision=$(jq -er '.commitSha' build-metadata.json)
version=$(jq -er '.version' build-metadata.json)
[[ "$revision" =~ ^([a-f0-9]{40}|[a-f0-9]{64})$ ]]
project="strataai-bundle-smoke-$(openssl rand -hex 8)"
export COMPOSE_PROJECT_NAME="$project"
export POSTGRES_DB=strataai_release_smoke POSTGRES_USER=strataai_release_admin
export POSTGRES_PASSWORD=$(openssl rand -hex 24)
export STRATAAI_API_DB_PASSWORD=$(openssl rand -hex 24)
export STRATAAI_WORKER_DB_PASSWORD=$(openssl rand -hex 24)
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
cleanup() {
  local code=$?
  trap - EXIT
  if ! "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1; then code=1; fi
  rm -f .env
  if [ -n "$(docker ps -aq --filter "label=com.docker.compose.project=$project")" ] \
    || [ -n "$(docker volume ls -q --filter "label=com.docker.compose.project=$project")" ]; then code=1; fi
  exit "$code"
}
umask 077
# No existing operator settings are replaced; only this disposable test owns .env.
set -o noclobber
: > .env
set +o noclobber
trap cleanup EXIT
{
  printf '%s\n' "POSTGRES_DB=$POSTGRES_DB" "POSTGRES_USER=$POSTGRES_USER" "POSTGRES_PASSWORD=$POSTGRES_PASSWORD" \
    "STRATAAI_API_DB_PASSWORD=$STRATAAI_API_DB_PASSWORD" "STRATAAI_WORKER_DB_PASSWORD=$STRATAAI_WORKER_DB_PASSWORD" \
    "STRATAAI_AUTH_RETRY_CURRENT_KEY=$STRATAAI_AUTH_RETRY_CURRENT_KEY" "STRATAAI_AUTH_RETRY_KEYS='$STRATAAI_AUTH_RETRY_KEYS'" \
    "STRATAAI_WEB_IMAGE=$STRATAAI_WEB_IMAGE" "STRATAAI_API_IMAGE=$STRATAAI_API_IMAGE" "STRATAAI_WORKER_IMAGE=$STRATAAI_WORKER_IMAGE"
} > .env
for host in web api worker; do
  gunzip -c "images/strataai-$host.tar.gz" | docker load >/dev/null
  actual=$(docker image inspect --format '{{.Id}}' "strataai-$host:$revision")
  expected=$(jq -er --arg host "$host" '.imageIds[$host]' images/image-provenance.json)
  test "$actual" = "$expected"
done
chmod +x apply-migrations.sh migration-stream.sh provision-runtime-roles.sh health-check.sh
"${compose[@]}" up -d --wait --wait-timeout 120 postgres >/dev/null
COMPOSE_FILE=compose.release.yml bash apply-migrations.sh >/dev/null
COMPOSE_FILE=compose.release.yml bash provision-runtime-roles.sh >/dev/null
"${compose[@]}" up -d --wait --wait-timeout 180 >/dev/null
check_runtime() {
  bash health-check.sh >/dev/null
  for endpoint in http://127.0.0.1:8088/build-metadata.json http://127.0.0.1:8080/api/runtime http://127.0.0.1:8081/runtime; do
    curl --fail --silent --show-error "$endpoint" | jq -e --arg revision "$revision" --arg version "$version" \
      '.revision == $revision and .version == $version' >/dev/null
  done
  for host in web api worker; do
    container=$("${compose[@]}" ps -q "$host")
    actual=$(docker inspect --format '{{.Image}}' "$container")
    expected=$(jq -er --arg host "$host" '.imageIds[$host]' images/image-provenance.json)
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
"${compose[@]}" start --wait --wait-timeout 120 api worker >/dev/null
test "$("${compose[@]}" ps -q api)" = "$api"
test "$("${compose[@]}" ps -q worker)" = "$worker"
check_runtime
echo 'Assembled bundle migrations, restricted Production startup, exact image identity, health/proxy and graceful API/Worker restart passed.'
