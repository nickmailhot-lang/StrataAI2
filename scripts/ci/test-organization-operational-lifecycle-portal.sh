#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable Portal lifecycle fixtures require CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Portal lifecycle isolation failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
request() {
  local actor=$1 method=$2 path=$3 body=$4 key=${5:-$(uuid)}
  curl --max-time 30 --silent --show-error -b "$scratch/$actor.cookies" \
    -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $key" \
    -X "$method" -d "$body" -o "$scratch/response.json" -w '%{http_code}' "$base$path"
}
get() { curl --max-time 30 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/response.json" -w '%{http_code}' "$base$2"; }
for actor in owner portal; do
  email="lifecycle-portal-$actor-$(uuid)@example.test"
  credentials=$(jq -nc --arg email "$email" '{email:$email,password:"Lifecycle-portal-correct-horse!",displayName:"Portal lifecycle fixture"}')
  test "$(request "$actor" POST /auth/register "$credentials")" = 201
  user=$(jq -r '.user.id' "$scratch/response.json")
  [[ "$user" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
  # Activate only the newly registered disposable fixture account. Provider
  # verification is covered independently by the identity delivery checks.
  admin "UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE id='$user';" >/dev/null
  curl --max-time 30 --fail --silent --show-error -c "$scratch/$actor.cookies" \
    -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" > /dev/null
  if test "$actor" = portal; then recipient=$user; recipient_email=$email; fi
done
for state in SUSPENDED DEACTIVATED; do
  test "$(request owner POST /organizations '{"name":"Private Portal lifecycle fixture"}')" = 201
  org=$(jq -r '.organization.id' "$scratch/response.json")
  [[ "$org" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
  test "$(request owner POST "/organizations/$org/invitations" "$(jq -nc --arg email "$recipient_email" '{email:$email,surface:"PORTAL",targetRole:"OWNER"}')")" = 201
  invitation=$(jq -r '.id' "$scratch/response.json")
  [[ "$invitation" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
  test "$(request portal POST "/me/invitations/$invitation/accept" '{}')" = 200
  grant_snapshot() { admin "SELECT md5(jsonb_agg(to_jsonb(p) ORDER BY id)::text) FROM portal_access p WHERE tenant_id='$org' AND user_id='$recipient';"; }
  original_grant=$(grant_snapshot)
  test -n "$original_grant"
  boundaries() {
    local expected=$1
    test "$(get portal "/organizations/$org/surface-access?surface=PORTAL")" = "$expected"
    test "$(get portal "/organizations/$org/surface-access?surface=INTERNAL")" = 404
    test "$(get owner "/organizations/$org/surface-access?surface=PORTAL")" = 404
    for route in operational-lifecycle 'operational-lifecycle/events?afterVersion=0'; do
      test "$(get portal "/organizations/$org/$route")" = 404
      jq -e '.code=="organization_not_found"' "$scratch/response.json" >/dev/null
      ! grep -Fq 'Private Portal lifecycle fixture' "$scratch/response.json"
    done
    test "$(request portal PATCH "/organizations/$org/operational-lifecycle" '{"status":"SUSPENDED","version":1}')" = 404
    test "$(admin "SELECT count(*) FROM organization_members WHERE tenant_id='$org' AND user_id='$recipient';")" = 0
    test "$(grant_snapshot)" = "$original_grant"
  }
  boundaries 200
  key=$(uuid)
  test "$(request owner PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --arg state "$state" '{status:$state,version:1}')" "$key")" = 200
  jq -e --arg state "$state" '.version==2 and .status==$state' "$scratch/response.json" >/dev/null
  cp "$scratch/response.json" "$scratch/original.json"
  boundaries 404
  test "$(request owner PATCH "/organizations/$org/operational-lifecycle" '{"status":"ACTIVE","version":2}')" = 200
  jq -e '.version==3 and .status=="ACTIVE"' "$scratch/response.json" >/dev/null
  boundaries 200
  test "$(request owner PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --arg state "$state" '{status:$state,version:1}')" "$key")" = 200
  cmp "$scratch/original.json" "$scratch/response.json"
  test "$(get owner "/organizations/$org/operational-lifecycle")" = 200
  jq -e '.version==3 and .status=="ACTIVE"' "$scratch/response.json" >/dev/null
done
echo 'Production Portal lifecycle isolation passed: grants preserved, reactivation admitted, internal and lifecycle authority denied.'
