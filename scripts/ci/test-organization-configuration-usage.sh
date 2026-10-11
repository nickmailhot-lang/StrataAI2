#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable configuration usage fixture requires CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Configuration usage verification failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
jar="$scratch/owner.cookies"
request() {
  curl --max-time 30 --silent --show-error -b "$jar" -X "$1" -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: ${4:-$(uuid)}" -d "$3" \
    -o "$scratch/response.json" -w '%{http_code}' "$base$2"
}
get() { curl --max-time 30 --silent --show-error -b "$jar" -D "$scratch/headers" -o "$scratch/response.json" -w '%{http_code}' "$base$1"; }
account() {
  local credentials actor
  credentials=$(jq -nc --arg email "configuration-usage-$(uuid)@example.test" '{email:$email,password:"Configuration-usage-correct-horse!",displayName:"Private usage fixture"}')
  test "$(request POST /auth/register "$credentials")" = 201
  actor=$(jq -er '.user.id' "$scratch/response.json")
  [[ "$actor" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
  admin "UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE id='$actor';" >/dev/null
  curl --max-time 30 --fail --silent --show-error -c "$jar" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null
}
account
test "$(request POST /organizations '{"name":"Private usage Organization"}')" = 201
org=$(jq -er '.organization.id' "$scratch/response.json")
path="/organizations/$org/configuration"
usage() {
  test "$(get "$path/usage")" = 200
  grep -qi '^cache-control:.*private' "$scratch/headers"
  grep -qi '^cache-control:.*no-store' "$scratch/headers"
  jq -e --arg org "$org" --argjson count "$1" 'keys==["adopted","committedChangeCount","organizationId"] and .organizationId==$org and .committedChangeCount==$count and .adopted==($count>0)' "$scratch/response.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Private|legalName|managementCompany|actorId|correlationId|eventId' "$scratch/response.json"
}
usage 0
key=$(uuid)
first='{"version":0,"configuration":{"legalName":"Private original legal name","jurisdiction":"CA-BC","timezone":"UTC"}}'
test "$(request PATCH "$path" "$first" "$key")" = 200
usage 1
test "$(request PATCH "$path" '{"version":1,"configuration":{"legalName":"Private later legal name","jurisdiction":"CA-ON","timezone":"America/Toronto"}}')" = 200
usage 2
test "$(request PATCH "$path" "$first" "$key")" = 200
jq -e '.version==1' "$scratch/response.json" >/dev/null
usage 2
test "$(request PATCH "$path" "$first")" = 409
test "$(request PATCH "$path" '{"version":2,"configuration":{"legalName":"Private invalid","jurisdiction":"CA-BC","timezone":"UTC","lotCount":0}}')" = 400
usage 2
for target in SUSPENDED DEACTIVATED; do
  test "$(get "/organizations/$org/operational-lifecycle")" = 200
  version=$(jq -er '.version' "$scratch/response.json")
  test "$(request PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --arg status "$target" --argjson version "$version" '{status:$status,version:$version}')")" = 200
  test "$(get "$path/usage")" = 404
  scripts/ci/assert-file-excludes.sh 'Private|committedChangeCount|adopted' "$scratch/response.json"
  test "$(get "/organizations/$org/operational-lifecycle")" = 200
  version=$(jq -er '.version' "$scratch/response.json")
  test "$(request PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --argjson version "$version" '{status:"ACTIVE",version:$version}')")" = 200
  usage 2
done
jar="$scratch/other.cookies"
account
test "$(get "$path/usage")" = 404
scripts/ci/assert-file-excludes.sh 'Private|committedChangeCount|adopted' "$scratch/response.json"
jar="$scratch/anonymous.cookies"
test "$(get "$path/usage")" = 401
echo 'Organization configuration adoption and committed-use projection passed; private counts remain tenant scoped.'
