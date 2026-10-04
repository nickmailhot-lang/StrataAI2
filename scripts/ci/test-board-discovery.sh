#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'This disposable database fixture may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Board discovery check failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1" >/dev/null; }
request() {
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -b "$scratch/$1.cookies" -X "$2" -d "$4" "$BASE_URL$3"
}
for account in owner member outsider; do
  email="discovery-${account}-${RANDOM}-${RANDOM}@example.test"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg email "$email" '{email:$email,password:"discovery-correct-horse-battery",displayName:"Discovery fixture"}')" "$BASE_URL/auth/register" > "$scratch/$account.json"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -c "$scratch/$account.cookies" \
    -d "$(jq -nc --arg email "$email" '{email:$email,password:"discovery-correct-horse-battery"}')" "$BASE_URL/auth/login" >/dev/null
done
member_id="$(jq -r '.user.id' "$scratch/member.json")"
organization_id="$(request owner POST /organizations '{"name":"Private discovery fixture"}' | jq -r '.organization.id')"
for id in "$member_id" "$organization_id"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role) VALUES (gen_random_uuid(),'$organization_id','$member_id','MEMBER');"
for visibility in PRIVATE ORGANIZATION PUBLIC; do
  request owner POST /boards "$(jq -nc --arg org "$organization_id" --arg visibility "$visibility" '{organizationId:$org,name:("Discovery "+$visibility),visibility:$visibility}')" > "$scratch/$visibility.json"
done
private_id="$(jq -r '.id' "$scratch/PRIVATE.json")"
[[ "$private_id" =~ ^[0-9a-fA-F-]{36}$ ]]
list() { curl --fail --silent --show-error -b "$scratch/$1.cookies" "$BASE_URL/organizations/$organization_id/boards"; }
list owner | jq -e 'length == 3' >/dev/null
list member | jq -e 'length == 2 and all(.[]; .name != "Discovery PRIVATE")' >/dev/null
status="$(curl --silent --show-error -b "$scratch/outsider.cookies" -o "$scratch/denied.json" -w '%{http_code}' "$BASE_URL/organizations/$organization_id/boards")"
test "$status" = 404
scripts/ci/assert-file-excludes.sh 'Discovery PRIVATE' "$scratch/denied.json"
admin "INSERT INTO board_members(id,tenant_id,board_id,user_id,role,created_at,updated_at) VALUES (gen_random_uuid(),'$organization_id','$private_id','$member_id','MEMBER',now(),now());"
list member | jq -e 'length == 3' >/dev/null
request owner POST "/boards/$private_id/archive" "$(jq -nc --argjson version "$(jq '.version' "$scratch/PRIVATE.json")" '{version:$version}')" > "$scratch/archived.json"
list owner | jq -e 'length == 2 and all(.[]; .name != "Discovery PRIVATE")' >/dev/null
list member | jq -e 'length == 2 and all(.[]; .name != "Discovery PRIVATE")' >/dev/null
curl --fail --silent --show-error -b "$scratch/owner.cookies" "$BASE_URL/organizations/$organization_id/archived-boards" | jq -e --arg id "$private_id" 'any(.items[]; .id == $id)' >/dev/null
request owner POST "/boards/$private_id/restore" "$(jq -nc --argjson version "$(jq '.version' "$scratch/archived.json")" '{version:$version}')" > "$scratch/restored.json"
list owner | jq -e 'length == 3' >/dev/null
list member | jq -e 'length == 3' >/dev/null
admin "UPDATE board_members SET status='REMOVED' WHERE board_id='$private_id' AND user_id='$member_id';"
list member | jq -e 'length == 2' >/dev/null
admin "UPDATE organization_members SET role='ADMIN' WHERE tenant_id='$organization_id' AND user_id='$member_id';"
list member | jq -e 'length == 3' >/dev/null
admin "UPDATE boards SET lifecycle_state='DELETED' WHERE id='$private_id';"
list owner | jq -e 'length == 2' >/dev/null
list member | jq -e 'length == 2' >/dev/null
organization_board_id="$(jq -r '.id' "$scratch/ORGANIZATION.json")"
public_board_id="$(jq -r '.id' "$scratch/PUBLIC.json")"
for id in "$organization_board_id" "$public_board_id"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO board_members(id,tenant_id,board_id,user_id,role,created_at,updated_at) VALUES (gen_random_uuid(),'$organization_id','$organization_board_id','$member_id','ADMIN',now(),now()), (gen_random_uuid(),'$organization_id','$public_board_id','$member_id','ADMIN',now(),now());"
for membership_status in SUSPENDED REMOVED; do
admin "UPDATE organization_members SET status='$membership_status' WHERE tenant_id='$organization_id' AND user_id='$member_id';"
status="$(curl --silent --show-error -b "$scratch/member.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/organizations/$organization_id/boards")"
test "$status" = 404
status="$(curl --silent --show-error -b "$scratch/member.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/boards/$organization_board_id")"
test "$status" = 404
curl --fail --silent --show-error -b "$scratch/member.cookies" "$BASE_URL/boards/$public_board_id" | jq -e '.access.canEdit == false and .access.canAdminister == false and .access.canMove == false' >/dev/null
status="$(curl --silent --show-error -b "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Revoked contributor write"}' -o /dev/null -w '%{http_code}' "$BASE_URL/boards/$public_board_id/lists")"
test "$status" = 404
done
echo 'Exact release API filters private/archived/deleted board discovery, restores active entries and enforces membership revocation using restricted PostgreSQL credentials.'
