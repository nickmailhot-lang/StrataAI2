#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable checklist fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d)
gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'GRANT INSERT ON checklists, checklist_items, audit_events, work_events, background_jobs TO strataai_api_runtime;' >/dev/null || true
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Checklist API check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
for actor in owner member outsider; do
  jq -nc --arg email "checklists-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"checklist-fixture-battery-horse",displayName:"Checklist fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user")
member=$(jq -r '.user.id' "$scratch/member.user")
org=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Checklist transactions"}' "$base/organizations" | jq -r '.organization.id')
board=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Private checklist Board",visibility:"PRIVATE"}')" "$base/boards" | jq -r '.id')
list=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Checklist List"}' "$base/boards/$board/lists" | jq -r '.id')
card=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"title":"Checklist Card"}' "$base/lists/$list/cards" | jq -r '.id')
for id in "$owner" "$member" "$org" "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES(gen_random_uuid(),'$org','$board','$member','MEMBER','ACTIVE',now(),now());" >/dev/null
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -d "$5" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
read_page() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/page.json" -w '%{http_code}' "$base$2"; }
read_anonymous() { curl --max-time 60 --silent --show-error -o "$scratch/page.json" -w '%{http_code}' "$base$1"; }
hold_board() {
  rm -f "$scratch/gate.in" "$scratch/gate.log"; mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 &
  gate_pid=$!; exec 3> "$scratch/gate.in"
  printf 'BEGIN;\nSELECT id FROM boards WHERE tenant_id=%s AND id=%s FOR UPDATE;\n\\echo checklist_locked\n' "'$org'" "'$board'" >&3
  for ((attempt=0; attempt<100; attempt++)); do
    if grep -q '^checklist_locked$' "$scratch/gate.log"; then return; fi
    kill -0 "$gate_pid" || return 1; sleep 0.05
  done
  echo 'Checklist parent lock was not acquired.' >&2; return 1
}
blocked_read() {
  local count
  for ((attempt=0; attempt<100; attempt++)); do
    count=$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%FOR UPDATE%';")
    [[ "$count" =~ ^[0-9]+$ ]] || return 1
    if ((count>0)); then return; fi
    sleep 0.05
  done
  echo 'Checklist read did not reach the expected database lock wait.' >&2; return 1
}
state() { admin "SELECT md5(jsonb_build_object(
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'checklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE tenant_id='$org'),
 'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY id) FROM checklist_items i WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'notifications',(SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
path="/cards/$card/checklists"; key=$(uuid); payload='{"title":" Preparations ","cardVersion":1}'
before=$(state)
test "$(read_page outsider "$path?after=malformed")" = 404
test "$(request outsider POST "$path" "$(uuid)" "$payload")" = 404
test "$(request owner POST "$path" "$(uuid)" '{"title":" ","cardVersion":1}')" = 400
test "$before" = "$(state)"
# Fail both before child insertion and after Card/child/audit writes. Neither
# failure may reserve the retry key or leave any partially committed state.
for table in checklists work_events; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request owner POST "$path" "$key" "$payload")" = 503
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
test "$(request owner POST "$path" "$key" "$payload")" = 200
jq -e '.changed and .cardVersion==2 and .checklist.title=="Preparations" and .checklist.version==1' "$scratch/response.json" >/dev/null
checklist=$(jq -r '.checklist.id' "$scratch/response.json")
cp "$scratch/response.json" "$scratch/created.json"
after=$(state)
test "$(request owner POST "$path" "$key" "$payload")" = 200
cmp "$scratch/created.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(request owner POST "$path" "$key" '{"title":"Changed intent","cardVersion":1}')" = 409
test "$(request owner POST "$path" "$(uuid)" "$payload")" = 409
test "$after" = "$(state)"
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='CHECKLIST_CREATED' AND entity_type='Card' AND entity_id='$card' AND entity_version=2;")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND event_type='CHECKLIST_CREATED' AND entity_type='Checklist' AND entity_id='$checklist';")" = 1
test "$(read_page member "$path")" = 200
jq -e '.canEdit and .cardVersion==2 and (.items|length)==1 and .items[0].completed==0 and .items[0].total==0 and .items[0].percent==0' "$scratch/page.json" >/dev/null
renamePath="$path/$checklist"; renameKey=$(uuid); renameInput='{"title":" Revised preparations ","cardVersion":2,"version":1}'
before=$(state)
test "$(request outsider PATCH "$renamePath" "$(uuid)" "$renameInput")" = 404
test "$(request owner PATCH "$renamePath" "$(uuid)" '{"title":" ","cardVersion":2,"version":1}')" = 400
admin 'REVOKE INSERT ON work_events FROM strataai_api_runtime;' >/dev/null
test "$(request member PATCH "$renamePath" "$renameKey" "$renameInput")" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON work_events TO strataai_api_runtime;' >/dev/null
test "$(request member PATCH "$renamePath" "$renameKey" "$renameInput")" = 200
jq -e '.changed and .cardVersion==3 and .checklist.title=="Revised preparations" and .checklist.version==2' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/renamed.json"
after=$(state)
test "$(request member PATCH "$renamePath" "$renameKey" "$renameInput")" = 200
cmp "$scratch/renamed.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(request owner PATCH "$renamePath" "$(uuid)" "$renameInput")" = 409
test "$after" = "$(state)"
test "$(request owner PATCH "$renamePath" "$(uuid)" '{"title":" Revised preparations ","cardVersion":3,"version":2}')" = 200
jq -e '.changed==false and .cardVersion==3 and .checklist.version==2' "$scratch/response.json" >/dev/null
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='CHECKLIST_UPDATED';")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND event_type='CHECKLIST_UPDATED';")" = 1
itemPath="$path/$checklist/items"; itemKey=$(uuid); itemInput='{"text":" Pack supplies ","cardVersion":3,"checklistVersion":2}'
before=$(state)
test "$(read_page outsider "$itemPath?after=malformed")" = 404
test "$(request outsider POST "$itemPath" "$(uuid)" "$itemInput")" = 404
test "$(request owner POST "$itemPath" "$(uuid)" '{"text":" ","cardVersion":3,"checklistVersion":2}')" = 400
for table in checklist_items background_jobs; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request member POST "$itemPath" "$itemKey" "$itemInput")" = 503
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
test "$(request member POST "$itemPath" "$itemKey" "$itemInput")" = 200
jq -e '.changed and .cardVersion==4 and .checklist.version==3 and .item.text=="Pack supplies" and .item.version==1 and .item.completed==false and .item.completedAt==null and .item.completedBy==null' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/item-created.json"
after=$(state)
test "$(request member POST "$itemPath" "$itemKey" "$itemInput")" = 200
cmp "$scratch/item-created.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(request owner POST "$itemPath" "$(uuid)" "$itemInput")" = 409
test "$after" = "$(state)"
test "$(read_page owner "$itemPath")" = 200
jq -e '(.items|length)==1 and .summary.total==1 and .summary.completed==0 and .summary.percent==0' "$scratch/page.json" >/dev/null
# Visibility changes use the real producer. Public reads do not create receipts
# or alter child versions, and an authenticated outsider gains no editing grant.
test "$(read_anonymous "$path?after=malformed")" = 404
test "$(read_anonymous "$itemPath")" = 404
version=$(admin "SELECT version FROM boards WHERE tenant_id='$org' AND id='$board';")
test "$(request owner PATCH "/boards/$board/visibility" "$(uuid)" "{\"visibility\":\"PUBLIC\",\"version\":$version}")" = 200
before=$(state)
test "$(read_anonymous "$path")" = 200
jq -e '.canEdit==false and .cardVersion==4 and (.items|length)==1 and .items[0].total==1' "$scratch/page.json" >/dev/null
test "$(read_anonymous "$itemPath")" = 200
jq -e '.canEdit==false and .summary.total==1 and .items[0].text=="Pack supplies"' "$scratch/page.json" >/dev/null
test "$(read_page outsider "$itemPath")" = 200
jq -e '.canEdit==false' "$scratch/page.json" >/dev/null
test "$(request outsider POST "$itemPath" "$(uuid)" '{"text":"Denied","cardVersion":4,"checklistVersion":3}')" = 404
test "$(read_anonymous "$path?after=malformed")" = 400
test "$before" = "$(state)"
version=$(admin "SELECT version FROM boards WHERE tenant_id='$org' AND id='$board';")
test "$(request owner PATCH "/boards/$board/visibility" "$(uuid)" "{\"visibility\":\"PRIVATE\",\"version\":$version}")" = 200
test "$(read_anonymous "$path")" = 404
test "$(read_anonymous "$itemPath")" = 404
test "$(read_page outsider "$itemPath")" = 404
# A visitor arriving before visibility changes must be denied after its actual
# Board-lock wait. Direct SQL here represents the concurrently committed change.
for route in "$path" "$itemPath"; do
  version=$(admin "SELECT version FROM boards WHERE tenant_id='$org' AND id='$board';")
  test "$(request owner PATCH "/boards/$board/visibility" "$(uuid)" "{\"visibility\":\"PUBLIC\",\"version\":$version}")" = 200
  before=$(state); hold_board
  curl --max-time 60 --silent --show-error -o "$scratch/waited-page.json" -w '%{http_code}' "$base$route" > "$scratch/waited-status" &
  request_pid=$!; blocked_read
  printf "UPDATE boards SET visibility='PRIVATE',version=version+1,updated_at=now() WHERE tenant_id='%s' AND id='%s';\nCOMMIT;\n\\q\n" "$org" "$board" >&3
  exec 3>&-; wait "$gate_pid"; gate_pid=''
  wait "$request_pid"; request_pid=''
  test "$(cat "$scratch/waited-status")" = 404
  scripts/ci/assert-file-excludes.sh 'Preparations|preparations|Pack supplies' "$scratch/waited-page.json"
  test "$before" = "$(state)"
