#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable Card copy fixture requires CI.' >&2; exit 1; }
umask 077
scratch=$(mktemp -d); revoked=false; base=http://localhost:8088
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() { if $revoked; then admin 'GRANT INSERT ON work_events TO strataai_api_runtime;' >/dev/null || true; fi; rm -rf "$scratch"; }
trap cleanup EXIT
trap 'echo "Card copy fixture failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
request() {
 local actor=$1 method=$2 path=$3 body=$4 key=${5:-$(uuid)}
 curl --max-time 60 --silent --show-error -b "$scratch/$actor.cookies" -X "$method" -H 'X-StrataAI-Request: 1' \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "$body" -o "$scratch/response.json" -w '%{http_code}' "$base$path"
}
get() { curl --max-time 60 --fail --silent --show-error -b "$scratch/$1.cookies" "$base$2"; }
for actor in owner member; do
 jq -nc --arg email "copy-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"copy-correct-horse-battery",displayName:"Copy fixture"}' > "$scratch/$actor.credentials"
 curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
 curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); member=$(jq -r '.user.id' "$scratch/member.user")
test "$(request owner POST /organizations '{"name":"Card copy release"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response.json")
for id in "$org" "$owner" "$member"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
# Trusted disposable Organization roster only; copy/grants/children below use
# actual release sessions and the restricted API, not synthetic actor admission.
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');" >/dev/null
for side in source destination; do
 test "$(request owner POST /boards "$(jq -nc --arg org "$org" --arg name "$side" '{organizationId:$org,name:$name,visibility:"PRIVATE"}')")" = 201
 jq -r '.id' "$scratch/response.json" > "$scratch/$side.board"
 test "$(request owner PATCH "/boards/$(cat "$scratch/$side.board")/members/$member" '{"role":"MEMBER"}')" = 200
 test "$(request owner POST "/boards/$(cat "$scratch/$side.board")/lists" '{"name":"Copy parent"}')" = 201
 jq -r '.id' "$scratch/response.json" > "$scratch/$side.list"
done
source=$(cat "$scratch/source.board"); destination=$(cat "$scratch/destination.board")
source_list=$(cat "$scratch/source.list"); destination_list=$(cat "$scratch/destination.list")
test "$(request owner POST "/lists/$source_list/cards" '{"title":"Source content","description":"Retained description"}')" = 201
card=$(jq -r '.id' "$scratch/response.json")
test "$(request owner POST "/boards/$source/labels" '{"name":"Copied label","color":"blue"}')" = 201
label=$(jq -r '.id' "$scratch/response.json")
test "$(request owner PUT "/cards/$card/labels/$label?version=1" '{}')" = 200
test "$(request owner PATCH "/cards/$card/dates" '{"startAt":null,"dueAt":"2099-01-01T12:00:00Z","dueTimezone":"UTC","dueHasTime":true,"dueComplete":true,"version":2}')" = 200
test "$(request owner POST "/cards/$card/checklists" '{"title":"Fresh work","cardVersion":3}')" = 200
checklist=$(jq -r '.checklist.id' "$scratch/response.json")
test "$(request owner POST "/cards/$card/checklists/$checklist/items" '{"text":"Copied work","cardVersion":4,"checklistVersion":1}')" = 200
item=$(jq -r '.item.id' "$scratch/response.json")
test "$(request owner PATCH "/cards/$card/checklists/$checklist/items/$item" '{"text":"Copied work","completed":true,"cardVersion":5,"checklistVersion":2,"version":1}')" = 200
test "$(request member POST "/cards/$card/comments" '{"content":"Original history","cardVersion":6}')" = 200
test "$(request member PUT "/watch/CARD/$card?version=0" '{}')" = 200
test "$(request owner PUT "/watch/BOARD/$destination?version=0" '{}')" = 200
test "$(request owner PUT "/cards/$card/members/$member?version=7" '{}')" = 200
for id in "$source" "$destination" "$source_list" "$destination_list" "$card" "$label" "$checklist" "$item"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
state() { admin "SELECT md5(jsonb_build_object(
 'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
 'routes',(SELECT jsonb_agg(to_jsonb(r) ORDER BY card_id) FROM card_routes r WHERE tenant_id='$org'),
 'labels',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_labels l WHERE tenant_id='$org'),
 'associations',(SELECT jsonb_agg(to_jsonb(a) ORDER BY card_id,label_id) FROM card_labels a WHERE tenant_id='$org'),
 'checklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE tenant_id='$org'),
 'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY id) FROM checklist_items i WHERE tenant_id='$org'),
 'notifications',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='$org'),
 'events',(SELECT jsonb_agg(to_jsonb(e)-'ready_at'-'updated_at' ORDER BY event_id) FROM work_events e WHERE tenant_id='$org'),
 'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$org'),
 'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY actor_id,key_id) FROM work_command_replays r WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'))::text);"; }
