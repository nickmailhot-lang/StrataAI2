#!/usr/bin/env bash
set -euo pipefail
trap 'echo "HTTPS bundle contract failed at line $LINENO." >&2' ERR
# Small source-free HTTPS contract; use only the supplied bundle and loaded images.
test "$#" = 1
cd "$1"
for source in .git src apps; do test ! -e "$source"; done
test -f compose.https.yml
revision=$(jq -er '.commitSha' build-metadata.json)
version=$(jq -er '.version' build-metadata.json)
[[ "$revision" =~ ^([a-f0-9]{40}|[a-f0-9]{64})$ ]]
umask 077
temp_root=$(cd "${TMPDIR:-/tmp}" && pwd -P)
scratch=$(mktemp -d "$temp_root/strataai-https-bundle.XXXXXXXX")
scratch=$(cd "$scratch" && pwd -P)
[[ "$(dirname "$scratch")" == "$temp_root" && "$(basename "$scratch")" == strataai-https-bundle.* ]]
project="strataai-https-smoke-$(openssl rand -hex 8)"
compose=(docker compose --project-name "$project" --env-file "$scratch/fixture.env" -f compose.release.yml -f compose.https.yml)
started=false
cleanup() {
  local code=$? resource remaining
  trap - EXIT
  if [ "$started" = true ]; then
    "${compose[@]}" down --volumes --remove-orphans >/dev/null 2>&1 || code=1
    for resource in container volume network; do
      if [ "$resource" = container ]; then
        remaining=$(docker ps -aq --filter "label=com.docker.compose.project=$project") || { code=1; continue; }
      else
        remaining=$(docker "$resource" ls -q --filter "label=com.docker.compose.project=$project") || { code=1; continue; }
      fi
      test -z "$remaining" || code=1
    done
  fi
  rm -rf -- "$scratch" || code=1
  exit "$code"
}
trap cleanup EXIT
host_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s\n' "$1"; fi; }
export COMPOSE_PROJECT_NAME="$project"
export POSTGRES_DB=https_fixture POSTGRES_USER=https_fixture_admin
export POSTGRES_PASSWORD=$(openssl rand -hex 24)
export STRATAAI_API_DB_PASSWORD=$(openssl rand -hex 24)
export STRATAAI_WORKER_DB_PASSWORD=$(openssl rand -hex 24)
export STRATAAI_AUTH_RETRY_CURRENT_KEY=https-fixture
export STRATAAI_AUTH_RETRY_KEYS="{\"https-fixture\":\"$(openssl rand -base64 32)\"}"
export STRATAAI_AUTH_ALLOW_SELF_REGISTRATION=true STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL=false
export STRATAAI_IDENTITY_EMAIL_ENABLED=false STRATAAI_INVITATION_EMAIL_ENABLED=false
export STRATAAI_PUBLIC_ORIGIN=https://localhost STRATAAI_REALTIME_PUBLIC_ORIGIN=https://localhost
export STRATAAI_HTTPS_PORT=0 STRATAAI_HTTPS_BIND_ADDRESS=127.0.0.1
mkdir "$scratch/certificates"
export STRATAAI_HTTPS_CERTIFICATE_DIRECTORY=$(host_path "$scratch/certificates")
for host in web api worker; do
  export "STRATAAI_${host^^}_IMAGE=strataai-$host:$revision"
done
if [ "${GITHUB_ACTIONS:-}" = true ]; then
  for secret in "$POSTGRES_PASSWORD" "$STRATAAI_API_DB_PASSWORD" "$STRATAAI_WORKER_DB_PASSWORD" "$STRATAAI_AUTH_RETRY_KEYS"; do
    echo "::add-mask::$secret"
  done
fi
: > "$scratch/fixture.env"
declare -A image_ids
for host in web api worker; do image_ids[$host]=$(docker image inspect --format '{{.Id}}' "strataai-$host:$revision"); done
"${compose[@]}" config --format json > "$scratch/model.json"
jq -e '(.services | keys == ["api","postgres","web","worker"]) and
  ([.services[] | has("build")] | all(. == false))' "$scratch/model.json" >/dev/null
