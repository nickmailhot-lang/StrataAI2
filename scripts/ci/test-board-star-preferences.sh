#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable star fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Board star check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
account() {
  local name="$1" email="star-${RANDOM}-${RANDOM}@example.test"
  local body
  body="$(jq -nc --arg email "$email" '{email:$email,password:"star-correct-horse-battery",displayName:"Star fixture"}')"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/$name.json"
  curl --fail --silent --show-error -c "$scratch/$name.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
}
request() {
  curl --max-time 30 --silent --show-error -b "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -X "$2" -d "$5" -D "$scratch/headers" -o "$scratch/response" -w '%{http_code}' "$BASE_URL$3"
}
preference() {
  test "$(request "$1" GET "/boards/$board/star" "$(uuid)" '')" = 200
  jq -e --arg org "$organization" --arg board "$board" --arg user "$2" --argjson starred "$3" '
    (keys == ["boardId","organizationId","starred","userId"]) and
    .organizationId == $org and .boardId == $board and .userId == $user and .starred == $starred
  ' "$scratch/response" >/dev/null
  tr -d '\r' < "$scratch/headers" | grep -Ei '^cache-control:.*private' >/dev/null
  tr -d '\r' < "$scratch/headers" | grep -Ei '^cache-control:.*no-store' >/dev/null
}
account owner
account member
owner_id="$(jq -r '.user.id' "$scratch/owner.json")"
member_id="$(jq -r '.user.id' "$scratch/member.json")"
test "$(request owner POST /organizations "$(uuid)" '{"name":"Personal star isolation"}')" = 201
organization="$(jq -r '.organization.id' "$scratch/response")"
for id in "$owner_id" "$member_id" "$organization"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test "$(request owner POST /boards "$(uuid)" "$(jq -nc --arg org "$organization" '{organizationId:$org,name:"Private personal preferences"}')")" = 201
board="$(jq -r '.id' "$scratch/response")"
[[ "$board" =~ ^[0-9a-fA-F-]{36}$ ]]
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at) VALUES(gen_random_uuid(),'$organization','$member_id','MEMBER','ACTIVE',now(),now());" >/dev/null
test "$(request owner PATCH "/boards/$board/members/$member_id" "$(uuid)" '{"role":"MEMBER"}')" = 200
test "$(request owner GET "/boards/$board" "$(uuid)" '')" = 200
jq -S '{board,lists}' "$scratch/response" > "$scratch/before.json"
preference owner "$owner_id" false
preference member "$member_id" false
key="$(uuid)"
test "$(request owner PUT "/boards/$board/star" "$key" '')" = 204
test ! -s "$scratch/response"
preference owner "$owner_id" true
preference member "$member_id" false
# The same key belongs to a different actor namespace.
test "$(request member PUT "/boards/$board/star" "$key" '')" = 204
test "$(request owner DELETE "/boards/$board/star" "$(uuid)" '')" = 204
test "$(request owner PUT "/boards/$board/star" "$key" '')" = 204
preference owner "$owner_id" false
preference member "$member_id" true
test "$(request owner DELETE "/boards/$board/star" "$key" '')" = 409
jq -e '.code == "idempotency_key_reused"' "$scratch/response" >/dev/null
test "$(request owner GET "/boards/$board" "$(uuid)" '')" = 200
jq -S '{board,lists}' "$scratch/response" > "$scratch/after.json"
cmp "$scratch/before.json" "$scratch/after.json"
# Retain the Board grant and preference to prove current Organization eligibility.
admin "UPDATE organization_members SET status='SUSPENDED' WHERE tenant_id='$organization' AND user_id='$member_id';" >/dev/null
for method in GET PUT; do
  test "$(request member "$method" "/boards/$board/star" "$key" '')" = 404
  jq -e '.code == "board_not_found"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh 'starred|Private personal preferences|Npgsql|user_board_preferences|work_command_replays' "$scratch/response"
done
test "$(curl --max-time 30 --silent --show-error -o "$scratch/response" -w '%{http_code}' "$BASE_URL/boards/$board/star")" = 401
echo 'Exact release API proves private actor-scoped star reads, receipt isolation, replay without overwrite, unchanged shared records and revoked admission.'
