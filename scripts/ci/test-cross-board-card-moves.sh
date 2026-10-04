#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable movement fixtures require CI.' >&2; exit 1; }
umask 077
base=http://localhost:8088
scratch=$(mktemp -d); revoked=false
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() { if $revoked; then admin 'GRANT INSERT ON work_events TO strataai_api_runtime;' >/dev/null || true; fi; rm -rf "$scratch"; }
trap cleanup EXIT
trap 'echo "Cross-Board movement fixture failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
request() {
  local actor=$1 method=$2 path=$3 body=$4 key=${5:-$(uuid)}
  curl --silent --show-error -b "$scratch/$actor.cookies" -X "$method" -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "$body" -o "$scratch/response.json" -w '%{http_code}' "$base$path"
}
get() { curl --fail --silent --show-error -b "$scratch/$1.cookies" "$base$2"; }
for actor in owner member other; do
  jq -nc --arg email "move-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"move-correct-horse-battery",displayName:"Movement fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); member=$(jq -r '.user.id' "$scratch/member.user"); other=$(jq -r '.user.id' "$scratch/other.user")
test "$(request owner POST /organizations '{"name":"Cross-Board movement release"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response.json")
for side in source destination; do
  test "$(request owner POST /boards "$(jq -nc --arg org "$org" --arg name "$side" '{organizationId:$org,name:$name,visibility:"PRIVATE"}')")" = 201
  jq -r '.id' "$scratch/response.json" > "$scratch/$side.board"
  test "$(request owner POST "/boards/$(cat "$scratch/$side.board")/lists" '{"name":"Movement parent"}')" = 201
  jq -r '.id' "$scratch/response.json" > "$scratch/$side.list"
done
source=$(cat "$scratch/source.board"); destination=$(cat "$scratch/destination.board")
source_list=$(cat "$scratch/source.list"); destination_list=$(cat "$scratch/destination.list")
for id in "$owner" "$member" "$other" "$org" "$source" "$destination" "$source_list" "$destination_list"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
# Trusted disposable membership setup; all mutations below use the actual
# restricted release API, issuing sessions and real Board admission.
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
 (gen_random_uuid(),'$org','$member','MEMBER','ACTIVE'),(gen_random_uuid(),'$org','$other','MEMBER','ACTIVE');" >/dev/null
for actor in "$member" "$other"; do
  test "$(request owner PATCH "/boards/$source/members/$actor" '{"role":"MEMBER"}')" = 200
done
test "$(request owner POST "/lists/$source_list/cards" '{"title":"Stable movement subject"}')" = 201
card=$(jq -r '.id' "$scratch/response.json"); [[ "$card" =~ ^[0-9a-f-]{36}$ ]]
test "$(request owner POST "/boards/$source/labels" '{"name":"Preserved label","color":"blue"}')" = 201
label=$(jq -r '.id' "$scratch/response.json"); [[ "$label" =~ ^[0-9a-f-]{36}$ ]]
test "$(request owner PUT "/cards/$card/labels/$label?version=1" '{}')" = 200
test "$(request owner PUT "/cards/$card/members/$member?version=2" '{}')" = 200
test "$(request owner PUT "/cards/$card/members/$other?version=3" '{}')" = 200
state() { admin "SELECT md5(jsonb_build_object(
 'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
 'routes',(SELECT jsonb_agg(to_jsonb(r) ORDER BY card_id) FROM card_routes r WHERE tenant_id='$org'),
 'comments',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM card_comments c WHERE tenant_id='$org'),
 'checklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE tenant_id='$org'),
 'items',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklist_items c WHERE tenant_id='$org'),
 'attachments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE tenant_id='$org'),
 'watches',(SELECT jsonb_agg(to_jsonb(w) ORDER BY id) FROM watch_subscriptions w WHERE tenant_id='$org'),
 'labels',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_labels l WHERE tenant_id='$org'),
 'associations',(SELECT jsonb_agg(to_jsonb(a) ORDER BY card_id,label_id) FROM card_labels a WHERE tenant_id='$org'),
 'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY card_id,user_id) FROM card_members m WHERE tenant_id='$org'),
 'notifications',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='$org'),
 'events',(SELECT jsonb_agg(to_jsonb(e)-'ready_at' ORDER BY board_id,sequence) FROM work_events e WHERE tenant_id='$org'),
 'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY board_id) FROM work_event_streams s WHERE tenant_id='$org'),
 'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$org'),
 'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY actor_id,key_id) FROM work_command_replays r WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'))::text);"; }
