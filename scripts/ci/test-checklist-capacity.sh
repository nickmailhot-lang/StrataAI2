#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable capacity fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
uuid() { cat /proc/sys/kernel/random/uuid; }
credentials=$(jq -nc --arg email "checklist-capacity-$(uuid)@example.test" '{email:$email,password:"checklist-capacity-correct-horse",displayName:"Capacity fixture"}')
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/register" > "$scratch/user.json"
curl --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null
owner=$(jq -r '.user.id' "$scratch/user.json")
post() { curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$2" "$base$1"; }
org=$(post /organizations '{"name":"Checklist capacity"}' | jq -r '.organization.id')
board=$(post /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Capacity Board",visibility:"PRIVATE"}')" | jq -r '.id')
list=$(post "/boards/$board/lists" '{"name":"Selected List"}' | jq -r '.id')
card=$(post "/lists/$list/cards" '{"title":"Selected Card"}' | jq -r '.id')
checklist=$(post "/cards/$card/checklists" '{"title":"Capacity preparations","cardVersion":1}' | jq -r '.checklist.id')
for id in "$owner" "$org" "$board" "$list" "$card" "$checklist"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
# Direct inserts are disposable scale fixtures, not evidence of audited commands.
# Every tested read/write below goes through the tenant-scoped release API/Nginx.
admin "BEGIN;
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board','Capacity List '||n,lpad((n*1000)::text,30,'0'),now(),now() FROM generate_series(1,199) n;
 WITH targets AS (SELECT id,row_number() OVER(ORDER BY id) AS slot FROM board_lists WHERE tenant_id='$org' AND board_id='$board')
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board',t.id,'Active capacity Card '||n,lpad((n*1000)::text,30,'0'),'ACTIVE',now(),now()
 FROM generate_series(1,4999) n JOIN targets t ON t.slot=(n%200)+1;
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board','$list','Archived capacity Card '||n,lpad((900000000+n*1000)::text,30,'0'),'ARCHIVED',now(),now(),now()
 FROM generate_series(1,100000) n;
 INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,completed,completed_at,completed_by,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$checklist','Capacity preparation '||n,lpad((n*1000)::text,30,'0'),n%2=0,
 CASE WHEN n%2=0 THEN now() END,CASE WHEN n%2=0 THEN '$owner'::uuid END,now(),now() FROM generate_series(1,63) n;
 UPDATE checklists SET version=64,updated_at=now() WHERE tenant_id='$org' AND id='$checklist';
 UPDATE cards SET version=65,updated_at=now() WHERE tenant_id='$org' AND id='$card';
 COMMIT; ANALYZE board_lists; ANALYZE cards; ANALYZE checklist_items;" >/dev/null
counts=$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")
test "$counts" = '200/5000/100000'
state() { admin "SELECT md5(jsonb_build_object(
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'checklist',(SELECT to_jsonb(c) FROM checklists c WHERE tenant_id='$org' AND id='$checklist'),
 'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY id) FROM checklist_items i WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
read_page() { curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -o "$scratch/$2.json" -w '%{time_total}' "$base$1"; }
before=$(state)
board_seconds=$(read_page "/boards/$board" board)
jq -e --arg org "$org" --arg board "$board" ' .board.organizationId==$org and .board.id==$board and (.lists|length)==200
 and ([.lists[].cards[]]|length)==5000 and ([.lists[].list.id]|unique|length)==200 and ([.lists[].cards[].id]|unique|length)==5000' "$scratch/board.json" >/dev/null
archive_seconds=$(read_page "/boards/$board/archived-cards" archived-first)
jq -e --arg org "$org" --arg board "$board" '.organizationId==$org and .boardId==$board and (.items|length)==50 and .nextCursor==.items[-1].card.id
 and all(.items[];.card.organizationId==$org and .card.boardId==$board and .card.lifecycleState=="archived")' "$scratch/archived-first.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/archived-first.json")
read_page "/boards/$board/archived-cards?after=$cursor" archived-second >/dev/null
jq -se '[.[].items[].card.id]|length==100 and (unique|length)==100' "$scratch/archived-first.json" "$scratch/archived-second.json" >/dev/null
last_before=$(admin "SELECT id FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ARCHIVED' ORDER BY id DESC OFFSET 1 LIMIT 1;")
archive_last_seconds=$(read_page "/boards/$board/archived-cards?after=$last_before" archived-last)
jq -e --arg org "$org" --arg board "$board" --arg before "$last_before" '(.items|length)==1 and .nextCursor==null
 and .items[0].card.id>$before and .items[0].card.organizationId==$org and .items[0].card.boardId==$board' "$scratch/archived-last.json" >/dev/null
path="/cards/$card/checklists/$checklist/items"
items_seconds=$(read_page "$path" items-first)
jq -e '.cardVersion==65 and (.items|length)==50 and .nextCursor!=null and .summary.total==63 and .summary.completed==31' "$scratch/items-first.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/items-first.json")
read_page "$path?after=$cursor" items-last >/dev/null
jq -e '(.items|length)==13 and .nextCursor==null and .summary.total==63 and .summary.completed==31' "$scratch/items-last.json" >/dev/null
jq -se '[.[].items[].id]|length==63 and (unique|length)==63' "$scratch/items-first.json" "$scratch/items-last.json" >/dev/null
test "$before" = "$(state)"
item=$(jq -r '.items[0].id' "$scratch/items-first.json")
curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -X PATCH -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" \
 -d '{"text":"Capacity preparation 1","completed":true,"cardVersion":65,"checklistVersion":64,"version":1}' "$base$path/$item" > "$scratch/changed.json"
jq -e --arg actor "$owner" '.changed==true and .cardVersion==66 and .checklist.version==65 and .item.version==2 and .item.completed==true and .item.completedBy==$actor' "$scratch/changed.json" >/dev/null
read_page "$path" after-change >/dev/null
jq -e '.cardVersion==66 and .summary.total==63 and .summary.completed==32' "$scratch/after-change.json" >/dev/null
# Only fixed sizes, measured timings and immutable revision leave the fixture.
revision=${GITHUB_SHA:-}
[[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p artifacts/capacity
jq -nc --arg revision "$revision" --argjson board "$board_seconds" --argjson archive "$archive_seconds" --argjson last "$archive_last_seconds" --argjson items "$items_seconds" \
 '{schemaVersion:1,revision:$revision,topology:"exact release images through Nginx",status:"passed",fixture:{lists:200,activeCards:5000,archivedCards:100000,items:63,pageSize:50},
 verified:{archivePages:[50,50,1],itemPages:[50,13],fullProgressBefore:31,fullProgressAfter:32,readStateUnchanged:true,versionedCompletion:true},
 milliseconds:{board:($board*1000),archiveFirst:($archive*1000),archiveLast:($last*1000),itemsFirst:($items*1000)}}' > "$scratch/capacity.json"
mv "$scratch/capacity.json" artifacts/capacity/checklists.json
bash scripts/ci/test-activity-capacity.sh "$org" "$board" "$card" "$owner" "$scratch/cookies"
bash scripts/ci/test-comment-capacity.sh "$org" "$board" "$card" "$owner" "$scratch/cookies"
bash scripts/ci/test-card-move-capacity.sh "$org" "$board" "$card" "$owner" "$scratch/cookies"
bash scripts/ci/test-card-copy-capacity.sh "$org" "$board" "$card" "$owner" "$scratch/cookies"
echo 'Exact-image Checklist capacity: 200 Lists, 5,000 active Cards, 100,000 archived Cards, bounded first/seek/final pages, full progress, unchanged read state and versioned completion passed.'
