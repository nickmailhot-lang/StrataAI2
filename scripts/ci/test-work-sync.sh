#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable synchronization fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
organization=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
restore() {
  admin 'GRANT SELECT ON work_events TO strataai_api_runtime; DROP POLICY IF EXISTS ci_sync_read_delay ON work_events; DROP FUNCTION IF EXISTS public.ci_sync_read_pause(uuid);' >/dev/null
  if test -n "$organization"; then
    admin "UPDATE work_event_streams s SET last_sequence=(SELECT coalesce(max(e.sequence),0) FROM work_events e WHERE e.tenant_id=s.tenant_id AND e.board_id=s.board_id) WHERE s.tenant_id='$organization';" >/dev/null
  fi
  rm -rf "$scratch"
}
trap restore EXIT
trap 'echo "Work synchronization check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
account() {
  local name="$1" email="sync-${RANDOM}-${RANDOM}@example.test"
  local body
  body="$(jq -nc --arg email "$email" '{email:$email,password:"sync-correct-horse-battery",displayName:"Sync fixture"}')"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/$name.json"
  curl --fail --silent --show-error -c "$scratch/$name.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
}
request() { curl --max-time 30 --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -X "$1" -d "$3" "$BASE_URL$2"; }
sync() { curl --max-time 30 --fail --silent --show-error -b "$scratch/${2:-owner}.cookies" "$BASE_URL$1"; }
rejected() {
  local status
  status="$(curl --max-time 30 --silent --show-error -b "$scratch/${4:-owner}.cookies" -o "$scratch/failure.json" -w '%{http_code}' "$BASE_URL$1")"
  test "$status" = "$2"
  jq -e --arg code "$3" '.code==$code' "$scratch/failure.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Protected|Private|Npgsql|SELECT|permission denied|work_events' "$scratch/failure.json"
}
account owner; account member; account outsider
: > "$scratch/visitor.cookies"
member="$(jq -r '.user.id' "$scratch/member.json")"
organization="$(request POST /organizations '{"name":"Sync Organization"}' | jq -r '.organization.id')"
[[ "$organization" =~ ^[0-9a-fA-F-]{36}$ ]]
board="$(request POST /boards "$(jq -nc --arg org "$organization" '{organizationId:$org,name:"Protected private board"}')" | jq -r '.id')"
list="$(request POST "/boards/$board/lists" '{"name":"Protected list"}' | jq -r '.id')"
card="$(request POST "/lists/$list/cards" '{"title":"Protected title","description":"Private description"}' | jq -r '.id')"
route="/boards/$board/sync"
sync "$route" | jq -e '.cursor=="0" and .pending and (.events|length)==0 and (.hasMore|not)' >/dev/null
# A later ready event cannot conceal a delayed predecessor.
admin "UPDATE work_events SET ready_at=clock_timestamp() WHERE tenant_id='$organization' AND board_id='$board' AND sequence=3;" >/dev/null
sync "$route" | jq -e '.cursor=="0" and .pending and (.events|length)==0' >/dev/null
admin "UPDATE work_events SET ready_at=clock_timestamp() WHERE tenant_id='$organization' AND board_id='$board' AND sequence=1;" >/dev/null
sync "$route" | jq -e '.cursor=="1" and .pending and (.events|length)==1 and .events[0].sequence=="1"' >/dev/null
admin "UPDATE work_events SET ready_at=clock_timestamp() WHERE tenant_id='$organization' AND board_id='$board' AND sequence=2;" >/dev/null
sync "$route?limit=2" > "$scratch/first.json"
sync "$route?limit=2" > "$scratch/replay.json"
cmp "$scratch/first.json" "$scratch/replay.json"
jq -e '.cursor=="2" and .hasMore and (.pending|not) and (.events|length)==2' "$scratch/first.json" >/dev/null
sync "$route?since=2" > "$scratch/last.json"
jq -e --arg card "$card" '.cursor=="3" and (.hasMore|not) and (.events|length)==1 and .events[0].entityId==$card and .events[0].version==1 and .events[0].eventType=="CARD_CREATED" and .events[0].metadata=={}' "$scratch/last.json" >/dev/null
scripts/ci/assert-file-excludes.sh 'Protected title|Private description|safe-correlation' "$scratch/last.json"
curl --fail --silent --show-error -b "$scratch/owner.cookies" -D "$scratch/headers" -o /dev/null "$BASE_URL$route"
grep -iq '^cache-control: no-store' "$scratch/headers"
rejected "$route" 404 board_not_found outsider
rejected "$route" 404 board_not_found visitor
rejected "$route?since=-1" 400 invalid_sync_cursor
rejected "$route?since=9223372036854775808" 400 invalid_sync_cursor
rejected "$route?since=1&since=2" 400 invalid_sync_cursor
rejected "$route?limit=101" 400 invalid_sync_limit
sync "$route?since=9223372036854775807" | jq -e '.resetRequired and .cursor=="0" and (.events|length)==0' >/dev/null
# Simulate a damaged/purged history boundary, without allowing cursor advancement.
admin "UPDATE work_event_streams SET last_sequence=5 WHERE tenant_id='$organization' AND board_id='$board';" >/dev/null
sync "$route" | jq -e '.resetRequired and .cursor=="0" and (.events|length)==0' >/dev/null
admin "UPDATE work_event_streams SET last_sequence=3 WHERE tenant_id='$organization' AND board_id='$board'; REVOKE SELECT ON work_events FROM strataai_api_runtime;" >/dev/null
rejected "$route" 503 work_sync_unavailable
admin 'GRANT SELECT ON work_events TO strataai_api_runtime;' >/dev/null
# A public board exposes only currently visible headers, never actor IDs/content.
public_board="$(request POST /boards "$(jq -nc --arg org "$organization" '{organizationId:$org,name:"Public sync board",visibility:"PUBLIC"}')" | jq -r '.id')"
public_list="$(request POST "/boards/$public_board/lists" '{"name":"Public list"}' | jq -r '.id')"
public_card="$(request POST "/lists/$public_list/cards" '{"title":"Protected history title"}' | jq -r '.id')"
public_route="/boards/$public_board/sync"
admin "UPDATE work_events SET ready_at=clock_timestamp() WHERE tenant_id='$organization' AND board_id='$public_board';" >/dev/null
sync "$public_route" visitor | jq -e '(.events|length)==3 and all(.events[];.actorId==null and .metadata=={})' >/dev/null
# A damaged event referring to another private Board's entity is coarsened too;
# Organization scope by itself never authorizes that entity reference.
# Deliberately damaged historical data is a privileged disposable fixture.
# Runtime activity sources cannot be rewritten; bypass only its source guard
# inside this one administrative transaction, restoring it before commit.
admin "BEGIN; ALTER TABLE work_events DISABLE TRIGGER work_event_activity_immutable;
 UPDATE work_events SET entity_id='$card' WHERE tenant_id='$organization' AND board_id='$public_board' AND sequence=3;
 ALTER TABLE work_events ENABLE TRIGGER work_event_activity_immutable; COMMIT;" >/dev/null
