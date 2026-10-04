#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable star fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
restore() {
  if [ "${preference_permission_withdrawn:-0}" = 1 ]; then
    admin 'GRANT UPDATE ON user_board_preferences TO strataai_api_runtime;' >/dev/null
  fi
}
trap 'restore; rm -rf "$scratch"' EXIT
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
    (keys == ["boardId","createdAt","organizationId","starred","updatedAt","userId","version"]) and
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
test "$(request owner PUT "/boards/$board/star?version=0" "$key" '')" = 204
test ! -s "$scratch/response"
test "$(admin "SELECT version=1 AND created_at IS NOT NULL AND created_at=updated_at FROM user_board_preferences WHERE board_id='$board' AND user_id='$owner_id';")" = t
creation="$(admin "SELECT created_at FROM user_board_preferences WHERE board_id='$board' AND user_id='$owner_id';")"
test "$(request owner PUT "/boards/$board/star?version=1" "$(uuid)" '')" = 204
test "$(admin "SELECT version FROM user_board_preferences WHERE board_id='$board' AND user_id='$owner_id';")" = 1
preference owner "$owner_id" true
preference member "$member_id" false
# The same key belongs to a different actor namespace.
test "$(request member PUT "/boards/$board/star?version=0" "$key" '')" = 204
test "$(request owner DELETE "/boards/$board/star?version=0" "$(uuid)" '')" = 409
jq -e '.code == "version_conflict"' "$scratch/response" >/dev/null
test "$(request owner DELETE "/boards/$board/star?version=1" "$(uuid)" '')" = 204
test "$(request owner PUT "/boards/$board/star?version=0" "$key" '')" = 204
preference owner "$owner_id" false
preference member "$member_id" true
test "$(admin "SELECT version=2 AND NOT starred AND created_at<=updated_at FROM user_board_preferences WHERE board_id='$board' AND user_id='$owner_id';")" = t
test "$(admin "SELECT created_at FROM user_board_preferences WHERE board_id='$board' AND user_id='$owner_id';")" = "$creation"
test "$(admin "SELECT count(*) FROM board_star_events WHERE board_id='$board' AND actor_id='$owner_id';")" = 2
test "$(admin "SELECT count(*) FROM board_star_events WHERE board_id='$board' AND actor_id='$member_id';")" = 1
test "$(admin "SELECT count(*) FROM board_star_events e JOIN user_board_preferences p ON p.tenant_id=e.tenant_id AND p.id=e.entity_id WHERE e.board_id='$board' AND (e.actor_id<>p.user_id OR e.board_id<>p.board_id OR e.event_type<>'BOARD_STARRED' OR e.entity_type<>'UserBoardPreference' OR e.metadata<>'{}'::jsonb);")" = 0
test "$(request owner DELETE "/boards/$board/star?version=0" "$key" '')" = 409
jq -e '.code == "idempotency_key_reused"' "$scratch/response" >/dev/null
# A failed restricted write must roll back its tentative receipt and retain all
# preference clocks/revision. The same intent may then commit after recovery.
retry_key="$(uuid)"
preference_before="$(admin "SELECT to_jsonb(p)::text FROM user_board_preferences p WHERE board_id='$board' AND user_id='$owner_id';")"
events_before="$(admin "SELECT md5(string_agg(to_jsonb(e)::text,chr(10) ORDER BY version)) FROM board_star_events e WHERE board_id='$board' AND actor_id='$owner_id';")"
preference_permission_withdrawn=1
admin 'REVOKE UPDATE ON user_board_preferences FROM strataai_api_runtime;' >/dev/null
test "$(request owner PUT "/boards/$board/star?version=2" "$retry_key" '')" = 503
jq -e '.code == "work_storage_unavailable"' "$scratch/response" >/dev/null
scripts/ci/assert-file-excludes.sh 'Npgsql|permission denied|user_board_preferences|work_command_replays|INSERT INTO' "$scratch/response"
test "$(admin "SELECT to_jsonb(p)::text FROM user_board_preferences p WHERE board_id='$board' AND user_id='$owner_id';")" = "$preference_before"
test "$(admin "SELECT md5(string_agg(to_jsonb(e)::text,chr(10) ORDER BY version)) FROM board_star_events e WHERE board_id='$board' AND actor_id='$owner_id';")" = "$events_before"
test "$(admin "SELECT count(*) FROM work_command_replays WHERE tenant_id='$organization' AND actor_id='$owner_id' AND key_id='$retry_key';")" = 0
restore
preference_permission_withdrawn=0
test "$(request owner PUT "/boards/$board/star?version=2" "$retry_key" '')" = 204
test "$(admin "SELECT version=3 AND starred FROM user_board_preferences WHERE board_id='$board' AND user_id='$owner_id';")" = t
committed="$(admin "SELECT to_jsonb(p)::text FROM user_board_preferences p WHERE board_id='$board' AND user_id='$owner_id';")"
test "$(request owner PUT "/boards/$board/star?version=2" "$retry_key" '')" = 204
test "$(admin "SELECT to_jsonb(p)::text FROM user_board_preferences p WHERE board_id='$board' AND user_id='$owner_id';")" = "$committed"
test "$(admin "SELECT count(*) FROM board_star_events WHERE board_id='$board' AND actor_id='$owner_id';")" = 3
test "$(request owner GET "/boards/$board/star/events" "$(uuid)" '')" = 200
jq -e --arg actor "$owner_id" --arg board "$board" --arg org "$organization" '
 .userId==$actor and .boardId==$board and .organizationId==$org and .nextAfter==null and
 ([.items[].version]==[1,2,3]) and all(.items[]; .actorId==$actor and .boardId==$board and
 .organizationId==$org and .eventType=="BOARD_STARRED" and .entityType=="UserBoardPreference" and .metadata=={} and (has("starred")|not))
