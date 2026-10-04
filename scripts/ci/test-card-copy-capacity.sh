#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable copy capacity fixture requires CI.' >&2; exit 1; }
org=$1; source=$2; card=$3; owner=$4; cookies=$5
for id in "$org" "$source" "$card" "$owner"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
base=http://localhost:8088; scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Card copy capacity fixture failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
uuid() { cat /proc/sys/kernel/random/uuid; }
post() { curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$2" "$base$1"; }
test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$source')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$source' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")" = '200/5000/100000'
destination=$(post /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Copy capacity destination",visibility:"PRIVATE"}')" | jq -r '.id')
list=$(post "/boards/$destination/lists" '{"name":"Copy capacity List"}' | jq -r '.id')
for id in "$destination" "$list"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
# Trusted scale rows, followed by twenty actual restricted copy commands. The
# destination grows from 4,980 to the supported 5,000; source stays at 5,000.
admin "BEGIN;
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$destination','Copy capacity List '||n,lpad((n*1000)::text,30,'0'),now(),now() FROM generate_series(1,199) n;
 WITH targets AS (SELECT id,row_number() OVER(ORDER BY id) AS slot FROM board_lists WHERE tenant_id='$org' AND board_id='$destination')
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$destination',t.id,'Copy capacity Card '||n,lpad((n*1000)::text,30,'0'),'ACTIVE',now(),now()
 FROM generate_series(1,4980) n JOIN targets t ON t.slot=(n%200)+1;
 COMMIT; ANALYZE cards; ANALYZE board_lists; ANALYZE checklist_items;" >/dev/null
version=$(admin "SELECT version FROM cards WHERE tenant_id='$org' AND id='$card' AND board_id='$source' AND lifecycle_state='ACTIVE';")
checklists=$(admin "SELECT count(*) FROM checklists WHERE tenant_id='$org' AND card_id='$card' AND deleted_at IS NULL;")
items=$(admin "SELECT count(*) FROM checklist_items i JOIN checklists c ON c.tenant_id=i.tenant_id AND c.id=i.checklist_id WHERE c.tenant_id='$org' AND c.card_id='$card' AND c.deleted_at IS NULL AND i.deleted_at IS NULL;")
[[ "$version" =~ ^[1-9][0-9]*$ ]]; test "$checklists" = 1; test "$items" -gt 50
effects() { admin "SELECT jsonb_build_object(
 'source',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'sourceChecklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE tenant_id='$org' AND card_id='$card'),
 'sourceItems',(SELECT jsonb_agg(to_jsonb(i) ORDER BY i.id) FROM checklist_items i JOIN checklists c ON c.id=i.checklist_id AND c.tenant_id=i.tenant_id WHERE c.tenant_id='$org' AND c.card_id='$card'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'),
 'destinationCards',(SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$destination' AND lifecycle_state='ACTIVE'),
 'labels',(SELECT count(*) FROM board_labels WHERE tenant_id='$org' AND board_id='$destination'),
 'checklists',(SELECT count(*) FROM checklists c JOIN cards p ON p.id=c.card_id AND p.tenant_id=c.tenant_id WHERE c.tenant_id='$org' AND p.board_id='$destination'),
 'items',(SELECT count(*) FROM checklist_items i JOIN checklists c ON c.id=i.checklist_id AND c.tenant_id=i.tenant_id JOIN cards p ON p.id=c.card_id AND p.tenant_id=c.tenant_id WHERE i.tenant_id='$org' AND p.board_id='$destination'));"; }
effects > "$scratch/before.json"; : > "$scratch/seconds"; : > "$scratch/copies"
for ((sample=0;sample<20;sample++)); do
 jq -nc --arg source "$source" --arg list "$list" --argjson version "$version" \
  '{sourceBoardId:$source,destinationListId:$list,title:"Copy capacity work",expectedVersion:$version}' > "$scratch/body-$sample.json"
 key=$(uuid)
 elapsed=$(curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $key" --data-binary "@$scratch/body-$sample.json" -o "$scratch/ack-$sample.json" -w '%{time_total}' "$base/cards/$card/copy")
 jq -e --arg source "$card" --arg board "$destination" --arg list "$list" \
  '.id!=$source and .boardId==$board and .listId==$list and .version==1 and .title=="Copy capacity work" and .dueComplete==false' "$scratch/ack-$sample.json" >/dev/null
 copy=$(jq -r '.id' "$scratch/ack-$sample.json"); [[ "$copy" =~ ^[0-9a-fA-F-]{36}$ ]]
 printf '%s\n' "$copy" >> "$scratch/copies"; printf '%s\n' "$elapsed" >> "$scratch/seconds"
 if test "$sample" = 0; then original_key=$key; fi
done
test "$(sort -u "$scratch/copies" | wc -l)" -eq 20
effects > "$scratch/after.json"
jq -se --argjson checklists "$checklists" --argjson items "$items" '.[1].source==.[0].source and .[1].sourceChecklists==.[0].sourceChecklists and .[1].sourceItems==.[0].sourceItems and .[1].events==.[0].events+20
 and .[1].audit==.[0].audit+20 and .[1].receipts==.[0].receipts+20 and .[1].destinationCards==5000
 and .[1].labels==.[0].labels+20 and .[1].checklists==.[0].checklists+20*$checklists and .[1].items==.[0].items+20*$items' "$scratch/before.json" "$scratch/after.json" >/dev/null
# Every copied child belongs to a fresh parent and starts as incomplete work;
# source comments/personal subscriptions/members/attachments are not inherited.
test "$(admin "SELECT count(*) FROM checklist_items i JOIN checklists c ON c.id=i.checklist_id AND c.tenant_id=i.tenant_id JOIN cards p ON p.id=c.card_id AND p.tenant_id=c.tenant_id
 WHERE p.tenant_id='$org' AND p.board_id='$destination' AND (i.completed OR i.completed_at IS NOT NULL OR i.completed_by IS NOT NULL OR i.version<>1 OR c.version<>1);")" = 0
test "$(admin "SELECT (SELECT count(*) FROM card_comments a JOIN cards c ON c.tenant_id=a.tenant_id AND c.id=a.card_id WHERE c.tenant_id='$org' AND c.board_id='$destination')+
 (SELECT count(*) FROM card_members a WHERE a.tenant_id='$org' AND a.board_id='$destination')+
 (SELECT count(*) FROM card_reminders a JOIN cards c ON c.tenant_id=a.tenant_id AND c.id=a.card_id WHERE c.tenant_id='$org' AND c.board_id='$destination')+
 (SELECT count(*) FROM attachments a JOIN cards c ON c.tenant_id=a.tenant_id AND c.id=a.card_id WHERE c.tenant_id='$org' AND c.board_id='$destination')+
 (SELECT count(*) FROM watch_subscriptions w JOIN cards c ON c.tenant_id=w.tenant_id AND c.id=w.entity_id WHERE w.entity_type='CARD' AND c.tenant_id='$org' AND c.board_id='$destination');")" = 0
curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -H "Idempotency-Key: $original_key" --data-binary "@$scratch/body-0.json" -o "$scratch/replay.json" "$base/cards/$card/copy"
jq -e --slurpfile original "$scratch/ack-0.json" '.==$original[0]' "$scratch/replay.json" >/dev/null
effects > "$scratch/replay-effects.json"
test "$(jq -Sc . "$scratch/after.json")" = "$(jq -Sc . "$scratch/replay-effects.json")"
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p artifacts/capacity
jq -nc --arg revision "$revision" --argjson checklists "$checklists" --argjson items "$items" --slurpfile samples "$scratch/seconds" \
 '{schemaVersion:1,revision:$revision,topology:"exact release images through Nginx",status:(if ($samples|sort|.[18]*1000)<500 then "passed" else "failed" end),
 conditions:{clients:1,serial:true,intentionalNetworkLatencyMs:0},fixture:{listsPerBoard:200,sourceActiveCards:5000,destinationActiveCardsBefore:4980,
 destinationActiveCardsAfter:5000,archivedCardsInOrganization:100000,copies:20,labelsPerCopy:1,checklistsPerCopy:$checklists,itemsPerCopy:$items},
 verified:{independentCardIds:true,sourceUnchanged:true,freshIncompleteChildren:true,noInheritedHistoryOrPersonalState:true,originalRetryUnchanged:true},
 milliseconds:{samples:($samples|map(.*1000)),mutationP95:($samples|sort|.[18]*1000)},budget:{mutationP95Ms:500}}' > "$scratch/capacity.json"
mv "$scratch/capacity.json" artifacts/capacity/card-copies.json
jq -e '.milliseconds.mutationP95 < .budget.mutationP95Ms' artifacts/capacity/card-copies.json >/dev/null
echo 'Exact-image copy capacity: 200 Lists, source 5,000 Cards, destination 4,980 to 5,000 Cards, 100,000 archived Cards, 20 serial copies with fresh children and mutation p95 below 500ms passed.'
