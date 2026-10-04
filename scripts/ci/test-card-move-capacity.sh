#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable movement capacity fixture requires CI.' >&2; exit 1; }
org=$1; source=$2; card=$3; owner=$4; cookies=$5
for id in "$org" "$source" "$card" "$owner"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
base=http://localhost:8088; scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Card movement capacity fixture failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
uuid() { cat /proc/sys/kernel/random/uuid; }
post() { curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$2" "$base$1"; }
test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$source')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$source' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")" = '200/5000/100000'
source_list=$(admin "SELECT list_id FROM cards WHERE tenant_id='$org' AND id='$card' AND board_id='$source';")
[[ "$source_list" =~ ^[0-9a-fA-F-]{36}$ ]]
destination=$(post /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Movement capacity destination",visibility:"PRIVATE"}')" | jq -r '.id')
destination_list=$(post "/boards/$destination/lists" '{"name":"Movement capacity List"}' | jq -r '.id')
for id in "$destination" "$destination_list"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
# Synthetic scale rows establish the supported Board size; the timed commands
# use the restricted API. Each admitted destination finishes with 5,000 Cards.
admin "BEGIN;
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$destination','Movement capacity List '||n,lpad((n*1000)::text,30,'0'),now(),now() FROM generate_series(1,199) n;
 WITH targets AS (SELECT id,row_number() OVER(ORDER BY id) AS slot FROM board_lists WHERE tenant_id='$org' AND board_id='$destination')
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$destination',t.id,'Movement capacity Card '||n,lpad((n*1000)::text,30,'0'),'ACTIVE',now(),now()
 FROM generate_series(1,4999) n JOIN targets t ON t.slot=(n%200)+1;
 COMMIT; ANALYZE cards; ANALYZE board_lists;" >/dev/null
label=$(post "/boards/$source/labels" '{"name":"Capacity movement label","color":"blue"}' | jq -r '.id')
version=$(admin "SELECT version FROM cards WHERE tenant_id='$org' AND id='$card';")
[[ "$version" =~ ^[1-9][0-9]*$ ]]; [[ "$label" =~ ^[0-9a-fA-F-]{36}$ ]]
curl --max-time 60 --fail --silent --show-error -b "$cookies" -X PUT -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -H "Idempotency-Key: $(uuid)" -d '{}' "$base/cards/$card/labels/$label?version=$version" >/dev/null
version=$((version+1))
curl --max-time 60 --fail --silent --show-error -b "$cookies" -X PUT -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -H "Idempotency-Key: $(uuid)" -d '{}' "$base/cards/$card/members/$owner?version=$version" >/dev/null
version=$((version+1))
# Both Boards were created by the owner, who already has their explicit ADMIN
# memberships as well as Organization ownership.
effects() { admin "SELECT jsonb_build_object(
 'moves',(SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id='$card' AND event_type='CARD_MOVED'),
 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'),
 'version',(SELECT version FROM cards WHERE tenant_id='$org' AND id='$card'),
 'body',(SELECT to_jsonb(c)-'board_id'-'list_id'-'rank'-'updated_at'-'version' FROM cards c WHERE tenant_id='$org' AND id='$card'));"; }
effects > "$scratch/before.json"
: > "$scratch/seconds"
for ((sample=0;sample<20;sample++)); do
 if ((sample%2==0)); then from=$source; to=$destination; list=$destination_list; else from=$destination; to=$source; list=$source_list; fi
 jq -nc --arg from "$from" --arg list "$list" --argjson version "$((version+sample))" \
  '{sourceBoardId:$from,destinationListId:$list,expectedVersion:$version}' > "$scratch/body-$sample.json"
 key=$(uuid)
 elapsed=$(curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $key" --data-binary "@$scratch/body-$sample.json" -o "$scratch/ack-$sample.json" -w '%{time_total}' "$base/cards/$card/move")
 jq -e --arg card "$card" --arg board "$to" --arg list "$list" --argjson version "$((version+sample+1))" \
  '.id==$card and .boardId==$board and .listId==$list and .version==$version' "$scratch/ack-$sample.json" >/dev/null
 printf '%s\n' "$elapsed" >> "$scratch/seconds"
 if test "$sample" = 0; then original_key=$key; fi
done
effects > "$scratch/after.json"
jq -se --argjson version "$version" '.[1].moves==.[0].moves+40 and .[1].audit==.[0].audit+20 and .[1].receipts==.[0].receipts+20
 and .[1].version==$version+20 and .[1].body==.[0].body' "$scratch/before.json" "$scratch/after.json" >/dev/null
curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -H "Idempotency-Key: $original_key" --data-binary "@$scratch/body-0.json" -o "$scratch/replay.json" "$base/cards/$card/move"
jq -e --slurpfile original "$scratch/ack-0.json" '.==$original[0]' "$scratch/replay.json" >/dev/null
effects > "$scratch/replay-effects.json"
test "$(jq -Sc . "$scratch/after.json")" = "$(jq -Sc . "$scratch/replay-effects.json")"
test "$(admin "SELECT count(*) FROM card_labels WHERE tenant_id='$org' AND card_id='$card' AND board_id='$source';")" = 1
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND card_id='$card' AND board_id='$source' AND user_id='$owner';")" = 1
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p artifacts/capacity
jq -nc --arg revision "$revision" --slurpfile samples "$scratch/seconds" \
 '{schemaVersion:1,revision:$revision,topology:"exact release images through Nginx",status:(if ($samples|sort|.[18]*1000)<500 then "passed" else "failed" end),
 conditions:{clients:1,serial:true,intentionalNetworkLatencyMs:0},
 fixture:{listsPerBoard:200,sourceActiveCardsBefore:5000,destinationActiveCardsBefore:4999,destinationActiveCardsAfterEachMove:5000,
 archivedCardsInOrganization:100000,moves:20,labelsOnMovedCard:1,assignmentsOnMovedCard:1},
 verified:{stableCardId:true,bothBoardEvents:true,oneAuditPerMove:true,originalRetryUnchanged:true,bodyUnchanged:true,currentReferences:true},
 milliseconds:{samples:($samples|map(.*1000)),mutationP95:($samples|sort|.[18]*1000)},
 budget:{mutationP95Ms:500}}' > "$scratch/capacity.json"
mv "$scratch/capacity.json" artifacts/capacity/card-moves.json
jq -e '.milliseconds.mutationP95 < .budget.mutationP95Ms' artifacts/capacity/card-moves.json >/dev/null
echo 'Exact-image movement capacity: 200 Lists, admitted destination 5,000 Cards, 100,000 archived Cards, 20 serial cross-Board moves, stable references, exact retry and mutation p95 below 500ms passed.'