done
# Canonical child rows exercise aggregate progress across a bounded checklist
# page, including deleted-item exclusion and an empty sibling's zero progress.
admin "INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,completed,completed_at,completed_by,created_at,updated_at,deleted_at)
 SELECT gen_random_uuid(),'$org','$checklist','Progress fixture',lpad((n*1000)::text,30,'0'),n IN (1,64),
 CASE WHEN n IN (1,64) THEN now() END,CASE WHEN n IN (1,64) THEN '$owner'::uuid END,now(),now(),CASE WHEN n=64 THEN now() END FROM generate_series(1,64) n WHERE n<>63;
 INSERT INTO checklists(id,tenant_id,card_id,title,rank,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$card','Page '||n,lpad((n*1000)::text,30,'0'),now(),now() FROM generate_series(1,62) n;" >/dev/null
test "$(read_page member "$path")" = 200
jq -e '(.items|length)==50 and .nextCursor!=null and all(.items[];.total==0 and .percent==0)' "$scratch/page.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/page.json")
cp "$scratch/page.json" "$scratch/first.json"
test "$(read_page member "$path?after=$cursor")" = 200
jq -e --arg checklist "$checklist" '(.items|length)==13 and .nextCursor==null and any(.items[];.checklist.id==$checklist and .completed==1 and .total==63 and .percent>1 and .percent<2)' "$scratch/page.json" >/dev/null
jq -se '[.[].items[].checklist.id] | length==63 and (unique|length)==63' "$scratch/first.json" "$scratch/page.json" >/dev/null
test "$(read_page owner "$path?after=malformed")" = 400
test "$(read_page owner "$itemPath")" = 200
jq -e '(.items|length)==50 and .nextCursor!=null and .summary.total==63 and .summary.completed==1' "$scratch/page.json" >/dev/null
itemCursor=$(jq -r '.nextCursor' "$scratch/page.json")
cp "$scratch/page.json" "$scratch/items-first.json"
test "$(read_page owner "$itemPath?after=$itemCursor")" = 200
jq -e '(.items|length)==13 and .nextCursor==null and .summary.total==63 and .summary.completed==1 and all(.items[];.deletedAt==null)' "$scratch/page.json" >/dev/null
jq -se '[.[].items[].id] | length==63 and (unique|length)==63' "$scratch/items-first.json" "$scratch/page.json" >/dev/null
# Copy the complete child graph inside the List transaction, beyond GET page size.
# Every copied item starts incomplete; deleted children are excluded.
copyKey=$(uuid); copyInput=$(jq -nc --arg board "$board" '{destinationBoardId:$board,name:"Checklist graph copy",version:1}')
copyPath="/lists/$list/copy"
before=$(state)
parentCounts=$(admin "SELECT (SELECT count(*) FROM cards WHERE tenant_id='$org')||'/'||(SELECT count(*) FROM board_lists WHERE tenant_id='$org');")
for table in checklists checklist_items audit_events work_events background_jobs; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request owner POST "$copyPath" "$copyKey" "$copyInput")" = 503
  test "$before" = "$(state)"
  test "$parentCounts" = "$(admin "SELECT (SELECT count(*) FROM cards WHERE tenant_id='$org')||'/'||(SELECT count(*) FROM board_lists WHERE tenant_id='$org');")"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
