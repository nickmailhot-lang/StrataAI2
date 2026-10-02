#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable watch fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d); gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'GRANT INSERT ON audit_events,work_events,background_jobs,card_assignment_notifications TO strataai_api_runtime; GRANT UPDATE(watching,updated_at,version) ON watch_subscriptions TO strataai_api_runtime;' >/dev/null || true
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Watch subscription check failed at line $LINENO" >&2' ERR
for actor in owner member outsider; do
  jq -nc --arg email "watch-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"watch-correct-horse-battery",displayName:"Watch fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); member=$(jq -r '.user.id' "$scratch/member.user")
org=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Watch transactions"}' "$base/organizations" | jq -r '.organization.id')
board=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Private watched Board",visibility:"PRIVATE"}')" "$base/boards" | jq -r '.id')
for id in "$owner" "$member" "$org" "$board"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES(gen_random_uuid(),'$org','$board','$member','MEMBER','ACTIVE',now(),now());" >/dev/null
list=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Watched List"}' "$base/boards/$board/lists" | jq -r '.id')
destination=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Moved List"}' "$base/boards/$board/lists" | jq -r '.id')
card=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"title":"Watched Card"}' "$base/lists/$list/cards" | jq -r '.id')
for id in "$list" "$destination" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
get() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/response.json" -w '%{http_code}' "$base$2"; }
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -d "$5" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
uuid() { cat /proc/sys/kernel/random/uuid; }
state() { admin "SELECT md5(jsonb_build_object(
 'watches',(SELECT jsonb_agg(to_jsonb(w) ORDER BY id) FROM watch_subscriptions w WHERE tenant_id='$org'),
 'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'notifications',(SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
for type in CARD LIST BOARD; do
  case "$type" in CARD) entity=$card;; LIST) entity=$list;; BOARD) entity=$board;; esac
  path="/watch/$type/$entity"; key=$(uuid); before=$(state)
  test "$(get member "$path")" = 200
  jq -e --arg member "$member" '.userId==$member and .watching==false and .version==0 and .subscriptionId==null' "$scratch/response.json" >/dev/null
  test "$(request outsider PUT "$path?version=invalid" "$key" '{}')" = 404
  test "$(request member PUT "$path?version=invalid" "$key" '{}')" = 400
  test "$(request member PUT "$path?version=0" bad-key '{}')" = 400
  test "$before" = "$(state)"
  # The subscription write precedes audit/event/job publication. Each missing
  # grant must roll back everything, including its retry claim and revision.
  for table in audit_events work_events background_jobs; do
    admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
    test "$(request member PUT "$path?version=0" "$key" '{}')" = 503
    admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
    test "$before" = "$(state)"
  done
  test "$(request member PUT "$path?version=0" "$key" '{}')" = 200
  jq -e --arg member "$member" --arg entity "$entity" --arg type "$type" '.userId==$member and .entityId==$entity and .entityType==$type and .watching and .changed and .version==1 and .subscriptionId!=null and .createdAt==.updatedAt' "$scratch/response.json" >/dev/null
  cp "$scratch/response.json" "$scratch/$type.receipt"; after=$(state)
  test "$(request member PUT "$path?version=0" "$key" '{}')" = 200
  cmp "$scratch/response.json" "$scratch/$type.receipt"; test "$after" = "$(state)"
  test "$(request member DELETE "$path?version=1" "$key" '{}')" = 409; test "$after" = "$(state)"
  test "$(request member DELETE "$path?version=0" "$(uuid)" '{}')" = 409; test "$after" = "$(state)"
  test "$(get owner "$path?userId=$member")" = 200
  jq -e --arg owner "$owner" '.userId==$owner and .watching==false and .version==0' "$scratch/response.json" >/dev/null
  remove_key=$(uuid)
  admin 'REVOKE UPDATE(watching,updated_at,version) ON watch_subscriptions FROM strataai_api_runtime;' >/dev/null
  test "$(request member DELETE "$path?version=1" "$remove_key" '{}')" = 503
  admin 'GRANT UPDATE(watching,updated_at,version) ON watch_subscriptions TO strataai_api_runtime;' >/dev/null
  test "$after" = "$(state)"
  test "$(request member DELETE "$path?version=1" "$remove_key" '{}')" = 200
  jq -e '.watching==false and .version==2 and .changed' "$scratch/response.json" >/dev/null
  test "$(request member PUT "$path?version=2" "$(uuid)" '{}')" = 200
done
test "$(admin "SELECT count(*) FROM watch_subscriptions WHERE tenant_id='$org' AND user_id='$member' AND watching AND version=3;")" = 3
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type IN ('WATCH_CREATED','WATCH_REMOVED');")" = 9
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND event_type IN ('WATCH_CREATED','WATCH_REMOVED');")" = 9
# The real Worker must make these events ready without exposing personal watch
# payloads or breaking the shared Board cursor for either authorized viewer.
published=$(admin "SELECT last_sequence FROM work_event_streams WHERE tenant_id='$org' AND board_id='$board';")
watch_ids=$(admin "SELECT json_agg(event_id) FROM work_events WHERE tenant_id='$org' AND board_id='$board' AND entity_type='WatchSubscription';")
for ((attempt=0;attempt<60;attempt++)); do
  test "$(get owner "/boards/$board/sync")" = 200
  if jq -e --arg cursor "$published" '.cursor==$cursor and .pending==false' "$scratch/response.json" >/dev/null; then break; fi
  sleep 0.5