body=$(jq -nc --arg source "$source" --arg list "$destination_list" '{sourceBoardId:$source,destinationListId:$list,expectedVersion:4}')
before=$(state)
test "$(request member POST "/cards/$card/move" "$body")" = 404
test "$(state)" = "$before"
test "$(request owner PATCH "/boards/$destination/members/$member" '{"role":"MEMBER"}')" = 200
notifications_before=$(admin "SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='$org';")
member_before=$(admin "SELECT to_jsonb(m)-'board_id'-'updated_at'-'version' FROM card_members m WHERE tenant_id='$org' AND card_id='$card' AND user_id='$member';")
before=$(state); key=$(uuid)
admin 'REVOKE INSERT ON work_events FROM strataai_api_runtime;' >/dev/null; revoked=true
test "$(request member POST "/cards/$card/move" "$body" "$key")" = 503
admin 'GRANT INSERT ON work_events TO strataai_api_runtime;' >/dev/null; revoked=false
test "$(state)" = "$before"
test "$(request member POST "/cards/$card/move" "$body" "$key")" = 200
cp "$scratch/response.json" "$scratch/ack.json"
jq -e --arg card "$card" --arg board "$destination" --arg list "$destination_list" '.id==$card and .boardId==$board and .listId==$list and .version==5' "$scratch/ack.json" >/dev/null
test "$(admin "SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='$org';")" = "$notifications_before"
test "$(admin "SELECT to_jsonb(m)-'board_id'-'updated_at'-'version' FROM card_members m WHERE tenant_id='$org' AND card_id='$card' AND user_id='$member';")" = "$member_before"
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND card_id='$card' AND board_id='$destination' AND user_id='$member' AND version=2;")" = 1
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND card_id='$card';")" = 1
get owner "/cards/$card/labels" | jq -e --arg original "$label" --arg destination "$destination" '.boardId==$destination and .cardVersion==5 and (.items|length)==1 and .items[0].id!=$original and .items[0].name=="Preserved label" and .items[0].color=="blue"' >/dev/null
get owner "/boards/$source" | jq -e --arg card "$card" 'all(.lists[].cards[]; .id!=$card)' >/dev/null
get owner "/boards/$destination" | jq -e --arg card "$card" '[.lists[].cards[]|select(.id==$card and .version==5)]|length==1' >/dev/null
get owner "/cards/$card/activity" | jq -e --arg source "$source" --arg destination "$destination" '
 [.items[]|select(.eventType=="CARD_MOVED")] as $moves |
 ($moves|length)==2 and ($moves|map(.eventId)|unique|length)==2 and
 ($moves|map(.boardId)|sort)==([$source,$destination]|sort) and all($moves[];.currentBoardId==$destination and .metadata=={})' >/dev/null
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND event_type='CARD_MOVED' AND entity_id='$card';")" = 1
test "$(request owner PATCH "/cards/$card" '{"title":"Later moved revision","version":5}')" = 200
before=$(state)
test "$(request member POST "/cards/$card/move" "$body" "$key")" = 200
test "$(jq -Sc . "$scratch/response.json")" = "$(jq -Sc . "$scratch/ack.json")"
test "$(state)" = "$before"
test "$(request owner DELETE "/boards/$source/members/$member" '{}')" = 204
before=$(state)
test "$(request member POST "/cards/$card/move" "$body" "$key")" = 404
test "$(state)" = "$before"
get member "/boards/$destination" | jq -e '.access.canEdit==true' >/dev/null
get owner "/cards/$card/members" | jq -e --arg member "$member" --arg destination "$destination" '.boardId==$destination and .cardVersion==6 and (.items|length)==1 and .items[0].userId==$member' >/dev/null
# Stable Card-owned objects must survive movement while their readers admit the
# current destination, even when the departing user still sees the source Board.
test "$(request owner POST "/lists/$source_list/cards" '{"title":"Moved child subject","description":"Stable child body"}')" = 201
child_card=$(jq -r '.id' "$scratch/response.json")
test "$(request owner POST "/cards/$child_card/checklists" '{"title":"Stable checklist","cardVersion":1}')" = 200
checklist=$(jq -r '.checklist.id' "$scratch/response.json")
test "$(request owner POST "/cards/$child_card/checklists/$checklist/items" '{"text":"Stable item","cardVersion":2,"checklistVersion":1}')" = 200
jq '.item' "$scratch/response.json" > "$scratch/item-before.json"
test "$(request owner POST "/cards/$child_card/attachments/url" '{"title":"Stable URL","url":"https://example.test/moved","cardVersion":3}')" = 200
jq '.attachment' "$scratch/response.json" > "$scratch/attachment-before.json"
test "$(request other POST "/cards/$child_card/comments" '{"content":"Stable comment","cardVersion":4}')" = 200
jq '.comment' "$scratch/response.json" > "$scratch/comment-before.json"
comment=$(jq -r '.comment.id' "$scratch/response.json")
test "$(request owner PUT "/watch/CARD/$child_card?version=0" '{}')" = 200
jq '.' "$scratch/response.json" > "$scratch/watch-before.json"
test "$(request other PUT "/watch/CARD/$child_card?version=0" '{}')" = 200
get owner "/cards/$child_card/checklists" | jq '.items[0].checklist' > "$scratch/checklist-before.json"
child_body=$(jq -nc --arg source "$source" --arg list "$destination_list" '{sourceBoardId:$source,destinationListId:$list,expectedVersion:5}')
test "$(request owner POST "/cards/$child_card/move" "$child_body")" = 200
jq -e --arg card "$child_card" --arg board "$destination" '.id==$card and .boardId==$board and .version==6 and .description=="Stable child body"' "$scratch/response.json" >/dev/null
for kind in checklists attachments comments; do
 get owner "/cards/$child_card/$kind" > "$scratch/$kind-after.json"
 jq -e --arg board "$destination" --arg card "$child_card" '.boardId==$board and .cardId==$card and .cardVersion==6 and (.items|length)==1' "$scratch/$kind-after.json" >/dev/null