' "$scratch/response" >/dev/null
test "$(request member GET "/boards/$board/star/events" "$(uuid)" '')" = 200
jq -e --arg actor "$member_id" '.userId==$actor and (.items|length)==1 and .items[0].actorId==$actor' "$scratch/response" >/dev/null
test "$(request owner GET "/boards/$board/star/events?after=2" "$(uuid)" '')" = 200
jq -e '[.items[].version]==[3]' "$scratch/response" >/dev/null
test "$(admin "SELECT has_table_privilege('strataai_api_runtime','board_star_events','INSERT') OR has_table_privilege('strataai_api_runtime','board_star_events','UPDATE') OR has_table_privilege('strataai_api_runtime','board_star_events','DELETE');")" = f
if admin "UPDATE board_star_events SET version=version WHERE board_id='$board';" >/dev/null 2>&1; then echo 'Private star history was mutable'; exit 1; fi
admin "BEGIN; UPDATE user_board_preferences SET starred=false,version=version+1,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE board_id='$board' AND user_id='$owner_id'; ROLLBACK;" >/dev/null
test "$(admin "SELECT to_jsonb(p)::text FROM user_board_preferences p WHERE board_id='$board' AND user_id='$owner_id';")" = "$committed"
test "$(admin "SELECT count(*) FROM board_star_events WHERE board_id='$board' AND actor_id='$owner_id';")" = 3
test "$(request owner GET "/boards/$board" "$(uuid)" '')" = 200
jq -S '{board,lists}' "$scratch/response" > "$scratch/after.json"
cmp "$scratch/before.json" "$scratch/after.json"
# Retain the Board grant and preference to prove current Organization eligibility.
admin "UPDATE organization_members SET status='SUSPENDED' WHERE tenant_id='$organization' AND user_id='$member_id';" >/dev/null
for method in GET PUT; do
  test "$(request member "$method" "/boards/$board/star?version=0" "$key" '')" = 404
  jq -e '.code == "board_not_found"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh 'starred|Private personal preferences|Npgsql|user_board_preferences|work_command_replays' "$scratch/response"
done
test "$(request member GET "/boards/$board/star/events?after=invalid" "$(uuid)" '')" = 404
scripts/ci/assert-file-excludes.sh 'BOARD_STARRED|actorId|entityId' "$scratch/response"
test "$(curl --max-time 30 --silent --show-error -o "$scratch/response" -w '%{http_code}' "$BASE_URL/boards/$board/star")" = 401
echo 'Exact release API proves private star reads, retained revisions, atomic rollback/recovery, receipt isolation, replay without overwrite and revoked admission.'
