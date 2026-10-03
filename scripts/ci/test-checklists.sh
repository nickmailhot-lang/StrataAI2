#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable checklist fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d)
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  admin 'GRANT INSERT ON checklists, work_events TO strataai_api_runtime;' >/dev/null || true
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
# Canonical child rows exercise aggregate progress across a bounded checklist
# page, including deleted-item exclusion and an empty sibling's zero progress.
admin "INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,completed,completed_at,completed_by,created_at,updated_at,deleted_at)
 SELECT gen_random_uuid(),'$org','$checklist','Progress fixture',lpad(n::text,30,'0'),n<>2,
 CASE WHEN n<>2 THEN now() END,CASE WHEN n<>2 THEN '$owner'::uuid END,now(),now(),CASE WHEN n=3 THEN now() END FROM generate_series(1,3) n;
 INSERT INTO checklists(id,tenant_id,card_id,title,rank,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$card','Page '||n,lpad(n::text,30,'0'),now(),now() FROM generate_series(1,62) n;" >/dev/null
test "$(read_page member "$path")" = 200
jq -e '(.items|length)==50 and .nextCursor!=null and all(.items[];.total==0 and .percent==0)' "$scratch/page.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/page.json")
cp "$scratch/page.json" "$scratch/first.json"
test "$(read_page member "$path?after=$cursor")" = 200
jq -e --arg checklist "$checklist" '(.items|length)==13 and .nextCursor==null and any(.items[];.checklist.id==$checklist and .completed==1 and .total==2 and .percent==50)' "$scratch/page.json" >/dev/null
jq -se '[.[].items[].checklist.id] | length==63 and (unique|length)==63' "$scratch/first.json" "$scratch/page.json" >/dev/null
test "$(read_page owner "$path?after=malformed")" = 400
# A valid but exhausted tail must fail before advancing the aggregate revision.
admin "UPDATE checklists SET rank='999999999999999999999999999998' WHERE id='$checklist';" >/dev/null
before=$(state)
test "$(request owner POST "$path" "$(uuid)" '{"title":"No remaining rank","cardVersion":3}')" = 409
jq -e '.code=="rank_space_exhausted"' "$scratch/response.json" >/dev/null
test "$before" = "$(state)"
admin "UPDATE board_members SET status='REMOVED',version=version+1 WHERE tenant_id='$org' AND board_id='$board' AND user_id='$member';" >/dev/null
test "$(read_page member "$path?after=$cursor")" = 404
test "$(request member PATCH "$renamePath" "$renameKey" "$renameInput")" = 404
version=$(admin "SELECT version FROM boards WHERE tenant_id='$org' AND id='$board';")
test "$(request owner POST "/boards/$board/archive" "$(uuid)" "{\"version\":$version}")" = 200
test "$(read_page owner "$path")" = 200
jq -e '.canEdit==false' "$scratch/page.json" >/dev/null
before=$(state)
test "$(request owner POST "$path" "$key" "$payload")" = 404
test "$before" = "$(state)"
echo 'Checklist creation, canonical reads/progress, retry recovery, atomic rollback, rank exhaustion and lifecycle admission passed.'