jq -e '((.services.api.ports // []) | length == 0) and ((.services.worker.ports // []) | length == 0) and
  (.services.web.ports | length == 1 and .[0].target == 443)' "$scratch/model.json" >/dev/null
for kind in server wrong; do
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
    -subj /CN=localhost -addext subjectAltName=DNS:localhost \
    -keyout "$(host_path "$scratch/$kind.key")" -out "$(host_path "$scratch/$kind.crt")" > "$scratch/certificate.log" 2>&1
done
cp "$scratch/server.key" "$scratch/certificates/privkey.pem"
# Missing certificates and mismatched keys must fail before normal startup.
started=true
if "${compose[@]}" run --rm --no-deps --pull never web nginx -t > "$scratch/refusal.log" 2>&1; then exit 1; fi
grep -Fq 'cannot load certificate' "$scratch/refusal.log"
cp "$scratch/server.crt" "$scratch/certificates/fullchain.pem"
cp "$scratch/wrong.key" "$scratch/certificates/privkey.pem"
if "${compose[@]}" run --rm --no-deps --pull never web nginx -t > "$scratch/refusal.log" 2>&1; then exit 1; fi
grep -Fq 'key values mismatch' "$scratch/refusal.log"
cp "$scratch/server.key" "$scratch/certificates/privkey.pem"
"${compose[@]}" up -d --no-build --pull never --wait --wait-timeout 120 postgres >/dev/null
COMPOSE_FILE=compose.release.yml bash apply-migrations.sh >/dev/null
COMPOSE_FILE=compose.release.yml bash provision-runtime-roles.sh >/dev/null
"${compose[@]}" up -d --no-build --pull never --wait --wait-timeout 180 >/dev/null
binding=$("${compose[@]}" port web 443)
port=${binding##*:}
[[ "$port" =~ ^[0-9]+$ ]]
origin="https://localhost:$port"
export STRATAAI_PUBLIC_ORIGIN="$origin" STRATAAI_REALTIME_PUBLIC_ORIGIN="$origin"
# Docker can reassign a published port 0 even on restart. Pin the allocated
# port before asserting recovery of this same listener/session.
export STRATAAI_HTTPS_PORT="$port"
"${compose[@]}" up -d --no-deps --no-build --pull never --force-recreate --wait --wait-timeout 120 api worker web >/dev/null
test "$("${compose[@]}" port web 443)" = "$binding"
web_container=$("${compose[@]}" ps -q web)
for host in api worker web; do
  container=$("${compose[@]}" ps -q "$host")
  test "$(docker inspect --format '{{.Image}}' "$container")" = "${image_ids[$host]}"
  test "$(docker inspect --format '{{.State.Health.Status}}' "$container")" = healthy
  docker inspect --format '{{json .HostConfig.PortBindings}}' "$container" > "$scratch/ports.json"
  if [ "$host" = web ]; then
    jq -e 'keys == ["443/tcp"]' "$scratch/ports.json" >/dev/null
    docker inspect --format '{{json .Mounts}}' "$container" > "$scratch/mounts.json"
    jq -e '[.[] | select(.Destination=="/run/strataai-tls" or .Destination=="/etc/nginx/strataai-tls.inc")]
      | length==2 and all(.Type=="bind" and .RW==false)' "$scratch/mounts.json" >/dev/null
  else jq -e '(. // {}) | length == 0' "$scratch/ports.json" >/dev/null; fi
done
"${compose[@]}" exec -T web sh -c 'wget -T 15 -S -O /dev/null http://127.0.0.1/' > "$scratch/internal-http.log" 2>&1
if grep -Eiq '^[[:space:]]*Strict-Transport-Security:' "$scratch/internal-http.log"; then
  echo 'Private HTTP must not emit the HTTPS-only HSTS policy.' >&2; exit 1
fi
curl_args=(--noproxy '*' --connect-timeout 5 --max-time 15 --silent --show-error)
for url in "$origin/" "https://127.0.0.1:$port/"; do
  extra=(); if [[ "$url" == *127.0.0.1* ]]; then extra=(--cacert "$(host_path "$scratch/server.crt")"); fi
  code=0
  curl "${curl_args[@]}" "${extra[@]}" -o "$scratch/refused-response" "$url" > "$scratch/refusal.log" 2>&1 || code=$?
  test "$code" = 60
done
test "$(curl "${curl_args[@]}" -o "$scratch/response" -w '%{http_code}' "http://localhost:$port/api/health")" = 400
request() {
  local method=$1 path=$2 expected=$3 body=${4-} status
  local args=(-X "$method")
  if [ -n "$body" ]; then args+=(-H 'Content-Type: application/json' -d "$body"); fi
  status=$(curl "${curl_args[@]}" --cacert "$(host_path "$scratch/server.crt")" \
    -b "$(host_path "$scratch/cookies")" -c "$(host_path "$scratch/cookies")" \
    -H 'X-StrataAI-Request: 1' "${args[@]}" -D "$(host_path "$scratch/headers")" \
    -o "$(host_path "$scratch/response")" -w '%{http_code}' "$origin$path")
  test "$status" = "$expected"
  grep -Eiq '^Strict-Transport-Security: max-age=31536000' "$scratch/headers"
}
request GET /build-metadata.json 200
jq -e --arg revision "$revision" --arg version "$version" '.revision==$revision and .version==$version' "$scratch/response" >/dev/null
request GET /api/runtime 200
jq -e --arg revision "$revision" --arg version "$version" '.mode=="production" and .revision==$revision and .version==$version' "$scratch/response" >/dev/null
request GET /api/health 200
email="https-$(openssl rand -hex 8)@example.test"
password=$(openssl rand -hex 24)
credentials=$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password,displayName:"HTTPS fixture"}')
request POST /auth/register 201 "$credentials"
user=$(jq -er '.user.id' "$scratch/response")
request POST /auth/login 200 "$credentials"
grep -Eiq '^Set-Cookie: strataai_session=.*; secure' "$scratch/headers"
request GET /me 200
jq -e --arg user "$user" '.id==$user' "$scratch/response" >/dev/null
request POST /organizations 201 '{"name":"HTTPS Organization"}'
organization=$(jq -er '.organization.id' "$scratch/response")
request POST /boards 201 "$(jq -nc --arg org "$organization" '{organizationId:$org,name:"HTTPS Board"}')"
board=$(jq -er '.id' "$scratch/response")
request POST "/boards/$board/lists" 201 '{"name":"HTTPS List"}'
list=$(jq -er '.id' "$scratch/response")
request POST "/lists/$list/cards" 201 '{"title":"Task saved through HTTPS"}'
card=$(jq -er '.id' "$scratch/response")
request GET "/boards/$board" 200
jq -e --arg card "$card" '.lists | length==1 and .[0].cards[0].id==$card and .[0].cards[0].version==1' "$scratch/response" >/dev/null
cp "$scratch/response" "$scratch/before.json"
"${compose[@]}" restart --timeout 20 web >/dev/null
"${compose[@]}" up -d --no-deps --no-recreate --no-build --pull never --wait --wait-timeout 120 web >/dev/null
test "$("${compose[@]}" port web 443)" = "$binding"
test "$("${compose[@]}" ps -q web)" = "$web_container"
request GET /me 200
jq -e --arg user "$user" '.id==$user' "$scratch/response" >/dev/null
request GET "/boards/$board" 200
jq -e --slurpfile before "$scratch/before.json" '.==$before[0]' "$scratch/response" >/dev/null
echo 'Assembled HTTPS bundle passed: only TLS published, missing/mismatched key refusal, certificate trust/name verification, HSTS, Secure sign-in, persisted task graph and web restart; original images unchanged.'
