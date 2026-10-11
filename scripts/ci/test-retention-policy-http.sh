#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Retention HTTP verification requires disposable CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Retention HTTP verification failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
jar="$scratch/owner.cookies"
request() {
 curl --max-time 30 --silent --show-error -b "$jar" -X "$1" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -H "Idempotency-Key: ${4:-$(uuid)}" -d "$3" -D "$scratch/headers" -o "$scratch/response.json" -w '%{http_code}' "$base$2"
}
get() { curl --max-time 30 --silent --show-error -b "$jar" -D "$scratch/headers" -o "$scratch/response.json" -w '%{http_code}' "$base$1"; }
account() {
 local credentials actor
 credentials=$(jq -nc --arg email "retention-$(uuid)@example.test" '{email:$email,password:"Retention-policy-correct-horse!",displayName:"Retention fixture owner"}')
 test "$(request POST /auth/register "$credentials")" = 201
 actor=$(jq -er '.user.id' "$scratch/response.json")
 [[ "$actor" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
 admin "UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE id='$actor';" >/dev/null
 curl --max-time 30 --fail --silent --show-error -c "$jar" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null
}
account
test "$(request POST /organizations '{"name":"Private retention Organization"}')" = 201
org=$(jq -er '.organization.id' "$scratch/response.json")
[[ "$org" =~ ^[0-9a-f-]{36}$ ]]
directory="/organizations/$org/retention-policies"
policy=$(uuid); path="$directory/$policy"; key=$(uuid)
first='{"version":0,"policy":{"recordClass":"document","category":"contracts","jurisdiction":"CA-BC","anchor":"closed","retentionDays":365,"action":"preserve","source":"Private retention source","notes":"Private retention notes"}}'
test "$(get "$directory")" = 200
jq -e '.items==[] and .nextAfter==null' "$scratch/response.json" >/dev/null
test "$(request PATCH "$path" "$first" "$key")" = 200
grep -qi '^cache-control:.*private' "$scratch/headers"
grep -qi '^cache-control:.*no-store' "$scratch/headers"
jq -e '.version==1 and .recordClass=="document" and .action=="preserve"' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/first.json"
test "$(get "$path")" = 200
cmp "$scratch/first.json" "$scratch/response.json"
second=$(jq -c '.version=1 | .policy.retentionDays=730' <<< "$first")
test "$(request PATCH "$path" "$second")" = 200
test "$(request PATCH "$path" "$first" "$key")" = 200
cmp "$scratch/first.json" "$scratch/response.json"
test "$(request PATCH "$path" "$second" "$key")" = 409
test "$(request PATCH "$path" "$first")" = 409
test "$(get "$path/history")" = 200
jq -e '[.items[].policy.version]==[2,1] and .nextBeforeVersion==null and all(.items[];.actorLabel=="Retention fixture owner")' "$scratch/response.json" >/dev/null
scripts/ci/assert-file-excludes.sh 'fingerprint|keyId' "$scratch/response.json"
test "$(get "$path/history?beforeVersion=2")" = 200
jq -e '[.items[].policy.version]==[1]' "$scratch/response.json" >/dev/null
test "$(request PATCH "$directory/$(uuid)" "$first")" = 409
test "$(request PATCH "$path" '{"version":2,"policy":{"unsupported":true}}')" = 400
test "$(admin "SELECT count(*) FROM record_retention_policy_history WHERE tenant_id='$org' AND policy_id='$policy';")" = 2
test "$(admin "SELECT count(*) FROM record_retention_policy_receipts WHERE tenant_id='$org' AND policy_id='$policy';")" = 2
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND entity_id='$policy' AND event_type='RECORD_RETENTION_POLICY_CHANGED';")" = 2
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND entity_id='$policy' AND (safe_metadata::text LIKE '%Private%' OR (SELECT count(*) FROM jsonb_object_keys(safe_metadata))<>4);")" = 0
owner_jar="$jar"; jar="$scratch/foreign.cookies"; account
test "$(get "$path/history?beforeVersion=invalid")" = 404
scripts/ci/assert-file-excludes.sh 'Private|Retention fixture|contracts|recordClass' "$scratch/response.json"
test "$(request PATCH "$path" '{"invalid":true}')" = 404
jar="$scratch/anonymous.cookies"
test "$(get "$directory")" = 401
jar="$owner_jar"
for status in SUSPENDED DEACTIVATED; do
 test "$(get "/organizations/$org/operational-lifecycle")" = 200
 version=$(jq -er '.version' "$scratch/response.json")
 test "$(request PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --arg status "$status" --argjson version "$version" '{status:$status,version:$version}')")" = 200
 test "$(get "$directory")" = 404
 test "$(request PATCH "$path" "$first" "$key")" = 404
 test "$(get "/organizations/$org/operational-lifecycle")" = 200
 version=$(jq -er '.version' "$scratch/response.json")
 test "$(request PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --argjson version "$version" '{status:"ACTIVE",version:$version}')")" = 200
 test "$(get "$path")" = 200
 jq -e '.version==2' "$scratch/response.json" >/dev/null
done
echo 'Retention policy private HTTP, canonical persistence, audit, retries, seeks and authority checks passed.'