source_before=$(admin "SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card';")
body=$(jq -nc --arg source "$source" --arg list "$destination_list" '{sourceBoardId:$source,destinationListId:$list,title:"  Independent copy  ",expectedVersion:8}')
key=$(uuid); before=$(state)
admin 'REVOKE INSERT ON work_events FROM strataai_api_runtime;' >/dev/null; revoked=true
test "$(request member POST "/cards/$card/copy" "$body" "$key")" = 503
admin 'GRANT INSERT ON work_events TO strataai_api_runtime;' >/dev/null; revoked=false
test "$(state)" = "$before"
test "$(request member POST "/cards/$card/copy" "$body" "$key")" = 200
cp "$scratch/response.json" "$scratch/ack.json"; copy=$(jq -r '.id' "$scratch/response.json")
[[ "$copy" =~ ^[0-9a-f-]{36}$ ]]; test "$copy" != "$card"
jq -e --arg board "$destination" --arg list "$destination_list" '.boardId==$board and .listId==$list and .version==1 and .title=="Independent copy" and .description=="Retained description" and .dueAt!=null and .dueComplete==false' "$scratch/ack.json" >/dev/null
test "$(admin "SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card';")" = "$source_before"
get member "/cards/$copy/labels" | jq -e --arg source "$label" --arg board "$destination" '(.items|length)==1 and .items[0].id!=$source and .items[0].boardId==$board and .items[0].name=="Copied label"' >/dev/null
get member "/cards/$copy/checklists" > "$scratch/checklists.json"
jq -e --arg old "$checklist" '(.items|length)==1 and .items[0].checklist.id!=$old and .items[0].checklist.version==1' "$scratch/checklists.json" >/dev/null
copied_checklist=$(jq -r '.items[0].checklist.id' "$scratch/checklists.json")
get member "/cards/$copy/checklists/$copied_checklist/items" | jq -e --arg old "$item" '(.items|length)==1 and .items[0].id!=$old and .items[0].text=="Copied work" and .items[0].completed==false and .items[0].completedBy==null and .items[0].version==1' >/dev/null
get member "/cards/$copy/comments" | jq -e '(.items|length)==0' >/dev/null
get member "/cards/$copy/members" | jq -e '(.items|length)==0' >/dev/null
get member "/watch/CARD/$copy" | jq -e '.watching==false' >/dev/null
get member "/cards/$copy/activity" | jq -e '(.items|length)==1 and .items[0].eventType=="CARD_COPIED"' >/dev/null
get owner "/organizations/$org/notifications" | jq -e --arg copy "$copy" '[.items[]|select(.entityId==$copy)]|length==1 and .[0].type=="CARD_COPIED"' >/dev/null
test "$(request owner PATCH "/cards/$card" '{"title":"Later source","version":8}')" = 200
test "$(request member PATCH "/cards/$copy" '{"title":"Later copy","version":1}')" = 200
before=$(state)
test "$(request member POST "/cards/$card/copy" "$body" "$key")" = 200
cmp "$scratch/ack.json" "$scratch/response.json"; test "$(state)" = "$before"
test "$(request owner DELETE "/boards/$source/members/$member" '{}')" = 204
before=$(state)
test "$(request member POST "/cards/$card/copy" "$body" "$key")" = 404
test "$(state)" = "$before"
get member "/boards/$destination" >/dev/null
echo 'Exact-image Card copy: independent IDs/content/labels/fresh checklist work, original history isolation, eligible Watch notification, late publication rollback and currently admitted original receipt recovery passed.'
