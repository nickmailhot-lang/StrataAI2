#!/usr/bin/env bash
set -euo pipefail
# ARCH-10-AC-001: the assembled payload starts Demo from the retained images.
test "$#" -eq 1
cd "$1"
test -f build-metadata.json
test -f compose.demo.release.yml
test ! -e .env.demo
test ! -L .env.demo
for source in .git src apps; do test ! -e "$source"; done
revision=$(jq -er '.commitSha' build-metadata.json)
version=$(jq -er '.version' build-metadata.json)
[[ "$revision" =~ ^([a-f0-9]{40}|[a-f0-9]{64})$ ]]
project="strataai-demo-bundle-smoke-$(openssl rand -hex 8)"
compose=(docker compose --project-name "$project" --env-file .env.demo -f compose.demo.release.yml)
temp_root="$(cd "${TMPDIR:-/tmp}" && pwd -P)"
scratch="$(mktemp -d "$temp_root/strataai-demo-bundle.XXXXXXXX")"
scratch="$(cd "$scratch" && pwd -P)"
[[ "$scratch" == "$temp_root"/strataai-demo-bundle.* ]] || exit 1
owns_env=false
started=false
cleanup() {
  local code=$? resource remaining
  trap - EXIT
  if [ "$started" = true ]; then
    if ! "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1; then code=1; fi
    for resource in container volume network; do
      if [ "$resource" = container ]; then
        if ! remaining=$(docker ps -aq --filter "label=com.docker.compose.project=$project"); then code=1; continue; fi
      else
        if ! remaining=$(docker "$resource" ls -q --filter "label=com.docker.compose.project=$project"); then code=1; continue; fi
      fi
      if [ -n "$remaining" ]; then code=1; fi
    done
  fi
  if [ "$owns_env" = true ]; then rm -f .env.demo; fi
  rm -rf "$scratch"
  exit "$code"
}
trap cleanup EXIT
umask 077
set -o noclobber
: > .env.demo
owns_env=true
set +o noclobber
for host in web api worker; do
  printf 'STRATAAI_%s_IMAGE=strataai-%s:%s\n' "${host^^}" "$host" "$revision" >> .env.demo
done
printf '%s\n' STRATAAI_DEMO_WEB_PORT=0 >> .env.demo
services=$("${compose[@]}" config --services | sort | tr '\n' ' ')
test "$services" = 'api web worker '
started=true
"${compose[@]}" up -d --no-build --pull never --wait --wait-timeout 120 >/dev/null
for host in api worker web; do
  container=$("${compose[@]}" ps -q "$host")
  test -n "$container"
  health=$(docker inspect --format '{{.State.Health.Status}}' "$container")
  actual=$(docker inspect --format '{{.Image}}' "$container")
  expected=$(docker image inspect --format '{{.Id}}' "strataai-$host:$revision")
  test "$health" = healthy
  test "$actual" = "$expected"
done
volumes=$(docker volume ls -q --filter "label=com.docker.compose.project=$project")
test -z "$volumes"
web="http://$("${compose[@]}" port web 80)"
get() {
  local url=$1 status
  status=$(curl --connect-timeout 5 --max-time 15 --silent --show-error -o "$scratch/response" -w '%{http_code}' "$url")
  test "$status" = 200
}
get "$web/healthz"
get "$web/readyz"
internal_get() {
  local payload
  payload=$("${compose[@]}" exec -T "$1" curl --connect-timeout 5 --max-time 15 --silent --show-error \
    -w '\n%{http_code}' "http://127.0.0.1:$2$3")
  test "${payload##*$'\n'}" = 200
  printf '%s' "${payload%$'\n'*}" > "$scratch/response"
}
for host in api worker; do
  port=8080; path=/api/runtime
  if [ "$host" = worker ]; then port=8081; path=/runtime; fi
  internal_get "$host" "$port" /healthz
  internal_get "$host" "$port" /readyz
  internal_get "$host" "$port" "$path"
  jq -e --arg revision "$revision" --arg version "$version" \
    '.mode=="demo" and .revision==$revision and .version==$version' "$scratch/response" >/dev/null
done
get "$web/build-metadata.json"
jq -e --arg revision "$revision" --arg version "$version" '.revision==$revision and .version==$version' "$scratch/response" >/dev/null
get "$web/api/health"
get "$web/login"
grep -Fq 'id="root"' "$scratch/response"
nonce=$(openssl rand -hex 16)
[[ "$nonce" =~ ^[a-f0-9]{32}$ ]]
login_key="${nonce:0:8}-${nonce:8:4}-${nonce:12:4}-${nonce:16:4}-${nonce:20:12}"
status=$(curl --connect-timeout 5 --max-time 15 --silent --show-error \
  -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $login_key" \
  -d '{"email":"demo@strataai.test","password":"StrataAI-Demo-2026!"}' \
  -o "$scratch/response" -w '%{http_code}' "$web/auth/login")
test "$status" = 200
jq -e '.user.email=="demo@strataai.test"' "$scratch/response" >/dev/null
status=$(curl --connect-timeout 5 --max-time 15 --silent --show-error \
  -b "$scratch/cookies" -o "$scratch/response" -w '%{http_code}' "$web/me")
test "$status" = 200
jq -e '.email=="demo@strataai.test"' "$scratch/response" >/dev/null
echo 'Source-free Demo bundle: exact three images, no database/volumes, health/build identity, SPA and seeded sign-in passed.'
