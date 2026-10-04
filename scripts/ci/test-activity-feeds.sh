#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable activity fixtures require CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d); gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Activity release fixture failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
get() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -D "$scratch/headers" -o "$scratch/response.json" -w '%{http_code}' "$base$2"; }
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$4" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
login() { curl --fail --silent --show-error -c "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$1.credentials")" "$base/auth/login" >/dev/null; }
for actor in owner member outsider; do
  jq -nc --arg email "activity-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"activity-correct-horse-battery",displayName:"Activity original actor"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  login "$actor"
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); member=$(jq -r '.user.id' "$scratch/member.user")
test "$(request owner POST /organizations '{"name":"Activity release"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response.json")
for suffix in source current; do
  test "$(request owner POST /boards "$(jq -nc --arg org "$org" --arg name "Activity $suffix" '{organizationId:$org,name:$name,visibility:"PRIVATE"}')")" = 201
  jq -r '.id' "$scratch/response.json" > "$scratch/$suffix.board"
done
source=$(cat "$scratch/source.board"); current=$(cat "$scratch/current.board")
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),'$org','$source','$member','MEMBER','ACTIVE',now(),now()),(gen_random_uuid(),'$org','$current','$member','MEMBER','ACTIVE',now(),now());" >/dev/null
test "$(request owner POST "/boards/$current/lists" '{"name":"Current activity parent"}')" = 201
list=$(jq -r '.id' "$scratch/response.json")
test "$(request owner POST "/lists/$list/cards" '{"title":"Protected activity Card"}')" = 201
card=$(jq -r '.id' "$scratch/response.json")
for id in "$owner" "$member" "$org" "$source" "$current" "$list" "$card"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
# Synthetic historical source birth; actual current parents, sessions, role
# checks and API gates below are real. This does not claim a Card move command.
admin "WITH stream AS (UPDATE work_event_streams SET last_sequence=last_sequence+65,updated_at=clock_timestamp()
 WHERE tenant_id='$org' AND board_id='$source' RETURNING last_sequence)
 INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 SELECT '$org',gen_random_uuid(),'$source',s.last_sequence-65+i,'$owner','CARD_UPDATED','Card','$card',i,'activity-release-history',
 date_trunc('second',clock_timestamp())+interval '1 minute' FROM stream s CROSS JOIN generate_series(1,65) i;" >/dev/null
path="/cards/$card/activity"
test "$(get member "$path")" = 200
jq -e --arg source "$source" --arg current "$current" --arg card "$card" '.kind=="CARD" and .targetId==$card and (.items|length)==50 and .nextCursor!=null and
 all(.items[];.boardId==$source and .currentBoardId==$current and .actorLabel=="Activity original actor" and (.version|type)=="string" and .metadata=={})' "$scratch/response.json" >/dev/null
grep -qi '^Cache-Control: no-store' "$scratch/headers"
cp "$scratch/response.json" "$scratch/first.json"
cursor=$(jq -r '.nextCursor' "$scratch/response.json"); [[ "$cursor" =~ ^[A-Za-z0-9_-]+$ ]]
test "$(get member "$path?after=$cursor")" = 200
jq -e '(.items|length)==16 and .nextCursor==null' "$scratch/response.json" >/dev/null
jq -s -e '[.[].items[].eventId]|length==66 and (unique|length)==66' "$scratch/first.json" "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/tail.json"
test "$(get owner "$path?after=$cursor")" = 400
test "$(get member "/boards/$source/activity?after=$cursor")" = 400
test "$(get outsider "$path?after=invalid")" = 404
jq -e '.code=="activity_not_found" and (has("items")|not)' "$scratch/response.json" >/dev/null
# Same immutable tested API image, new process/container, preserved key volume.
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --force-recreate --wait --wait-timeout 180 api >/dev/null
edge_ready=false
for ((attempt=0;attempt<50;attempt++)); do
  if curl --fail --silent --show-error "$base/api/health" >/dev/null 2>&1; then edge_ready=true; break; fi
  sleep 0.2