done
jq -e --slurpfile original "$scratch/checklist-before.json" '.items[0].checklist==$original[0]' "$scratch/checklists-after.json" >/dev/null
jq -e --slurpfile original "$scratch/attachment-before.json" '.items[0]==$original[0]' "$scratch/attachments-after.json" >/dev/null
jq -e --slurpfile original "$scratch/comment-before.json" '.items[0]==$original[0]' "$scratch/comments-after.json" >/dev/null
get owner "/cards/$child_card/checklists/$checklist/items" | jq -e --arg board "$destination" --slurpfile original "$scratch/item-before.json" '.boardId==$board and (.items|length)==1 and .items[0]==$original[0]' >/dev/null
get owner "/watch/CARD/$child_card" | jq -e --arg board "$destination" --slurpfile original "$scratch/watch-before.json" '.boardId==$board and .watching==true and .subscriptionId==$original[0].subscriptionId and .version==$original[0].version' >/dev/null
before=$(state)
for path in "/cards/$child_card/checklists" "/cards/$child_card/checklists/$checklist/items" "/cards/$child_card/attachments" "/cards/$child_card/comments" "/watch/CARD/$child_card"; do
 test "$(curl --silent --show-error -b "$scratch/other.cookies" -o "$scratch/refused.json" -w '%{http_code}' "$base$path")" = 404
 ! grep -q 'Stable' "$scratch/refused.json"
done
test "$(request other PATCH "/cards/$child_card/comments/$comment" '{"content":"Denied","cardVersion":6,"version":1}')" = 404
get other "/boards/$source" | jq -e --arg board "$source" '.board.id==$board' >/dev/null
test "$(state)" = "$before"

# Opposing directions must acquire the same canonical pair of Board gates.
# Two simultaneous real commands on distinct Cards must both commit, rather
# than deadlock after each takes its own source first.
test "$(request owner POST "/lists/$source_list/cards" '{"title":"Opposing source Card"}')" = 201
opposing_source=$(jq -r '.id' "$scratch/response.json")
test "$(request owner POST "/lists/$destination_list/cards" '{"title":"Opposing destination Card"}')" = 201
opposing_destination=$(jq -r '.id' "$scratch/response.json")
for id in "$opposing_source" "$opposing_destination"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
jq -nc --arg source "$source" --arg list "$destination_list" '{sourceBoardId:$source,destinationListId:$list,expectedVersion:1}' > "$scratch/opposing-0.body"
jq -nc --arg source "$destination" --arg list "$source_list" '{sourceBoardId:$source,destinationListId:$list,expectedVersion:1}' > "$scratch/opposing-1.body"
parallel_move() {
 local index=$1 card=$2
 curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $(uuid)" --data-binary "@$scratch/opposing-$index.body" -o "$scratch/opposing-$index.json" -w '%{http_code}' "$base/cards/$card/move" > "$scratch/opposing-$index.code"
}
parallel_move 0 "$opposing_source" & first_pid=$!
parallel_move 1 "$opposing_destination" & second_pid=$!
wait "$first_pid"; wait "$second_pid"
test "$(cat "$scratch/opposing-0.code")" = 200; test "$(cat "$scratch/opposing-1.code")" = 200
jq -e --arg card "$opposing_source" --arg board "$destination" --arg list "$destination_list" '.id==$card and .boardId==$board and .listId==$list and .version==2' "$scratch/opposing-0.json" >/dev/null
jq -e --arg card "$opposing_destination" --arg board "$source" --arg list "$source_list" '.id==$card and .boardId==$board and .listId==$list and .version==2' "$scratch/opposing-1.json" >/dev/null
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id IN ('$opposing_source','$opposing_destination') AND event_type='CARD_MOVED';")" = 4
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND entity_id IN ('$opposing_source','$opposing_destination') AND event_type='CARD_MOVED';")" = 2

echo 'Exact-image cross-Board movement: scoped references, atomic rollback, history, original retry and source revocation passed.'