sync "$public_route?since=2" visitor > "$scratch/wrong-board.json"
jq -e --arg board "$public_board" '.events[0].entityId==$board and .events[0].entityType=="Board" and .events[0].eventType=="BOARD_INVALIDATED"' "$scratch/wrong-board.json" >/dev/null
scripts/ci/assert-file-excludes.sh "$card" "$scratch/wrong-board.json"
admin "BEGIN; ALTER TABLE work_events DISABLE TRIGGER work_event_activity_immutable;
 UPDATE work_events SET entity_id='$public_card' WHERE tenant_id='$organization' AND board_id='$public_board' AND sequence=3;
 ALTER TABLE work_events ENABLE TRIGGER work_event_activity_immutable; COMMIT;" >/dev/null
request POST "/cards/$public_card/archive" '{"version":1}' >/dev/null
admin "UPDATE work_events SET ready_at=clock_timestamp() WHERE tenant_id='$organization' AND board_id='$public_board';" >/dev/null
sync "$public_route?since=2" visitor > "$scratch/hidden.json"
jq -e --arg board "$public_board" '(.events|length)==2 and all(.events[];.entityType=="Board" and .entityId==$board and .eventType=="BOARD_INVALIDATED" and .actorId==null)' "$scratch/hidden.json" >/dev/null
scripts/ci/assert-file-excludes.sh "$public_card" "$scratch/hidden.json"
request POST "/boards/$public_board/archive" '{"version":1}' >/dev/null
rejected "$public_route" 404 board_not_found visitor
# Boards predating event migration have a valid empty stream.
legacy="$(uuid)"
admin "INSERT INTO boards(id,tenant_id,name,visibility,created_at,updated_at) VALUES ('$legacy','$organization','Legacy public board','PUBLIC',now(),now());" >/dev/null
sync "/boards/$legacy/sync" visitor | jq -e '.cursor=="0" and (.events|length)==0 and (.resetRequired|not) and (.pending|not)' >/dev/null
# Revoke an actual member while its protected event SELECT is waiting in PostgreSQL.
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at) VALUES (gen_random_uuid(),'$organization','$member','MEMBER','ACTIVE',now(),now());" >/dev/null
request PATCH "/boards/$board/members/$member" '{"role":"MEMBER"}' >/dev/null
admin "UPDATE work_events SET ready_at=clock_timestamp() WHERE tenant_id='$organization' AND board_id='$board'; CREATE FUNCTION public.ci_sync_read_pause(p_tenant uuid) RETURNS boolean LANGUAGE plpgsql AS \$\$ BEGIN IF p_tenant='$organization'::uuid THEN PERFORM pg_sleep(0.7); END IF; RETURN true; END; \$\$; CREATE POLICY ci_sync_read_delay ON work_events AS RESTRICTIVE FOR SELECT TO strataai_api_runtime USING (public.ci_sync_read_pause(tenant_id));" >/dev/null
curl --max-time 30 --silent --show-error -b "$scratch/member.cookies" -o "$scratch/late.json" -w '%{http_code}' "$BASE_URL$route" > "$scratch/late.status" & reading=$!
for attempt in $(seq 1 30); do
  observed="$(admin "SELECT count(*) FROM pg_stat_activity WHERE state='active' AND wait_event='PgSleep' AND query LIKE '%FROM work_events e WHERE%';")"
  if test "$observed" != 0; then break; fi
  sleep 0.1
done
test "$observed" != 0
admin "UPDATE organization_members SET status='SUSPENDED' WHERE tenant_id='$organization' AND user_id='$member';" >/dev/null
wait "$reading"
test "$(cat "$scratch/late.status")" = 404
jq -e '.code=="board_not_found" and (has("events")|not)' "$scratch/late.json" >/dev/null
scripts/ci/assert-file-excludes.sh "$card|$list|Protected|Private" "$scratch/late.json"
admin 'DROP POLICY ci_sync_read_delay ON work_events; DROP FUNCTION public.ci_sync_read_pause(uuid);' >/dev/null
echo 'Exact PostgreSQL API proves bounded contiguous replay, precise cursor recovery, public-safe history, sanitized failures and access revocation during an awaited event read.'
