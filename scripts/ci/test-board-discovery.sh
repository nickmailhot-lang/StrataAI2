#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'This disposable database fixture may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
gate_pid=''; request_pid=''
cleanup() {
  if [ -n "$request_pid" ]; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  if [ -n "$gate_pid" ]; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Board discovery check failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1" >/dev/null; }
scalar() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
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
for value in unknown '#ffffff' 'url(https://example.test/private)' 'https://example.test/image.png'; do
  status="$(curl --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -X PATCH -d "$(jq -nc --arg value "$value" '{name:"Should not change",version:1,backgroundType:"COLOR",backgroundValue:$value}')" \
    -o "$scratch/background-denied.json" -w '%{http_code}' "$BASE_URL/boards/$private_id")"
  test "$status" = 400
  jq -e '.code == "invalid_background"' "$scratch/background-denied.json" >/dev/null
  curl --fail --silent --show-error -b "$scratch/owner.cookies" "$BASE_URL/boards/$private_id" | jq -e '.board.name == "Discovery PRIVATE" and .board.version == 1' >/dev/null
done
request owner PATCH "/boards/$private_id" '{"name":"Discovery PRIVATE","version":1,"backgroundType":" color ","backgroundValue":" PURPLE "}' > "$scratch/PRIVATE.json"
jq -e '.backgroundType == "COLOR" and .backgroundValue == "purple" and .version == 2' "$scratch/PRIVATE.json" >/dev/null
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
# Observe a real directory read waiting on the canonical Board gate, then
# withdraw only its membership grant. No Board tuple update may refresh the
# old snapshot on behalf of the reader.
admin "UPDATE board_members SET role='ADMIN' WHERE board_id='$private_id' AND user_id='$member_id';"
curl --fail --silent --show-error -b "$scratch/member.cookies" "$BASE_URL/organizations/$organization_id/archived-boards" | jq -e --arg id "$private_id" '(.items|length)==1 and .items[0].id==$id' >/dev/null
mkfifo "$scratch/gate.in"
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 & gate_pid=$!
exec 3> "$scratch/gate.in"
printf 'BEGIN;\nSELECT id FROM boards WHERE id=\047%s\047 FOR UPDATE;\n\\echo discovery_locked\n' "$private_id" >&3
for ((attempt=0;attempt<100;attempt++)); do
  if grep -q '^discovery_locked$' "$scratch/gate.log"; then break; fi
  kill -0 "$gate_pid"; sleep 0.05
done
grep -q '^discovery_locked$' "$scratch/gate.log"
curl --fail --silent --show-error -b "$scratch/member.cookies" "$BASE_URL/organizations/$organization_id/archived-boards" > "$scratch/withdrawn-directory.json" & request_pid=$!
for ((attempt=0;attempt<100;attempt++)); do
  if test "$(scalar "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%b.archived_at%FOR SHARE%';")" -ge 1; then break; fi
  sleep 0.05
done
test "$attempt" -lt 100
printf 'UPDATE board_members SET role=\047MEMBER\047 WHERE board_id=\047%s\047 AND user_id=\047%s\047;\nCOMMIT;\n\\q\n' "$private_id" "$member_id" >&3
exec 3>&-; wait "$gate_pid"; gate_pid=''
wait "$request_pid"; request_pid=''
jq -e '(.items|length)==0 and .nextCursor==null' "$scratch/withdrawn-directory.json" >/dev/null
scripts/ci/assert-file-excludes.sh 'Discovery PRIVATE' "$scratch/withdrawn-directory.json"
admin "UPDATE board_members SET role='ADMIN' WHERE board_id='$private_id' AND user_id='$member_id';"
curl --fail --silent --show-error -b "$scratch/member.cookies" "$BASE_URL/organizations/$organization_id/archived-boards" | jq -e --arg id "$private_id" '(.items|length)==1 and .items[0].id==$id' >/dev/null
admin "UPDATE board_members SET role='MEMBER' WHERE board_id='$private_id' AND user_id='$member_id';"
echo 'Archived Board directory rechecks a withdrawn membership after an observed Board gate wait.'
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

# The bounded active home directory is verified through the actual Nginx/API
# image pair. Restore the member fixture after the legacy revocation checks.
admin "UPDATE organization_members SET status='ACTIVE',role='MEMBER',version=version+1,updated_at=clock_timestamp() WHERE tenant_id='$organization_id' AND user_id='$member_id';
INSERT INTO boards(id,tenant_id,name,visibility,lifecycle_state,created_at,updated_at)
 SELECT ('e1000000-1111-4111-8000-' || lpad(n::text,12,'0'))::uuid,'$organization_id','Paged visible Board ' || n,'ORGANIZATION','ACTIVE',now(),now() FROM generate_series(1,52) n;
INSERT INTO boards(id,tenant_id,name,visibility,lifecycle_state,created_at,updated_at)
 SELECT ('01000000-1111-4111-8000-' || lpad(n::text,12,'0'))::uuid,'$organization_id','Hidden paged private Board','PRIVATE','ACTIVE',now(),now() FROM generate_series(1,51) n;
INSERT INTO boards(id,tenant_id,name,visibility,lifecycle_state,archived_at,created_at,updated_at)
 SELECT ('02000000-1111-4111-8000-' || lpad(n::text,12,'0'))::uuid,'$organization_id','Archived paged Board','ORGANIZATION','ARCHIVED',now(),now(),now() FROM generate_series(1,51) n;"
board_directory="$BASE_URL/organizations/$organization_id/boards/directory"
expected_ids="$(scalar "SELECT json_agg(b.id ORDER BY b.id) FROM boards b WHERE b.tenant_id='$organization_id' AND b.lifecycle_state='ACTIVE' AND (b.visibility<>'PRIVATE' OR EXISTS(SELECT 1 FROM board_members m WHERE m.tenant_id=b.tenant_id AND m.board_id=b.id AND m.user_id='$member_id' AND m.status='ACTIVE'));")"
board_state() { scalar "SELECT md5(json_build_object('organization',(SELECT row_to_json(o) FROM organizations o WHERE o.id='$organization_id'),'boards',(SELECT json_agg(b ORDER BY b.id) FROM boards b WHERE b.tenant_id='$organization_id'),'members',(SELECT json_agg(m ORDER BY m.id) FROM board_members m WHERE m.tenant_id='$organization_id'),'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$organization_id'))::text);"; }
state_before="$(board_state)"
curl --fail --silent --show-error -D "$scratch/board-page.headers" -b "$scratch/member.cookies" "$board_directory" > "$scratch/board-page.json"
jq -e --arg org "$organization_id" '.organizationId==$org and (.items|length)==50 and (.nextCursor|type)=="string"' "$scratch/board-page.json" >/dev/null
grep -qi 'cache-control:.*private.*no-store' "$scratch/board-page.headers"
scripts/ci/assert-file-excludes.sh 'Hidden paged private Board' "$scratch/board-page.json"
scripts/ci/assert-file-excludes.sh 'Archived paged Board' "$scratch/board-page.json"
board_cursor="$(jq -r '.nextCursor' "$scratch/board-page.json")"
[[ "$board_cursor" =~ ^[0-9a-f-]{36}$ ]]
curl --fail --silent --show-error -b "$scratch/member.cookies" "$board_directory?after=$board_cursor" > "$scratch/board-tail.json"
jq -e '(.items|length)==4 and .nextCursor==null' "$scratch/board-tail.json" >/dev/null
jq -s -e --argjson expected "$expected_ids" '([.[].items[].id] == $expected) and ([.[].items[].id]|unique|length)==54' "$scratch/board-page.json" "$scratch/board-tail.json" >/dev/null
for board_cursor in invalid 00000000-0000-0000-0000-000000000000 e1000000111141118000000000000001; do
  status="$(curl --silent --show-error -b "$scratch/member.cookies" -o "$scratch/board-invalid.json" -w '%{http_code}' "$board_directory?after=$board_cursor")"
  test "$status" = 400
  jq -e '.code=="invalid_board_directory_cursor"' "$scratch/board-invalid.json" >/dev/null
done
status="$(curl --silent --show-error -b "$scratch/outsider.cookies" -o "$scratch/board-outsider.json" -w '%{http_code}' "$board_directory")"
test "$status" = 404
scripts/ci/assert-file-excludes.sh 'Paged visible Board' "$scratch/board-outsider.json"
test "$(board_state)" = "$state_before"
admin "UPDATE organization_members SET status='REMOVED' WHERE tenant_id='$organization_id' AND user_id='$member_id';"
status="$(curl --silent --show-error -b "$scratch/member.cookies" -o "$scratch/board-revoked.json" -w '%{http_code}' "$board_directory")"
test "$status" = 404
scripts/ci/assert-file-excludes.sh 'Paged visible Board' "$scratch/board-revoked.json"
echo 'Exact-image active Board directory: pre-limit visibility/lifecycle filtering, complete 54-row seek, private caching, stable invalid cursors, unchanged persisted state and revoked member denial passed.'