done
jq -e --arg cursor "$published" '.cursor==$cursor and .pending==false' "$scratch/response.json" >/dev/null
for viewer in owner member; do
  test "$(get "$viewer" "/boards/$board/sync")" = 200
  jq -e --arg board "$board" --argjson ids "$watch_ids" '
    [.events[] | select(.eventId as $id | $ids | index($id))] as $private |
    ($private | length)==9 and all($private[];
      .eventType=="BOARD_INVALIDATED" and .entityType=="Board" and .entityId==$board and .actorId==null and .metadata=={})
    ' "$scratch/response.json" >/dev/null
done
card_path="/watch/CARD/$card"; before_move=$(admin "SELECT id FROM watch_subscriptions WHERE tenant_id='$org' AND card_id='$card';")
# Populate more than the inbox page size, then use the real API transaction to
# prove all current eligible watchers receive one intent and actor/overlap dedupe.
test "$(request owner PUT "/watch/BOARD/$board?version=0" "$(uuid)" '{}')" = 200
test "$(admin "WITH candidates AS (SELECT gen_random_uuid() id FROM generate_series(1,75)),
 accounts AS (INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 SELECT id,'watch-many-'||id||'@example.test',upper('watch-many-'||id||'@example.test'),'Bulk watch fixture','ACTIVE',true,'unused-fixture-hash',now(),now() FROM candidates RETURNING id),
 memberships AS (INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),'$org',id,'MEMBER','ACTIVE' FROM accounts RETURNING user_id),
 grants AS (INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board',user_id,'MEMBER','ACTIVE',now(),now() FROM memberships RETURNING user_id),
 watching AS (INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,board_id,watching,created_at,updated_at,version)
 SELECT '$org',gen_random_uuid(),user_id,'BOARD','$board','$board',true,now(),now(),1 FROM grants RETURNING user_id)
 SELECT count(*) FROM watching;")" = 75
move_key=$(uuid); move_body=$(jq -nc --arg destination "$destination" '{destinationListId:$destination,expectedVersion:1}')
before_failure=$(state)
admin 'REVOKE INSERT ON card_assignment_notifications FROM strataai_api_runtime;' >/dev/null
test "$(request owner POST "/cards/$card/move" "$move_key" "$move_body")" = 503
admin 'GRANT INSERT ON card_assignment_notifications TO strataai_api_runtime;' >/dev/null
test "$before_failure" = "$(state)"
test "$(request owner POST "/cards/$card/move" "$move_key" "$move_body")" = 200
after_activity=$(state)
test "$(request owner POST "/cards/$card/move" "$move_key" "$move_body")" = 200
test "$after_activity" = "$(state)"
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND card_id='$card' AND notification_type='CARD_MOVED';")" = 76
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$owner';")" = 0
test "$(get member "/organizations/$org/notifications")" = 200
jq -e --arg member "$member" --arg card "$card" '.items|length==1 and .[0].type=="CARD_MOVED" and .[0].recipientId==$member and .[0].entityId==$card' "$scratch/response.json" >/dev/null
test "$(get member "$card_path")" = 200
jq -e --arg id "$before_move" '.subscriptionId==$id and .watching and .version==3' "$scratch/response.json" >/dev/null
admin "UPDATE organizations SET status='ARCHIVED' WHERE id='$org';" >/dev/null
test "$(get member "$card_path")" = 200
jq -e '.canChange==false and .watching and .version==3' "$scratch/response.json" >/dev/null
test "$(request member DELETE "$card_path?version=3" "$(uuid)" '{}')" = 404
admin "UPDATE organizations SET status='ACTIVE' WHERE id='$org';" >/dev/null
hold() {
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 & gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\nSELECT id FROM boards WHERE id=\047%s\047 FOR UPDATE;\n\\echo watch_locked\n' "$board" >&3
  for ((attempt=0;attempt<100;attempt++)); do if grep -q '^watch_locked$' "$scratch/gate.log"; then return; fi; kill -0 "$gate_pid" || return 1; sleep 0.05; done
  return 1
}
blocked() {
  for ((attempt=0;attempt<100;attempt++)); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%FOR UPDATE%';")" -ge 1; then return; fi
    sleep 0.05
  done
  echo 'Expected watch Board lock wait was not observed.' >&2; return 1
}
release() { printf '%s\nCOMMIT;\n\\q\n' "$1" >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; rm "$scratch/gate.in" "$scratch/gate.log"; }
# The read and a mutation/receipt both freshly authorize after the Board wait.
# Fan-out also selects recipients after that wait: a just-revoked private Board
# member cannot receive a fresh notification from an otherwise permitted actor.
hold; request owner PUT "/cards/$card/members/$owner?version=2" "$(uuid)" '{}' > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 200
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND notification_type='CARD_MEMBER_ADDED';")" = 75
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND notification_type='CARD_MEMBER_ADDED' AND recipient_id IN ('$member','$owner');")" = 0
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
hold; get member "$card_path" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
test "$(admin "SELECT count(*) FROM watch_subscriptions WHERE tenant_id='$org' AND user_id='$member' AND watching;")" = 3
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
replay_key=$(uuid); test "$(request member PUT "$card_path?version=3" "$replay_key" '{}')" = 200
hold; request member PUT "$card_path?version=3" "$replay_key" '{}' > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
hold; get member "$card_path" > "$scratch/status" & request_pid=$!
blocked; release "DELETE FROM sessions WHERE user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
echo 'Watch subscriptions: typed scopes, private replay, personal revisions, atomic fan-out/command rollback, 76 recipients, dedupe, List movement, archived Organization and observed access/session waits passed.'