test "$(request owner POST "$copyPath" "$copyKey" "$copyInput")" = 201
cp "$scratch/response.json" "$scratch/list-copy.json"
copiedList=$(jq -r '.id' "$scratch/response.json")
copiedCard=$(admin "SELECT id FROM cards WHERE tenant_id='$org' AND list_id='$copiedList';")
copiedChecklist=$(admin "SELECT id FROM checklists WHERE tenant_id='$org' AND card_id='$copiedCard' AND title='Revised preparations';")
test "$copiedCard" != "$card"; test "$copiedChecklist" != "$checklist"
test "$(admin "SELECT count(*) FROM checklists WHERE tenant_id='$org' AND card_id='$copiedCard' AND version=1 AND deleted_at IS NULL;")" = 63
test "$(admin "SELECT count(*) FROM checklist_items WHERE tenant_id='$org' AND checklist_id='$copiedChecklist' AND version=1 AND NOT completed AND completed_at IS NULL AND completed_by IS NULL AND deleted_at IS NULL;")" = 63
test "$(admin "SELECT count(*) FROM checklist_items s JOIN checklist_items d ON d.tenant_id=s.tenant_id AND d.rank=s.rank AND d.text=s.text AND d.id<>s.id WHERE s.tenant_id='$org' AND s.checklist_id='$checklist' AND s.deleted_at IS NULL AND d.checklist_id='$copiedChecklist';")" = 63
test "$(read_page owner "/cards/$copiedCard/checklists/$copiedChecklist/items")" = 200
jq -e '.cardVersion==1 and .summary.total==63 and .summary.completed==0 and .summary.percent==0 and (.items|length)==50 and .nextCursor!=null' "$scratch/page.json" >/dev/null
after=$(state)
test "$(request owner POST "$copyPath" "$copyKey" "$copyInput")" = 201
cmp "$scratch/list-copy.json" "$scratch/response.json"
test "$after" = "$(state)"
item=$(jq -r '.item.id' "$scratch/item-created.json")
editPath="$itemPath/$item"; completeKey=$(uuid); completeInput='{"text":" Pack supplies ","completed":true,"cardVersion":4,"checklistVersion":3,"version":1}'
before=$(state)
test "$(request outsider PATCH "$editPath" "$(uuid)" "$completeInput")" = 404
test "$(request owner PATCH "$editPath" "$(uuid)" '{"text":"Pack supplies","cardVersion":4,"checklistVersion":3,"version":1}')" = 400
for table in work_events background_jobs; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request member PATCH "$editPath" "$completeKey" "$completeInput")" = 503
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
test "$(request member PATCH "$editPath" "$completeKey" "$completeInput")" = 200
jq -e --arg member "$member" '.changed and .cardVersion==5 and .checklist.version==4 and .item.version==2 and .item.completed and .item.completedBy==$member and .item.completedAt!=null' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/completed.json"
after=$(state)
test "$(request member PATCH "$editPath" "$completeKey" "$completeInput")" = 200
cmp "$scratch/completed.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(request owner PATCH "$editPath" "$(uuid)" '{"text":" Pack supplies ","completed":true,"cardVersion":5,"checklistVersion":4,"version":2}')" = 200
jq -e '.changed==false and .cardVersion==5 and .item.version==2' "$scratch/response.json" >/dev/null
test "$(request owner PATCH "$editPath" "$(uuid)" '{"text":"Edited supplies","completed":true,"cardVersion":5,"checklistVersion":4,"version":2}')" = 200
jq -se '.[1].cardVersion==6 and .[1].checklist.version==5 and .[1].item.version==3 and .[1].item.completedBy==.[0].item.completedBy and .[1].item.completedAt==.[0].item.completedAt' "$scratch/completed.json" "$scratch/response.json" >/dev/null
# A simultaneous text change/uncompletion emits both facts but advances each
# aggregate row only once and clears attribution atomically.
test "$(request owner PATCH "$editPath" "$(uuid)" '{"text":"Pack supplies","completed":false,"cardVersion":6,"checklistVersion":5,"version":3}')" = 200
jq -e '.changed and .cardVersion==7 and .checklist.version==6 and .item.version==4 and .item.completed==false and .item.completedAt==null and .item.completedBy==null' "$scratch/response.json" >/dev/null
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='CHECKLIST_ITEM_UPDATED';")" = 2
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='CHECKLIST_ITEM_COMPLETED';")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='CHECKLIST_ITEM_UNCOMPLETED';")" = 1
test "$(admin "SELECT NOT due_complete FROM cards WHERE tenant_id='$org' AND id='$card';")" = t
test "$(read_page owner "$itemPath")" = 200
jq -e '.summary.completed==1 and .summary.total==63 and .cardVersion==7' "$scratch/page.json" >/dev/null
firstChecklist=$(admin "SELECT id FROM checklists WHERE tenant_id='$org' AND card_id='$card' AND title='Page 1';")
positionPath="$path/$checklist/position"; positionKey=$(uuid)
positionInput=$(jq -nc --arg before "$firstChecklist" '{beforeId:$before,cardVersion:7,version:6}')
before=$(state)
admin 'REVOKE INSERT ON work_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "$positionPath" "$positionKey" "$positionInput")" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON work_events TO strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "$positionPath" "$positionKey" "$positionInput")" = 200
jq -e '.changed and .cardVersion==8 and .checklist.version==7' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/position.json"
after=$(state)
test "$(request owner PATCH "$positionPath" "$positionKey" "$positionInput")" = 200
cmp "$scratch/position.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(read_page owner "$path")" = 200
jq -e --arg checklist "$checklist" '.items[0].checklist.id==$checklist' "$scratch/page.json" >/dev/null
test "$(request owner PATCH "$positionPath" "$(uuid)" "$(jq -c '.cardVersion=8|.version=7' <<< "$positionInput")")" = 200
jq -e '.changed==false and .cardVersion==8 and .checklist.version==7' "$scratch/response.json" >/dev/null
test "$(request owner PATCH "$positionPath" "$(uuid)" '{"beforeId":null,"cardVersion":8,"version":7}')" = 200
jq -e '.changed and .cardVersion==9 and .checklist.version==8' "$scratch/response.json" >/dev/null
firstItem=$(admin "SELECT id FROM checklist_items WHERE tenant_id='$org' AND checklist_id='$checklist' AND deleted_at IS NULL ORDER BY rank LIMIT 1;")
secondItem=$(admin "SELECT id FROM checklist_items WHERE tenant_id='$org' AND checklist_id='$checklist' AND deleted_at IS NULL ORDER BY rank OFFSET 1 LIMIT 1;")
itemPositionPath="$editPath/position"; itemPositionKey=$(uuid)
itemPositionInput=$(jq -nc --arg before "$firstItem" '{beforeId:$before,cardVersion:9,checklistVersion:8,version:4}')
before=$(state)
admin 'REVOKE INSERT ON background_jobs FROM strataai_api_runtime;' >/dev/null
test "$(request member PATCH "$itemPositionPath" "$itemPositionKey" "$itemPositionInput")" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON background_jobs TO strataai_api_runtime;' >/dev/null
test "$(request member PATCH "$itemPositionPath" "$itemPositionKey" "$itemPositionInput")" = 200
jq -e '.changed and .cardVersion==10 and .checklist.version==9 and .item.version==5 and .item.completed==false and .item.completedBy==null' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/item-position.json"
after=$(state)
test "$(request member PATCH "$itemPositionPath" "$itemPositionKey" "$itemPositionInput")" = 200
cmp "$scratch/item-position.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(read_page owner "$itemPath")" = 200
jq -e --arg item "$item" '.items[0].id==$item and .summary.completed==1 and .summary.total==63' "$scratch/page.json" >/dev/null
test "$(request owner PATCH "$itemPositionPath" "$(uuid)" "$(jq -c '.cardVersion=10|.checklistVersion=9|.version=5' <<< "$itemPositionInput")")" = 200
jq -e '.changed==false and .cardVersion==10 and .item.version==5' "$scratch/response.json" >/dev/null
test "$(request owner PATCH "$itemPositionPath" "$(uuid)" '{"beforeId":null,"cardVersion":10,"checklistVersion":9,"version":5}')" = 200
jq -e '.changed and .cardVersion==11 and .checklist.version==10 and .item.version==6' "$scratch/response.json" >/dev/null
# Exhaust a genuine interval between two neighboring active items. Position
# validation must leave the Card, Checklist, item, audit/outbox and receipt intact.
admin "UPDATE checklist_items SET rank=lpad((CASE WHEN id='$firstItem' THEN 1 ELSE 2 END)::text,30,'0') WHERE tenant_id='$org' AND id IN ('$firstItem','$secondItem');" >/dev/null
before=$(state)
test "$(request owner PATCH "$itemPositionPath" "$(uuid)" "$(jq -nc --arg before "$secondItem" '{beforeId:$before,cardVersion:11,checklistVersion:10,version:6}')")" = 409
jq -e '.code=="rank_space_exhausted"' "$scratch/response.json" >/dev/null
test "$before" = "$(state)"
# A valid but exhausted tail must fail before advancing the aggregate revision.
admin "UPDATE checklists SET rank='999999999999999999999999999998' WHERE id='$checklist';" >/dev/null
before=$(state)
test "$(request owner POST "$path" "$(uuid)" '{"title":"No remaining rank","cardVersion":11}')" = 409
jq -e '.code=="rank_space_exhausted"' "$scratch/response.json" >/dev/null
test "$before" = "$(state)"
deleteKey=$(uuid); deleteInput='{"confirmed":true,"cardVersion":11,"checklistVersion":10,"version":6}'
before=$(state)
test "$(request member DELETE "$editPath" "$(uuid)" "$deleteInput")" = 404
test "$(request owner DELETE "$editPath" "$(uuid)" "$(jq -c '.confirmed=false' <<< "$deleteInput")")" = 400
for table in work_events background_jobs; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request owner DELETE "$editPath" "$deleteKey" "$deleteInput")" = 503
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
test "$(request owner DELETE "$editPath" "$deleteKey" "$deleteInput")" = 200
jq -e '.changed and .cardVersion==12 and .checklist.version==11 and .item.version==7 and .item.deletedAt!=null and .item.deletedAt==.item.updatedAt and .item.text=="Pack supplies"' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/deleted.json"
after=$(state)
test "$(request owner DELETE "$editPath" "$deleteKey" "$deleteInput")" = 200
cmp "$scratch/deleted.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(request member POST "$itemPath" "$itemKey" "$itemInput")" = 404
test "$(request owner PATCH "$editPath" "$(uuid)" '{"text":"Resurrect","completed":false,"cardVersion":12,"checklistVersion":11,"version":7}')" = 404
test "$(request owner DELETE "$editPath" "$(uuid)" '{"confirmed":true,"cardVersion":12,"checklistVersion":11,"version":7}')" = 200
jq -e '.changed==false and .cardVersion==12 and .item.version==7' "$scratch/response.json" >/dev/null
test "$(read_page member "$itemPath")" = 200
jq -e '.summary.completed==1 and .summary.total==62' "$scratch/page.json" >/dev/null
test "$(admin "SELECT count(*) FROM checklist_items WHERE tenant_id='$org' AND id='$item' AND deleted_at IS NOT NULL;")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='CHECKLIST_ITEM_DELETED';")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND event_type='CHECKLIST_ITEM_DELETED';")" = 1
cascadePath="$path/$checklist"; cascadeKey=$(uuid); cascadeInput='{"confirmed":true,"cardVersion":12,"version":11}'
oldTombstones=$(admin "SELECT md5(jsonb_agg(to_jsonb(i) ORDER BY id)::text) FROM checklist_items i WHERE tenant_id='$org' AND checklist_id='$checklist' AND deleted_at IS NOT NULL;")
before=$(state)
test "$(request member DELETE "$cascadePath" "$(uuid)" "$cascadeInput")" = 404
test "$(request owner DELETE "$cascadePath" "$(uuid)" "$(jq -c '.confirmed=false' <<< "$cascadeInput")")" = 400
for table in audit_events work_events background_jobs; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request owner DELETE "$cascadePath" "$cascadeKey" "$cascadeInput")" = 503
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
test "$(request owner DELETE "$cascadePath" "$cascadeKey" "$cascadeInput")" = 200
jq -e '.changed and .cardVersion==13 and .checklist.version==12 and .checklist.deletedAt!=null and .deletedItems==62' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/cascade.json"
after=$(state)
test "$(request owner DELETE "$cascadePath" "$cascadeKey" "$cascadeInput")" = 200
cmp "$scratch/cascade.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(admin "SELECT count(*) FROM checklist_items WHERE tenant_id='$org' AND checklist_id='$checklist' AND deleted_at IS NULL;")" = 0
test "$(admin "SELECT md5(jsonb_agg(to_jsonb(i) ORDER BY id)::text) FROM checklist_items i WHERE tenant_id='$org' AND checklist_id='$checklist' AND (id='$item' OR rank=lpad('64000',30,'0'));")" = "$oldTombstones"
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND event_type='CHECKLIST_ITEM_DELETED';")" = 63
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND event_type='CHECKLIST_DELETED' AND entity_id='$checklist';")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='CHECKLIST_DELETED';")" = 1
test "$(read_page owner "$itemPath")" = 404
test "$(request owner POST "$path" "$key" "$payload")" = 404
test "$(request owner DELETE "$editPath" "$deleteKey" "$deleteInput")" = 404
test "$(request owner DELETE "$cascadePath" "$(uuid)" '{"confirmed":true,"cardVersion":13,"version":12}')" = 200
jq -e '.changed==false and .cardVersion==13 and .deletedItems==0' "$scratch/response.json" >/dev/null
test "$(read_page member "$path")" = 200
jq -e '(.items|length)==50 and all(.items[];.checklist.deletedAt==null)' "$scratch/page.json" >/dev/null
# A copied graph follows its Card to the new List, remains read-only while either
# parent is archived, and retains exact history when the List becomes deleted.
destinationListInput='{"name":"Retained checklist destination"}'
test "$(request owner POST "/boards/$board/lists" "$(uuid)" "$destinationListInput")" = 201
destinationList=$(jq -r '.id' "$scratch/response.json")
retained=$(admin "SELECT md5(jsonb_build_object('parents',(SELECT jsonb_agg(to_jsonb(c) ORDER BY c.id) FROM checklists c WHERE c.tenant_id='$org' AND c.card_id='$copiedCard'),'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY i.id) FROM checklist_items i JOIN checklists c ON c.tenant_id=i.tenant_id AND c.id=i.checklist_id WHERE c.tenant_id='$org' AND c.card_id='$copiedCard'))::text);")
test "$(request owner POST "/cards/$copiedCard/move" "$(uuid)" "{\"destinationListId\":\"$destinationList\",\"expectedVersion\":1}")" = 200
copiedPath="/cards/$copiedCard/checklists/$copiedChecklist/items"
test "$(read_page owner "$copiedPath")" = 200
jq -e '.canEdit and .cardVersion==2 and .summary.total==63' "$scratch/page.json" >/dev/null
test "$(request owner POST "/cards/$copiedCard/archive" "$(uuid)" '{"version":2}')" = 200
test "$(read_page owner "$copiedPath")" = 200
jq -e '.canEdit==false and .summary.total==63' "$scratch/page.json" >/dev/null
test "$(request owner POST "/cards/$copiedCard/checklists" "$(uuid)" '{"title":"Denied","cardVersion":3}')" = 404
test "$(request owner POST "/cards/$copiedCard/restore" "$(uuid)" '{"version":3}')" = 200
test "$(request owner POST "/lists/$destinationList/archive" "$(uuid)" '{"version":1}')" = 200
test "$(read_page owner "$copiedPath")" = 200
jq -e '.canEdit==false and .summary.total==63' "$scratch/page.json" >/dev/null
test "$(request owner POST "/cards/$copiedCard/checklists" "$(uuid)" '{"title":"Denied","cardVersion":4}')" = 404
test "$(request owner POST "/lists/$destinationList/restore" "$(uuid)" '{"version":2}')" = 200
test "$(read_page owner "$copiedPath")" = 200
jq -e '.canEdit and .summary.total==63' "$scratch/page.json" >/dev/null
test "$(request owner POST "/lists/$destinationList/archive" "$(uuid)" '{"version":3}')" = 200
test "$(request owner DELETE "/lists/$destinationList?version=4&confirmed=true&containedCardCount=1" "$(uuid)" '{}')" = 200
test "$(read_page owner "$copiedPath")" = 404
test "$(read_page owner "/cards/$copiedCard/checklists")" = 404
test "$retained" = "$(admin "SELECT md5(jsonb_build_object('parents',(SELECT jsonb_agg(to_jsonb(c) ORDER BY c.id) FROM checklists c WHERE c.tenant_id='$org' AND c.card_id='$copiedCard'),'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY i.id) FROM checklist_items i JOIN checklists c ON c.tenant_id=i.tenant_id AND c.id=i.checklist_id WHERE c.tenant_id='$org' AND c.card_id='$copiedCard'))::text);")"
admin "UPDATE board_members SET status='REMOVED',version=version+1 WHERE tenant_id='$org' AND board_id='$board' AND user_id='$member';" >/dev/null
test "$(read_page member "$path?after=$cursor")" = 404
test "$(request member PATCH "$renamePath" "$renameKey" "$renameInput")" = 404
test "$(read_page member "$itemPath?after=$itemCursor")" = 404
test "$(request member POST "$itemPath" "$itemKey" "$itemInput")" = 404
test "$(request member PATCH "$editPath" "$completeKey" "$completeInput")" = 404
test "$(request member PATCH "$itemPositionPath" "$itemPositionKey" "$itemPositionInput")" = 404
version=$(admin "SELECT version FROM boards WHERE tenant_id='$org' AND id='$board';")
test "$(request owner POST "/boards/$board/archive" "$(uuid)" "{\"version\":$version}")" = 200
test "$(read_page owner "$path")" = 200
jq -e '.canEdit==false' "$scratch/page.json" >/dev/null
before=$(state)
test "$(request owner POST "$path" "$key" "$payload")" = 404
test "$(request owner DELETE "$editPath" "$deleteKey" "$deleteInput")" = 404
test "$(request owner DELETE "$cascadePath" "$cascadeKey" "$cascadeInput")" = 404
test "$before" = "$(state)"
echo 'Checklist creation, canonical reads/progress, retry recovery, atomic rollback, rank exhaustion and lifecycle admission passed.'