done
test "$edge_ready" = true
test "$(get member "$path?after=$cursor")" = 200
cmp "$scratch/response.json" "$scratch/tail.json"
admin "UPDATE users SET display_name='Later activity actor' WHERE id='$owner';" >/dev/null
test "$(get member "$path")" = 200
jq -e 'all(.items[];.actorLabel=="Activity original actor")' "$scratch/response.json" >/dev/null
hold() {
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 & gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\n%s\n\\echo activity_locked\n' "$1" >&3
  for ((attempt=0;attempt<100;attempt++)); do
    if grep -q '^activity_locked$' "$scratch/gate.log"; then return; fi
    kill -0 "$gate_pid" || return 1; sleep 0.05
  done
  return 1
}
blocked() {
  for ((attempt=0;attempt<100;attempt++)); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '$1';")" -ge 1; then return; fi
    kill -0 "$request_pid" || return 1; sleep 0.05
  done
  echo 'Expected activity authorization lock wait was not observed.' >&2; return 1
}
release() { printf '%s\nCOMMIT;\n\\q\n' "$1" >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; rm "$scratch/gate.in" "$scratch/gate.log"; }
for denied_board in "$source" "$current"; do
  hold "SELECT id FROM boards WHERE tenant_id='$org' AND id='$denied_board' FOR UPDATE;"
  get member "$path" > "$scratch/status" & request_pid=$!
  blocked '%SELECT id FROM boards%FOR UPDATE%'
  release "UPDATE board_members SET status='REMOVED' WHERE tenant_id='$org' AND board_id='$denied_board' AND user_id='$member';"
  wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
  jq -e '.code=="activity_not_found" and (has("items")|not)' "$scratch/response.json" >/dev/null
  admin "UPDATE board_members SET status='ACTIVE' WHERE tenant_id='$org' AND board_id='$denied_board' AND user_id='$member';" >/dev/null
  test "$(get member "$path")" = 200
done
# Account wait occurs before taking the issuing session SHARE lock. Deleting
# that session while holding a later Board gate would invert the lock order.
hold "SELECT id FROM users WHERE id='$member' FOR UPDATE;"
get member "$path" > "$scratch/status" & request_pid=$!
blocked '%FROM users WHERE id%FOR SHARE%'
release "DELETE FROM sessions WHERE user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
jq -e '(has("items")|not)' "$scratch/response.json" >/dev/null
login member
hash=$(awk '$6=="strataai_session" {print $7}' "$scratch/member.cookies" | tr -d '\n' | sha256sum | cut -d ' ' -f 1)
[[ "$hash" =~ ^[0-9a-f]{64}$ ]]
hold "SELECT id FROM boards WHERE tenant_id='$org' AND id='$source' FOR UPDATE;"
admin "UPDATE sessions SET expires_at=clock_timestamp()+interval '15 seconds' WHERE token_hash='$hash';" >/dev/null
get member "$path" > "$scratch/status" & request_pid=$!
blocked '%SELECT id FROM boards%FOR UPDATE%'
test "$(admin "SELECT expires_at>clock_timestamp() FROM sessions WHERE token_hash='$hash';")" = t
expired=false
for ((attempt=0;attempt<200;attempt++)); do
  if test "$(admin "SELECT expires_at<=clock_timestamp() FROM sessions WHERE token_hash='$hash';")" = t; then expired=true; break; fi
  kill -0 "$request_pid" || exit 1; sleep 0.1
done
test "$expired" = true
release ''
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
jq -e '.code=="session_unavailable" and (has("items")|not)' "$scratch/response.json" >/dev/null
login member
test "$(get member "$path")" = 200
echo 'Exact activity API: immutable history, complete tied seek, protected cursor scopes/restart, source/current Board post-wait revocation, issuing-session revocation and natural expiry passed. Synthetic historical birth does not prove a Card move command.'
