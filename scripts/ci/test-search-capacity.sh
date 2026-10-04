#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || exit 1
umask 077
org=$1; board=$2; cookies=$3
for id in "$org" "$board"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
scratch=$(mktemp -d); base=http://localhost:8088
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Search capacity failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")" = '200/5000/100000'
effects() { admin "SELECT (SELECT sum(version) FROM cards WHERE tenant_id='$org')||'/'||
 (SELECT count(*) FROM audit_events WHERE tenant_id='$org')||'/'||(SELECT count(*) FROM work_events WHERE tenant_id='$org')||'/'||
 (SELECT count(*) FROM work_command_replays WHERE tenant_id='$org');"; }
before=$(effects)
measure() {
  local scope=$1 cursor=$2 output=$3
  local args=(--get --data-urlencode "q=$scope capacity card" --data-urlencode "scope=$scope")
  if test -n "$cursor"; then args+=(--data-urlencode "after=$cursor"); fi
  curl --max-time 60 --fail --silent --show-error -b "$cookies" "${args[@]}" -o "$scratch/$output.json" -w '%{time_total}\n' "$base/search"
}
for scope in active archived; do
  : > "$scratch/$scope-first.seconds"; : > "$scratch/$scope-seek.seconds"
  measure "$scope" '' "$scope-first" >/dev/null
  jq -e --arg org "$org" --arg board "$board" --arg scope "$scope" '(.items|length)==50 and (.nextCursor|type)=="string" and all(.items[]; .sourceKind=="CARD" and .card.organizationId==$org and .card.boardId==$board and .card.lifecycleState==$scope and .boardName=="Capacity Board")' "$scratch/$scope-first.json" >/dev/null
  cursor=$(jq -r '.nextCursor' "$scratch/$scope-first.json")
  measure "$scope" "$cursor" "$scope-seek" >/dev/null
  jq -e --arg org "$org" --arg board "$board" --arg scope "$scope" '(.items|length)==50 and (.nextCursor|type)=="string" and all(.items[]; .card.organizationId==$org and .card.boardId==$board and .card.lifecycleState==$scope)' "$scratch/$scope-seek.json" >/dev/null
  jq -sr '[.[].items[].card.id]|.[]' "$scratch/$scope-first.json" "$scratch/$scope-seek.json" | sort > "$scratch/$scope.actual"
  admin "SELECT id FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state=upper('$scope')
    AND strpos(lower(title),'$scope capacity card')>0 ORDER BY id LIMIT 100;" | sort > "$scratch/$scope.expected"
  diff -u "$scratch/$scope.expected" "$scratch/$scope.actual"
  test "$(sort -u "$scratch/$scope.actual" | wc -l)" -eq 100
  # Twenty serial warm reads of each bounded page, with no intentional delay.
  for ((sample=0;sample<20;sample++)); do
    measure "$scope" '' "$scope-sample" >> "$scratch/$scope-first.seconds"
    # Protected cursor ciphertext is intentionally randomized per response.
    jq -e --slurpfile expected "$scratch/$scope-first.json" '.items==$expected[0].items and (.nextCursor|type)=="string"' "$scratch/$scope-sample.json" >/dev/null
    measure "$scope" "$cursor" "$scope-sample" >> "$scratch/$scope-seek.seconds"
    jq -e --slurpfile expected "$scratch/$scope-seek.json" '[.items[].card.id]==[$expected[0].items[].card.id]' "$scratch/$scope-sample.json" >/dev/null
  done
done
test "$before" = "$(effects)"
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p artifacts/capacity
jq -nc --arg revision "$revision" --slurpfile activeFirst "$scratch/active-first.seconds" --slurpfile activeSeek "$scratch/active-seek.seconds" \
 --slurpfile archivedFirst "$scratch/archived-first.seconds" --slurpfile archivedSeek "$scratch/archived-seek.seconds" \
 '{schemaVersion:1,revision:$revision,topology:"exact release images through Nginx",conditions:{clients:1,serial:true,cache:"warm",intentionalNetworkLatencyMs:0},
 fixture:{lists:200,activeCards:5000,archivedCards:100000,pageSize:50,samplesPerPage:20},
 verified:{activePages:[50,50],archivedPages:[50,50],persistedFirstHundredIds:true,uniquePageIds:true,readStateUnchanged:true},
 milliseconds:{activeFirstSamples:($activeFirst|map(.*1000)),activeSeekSamples:($activeSeek|map(.*1000)),archivedFirstSamples:($archivedFirst|map(.*1000)),archivedSeekSamples:($archivedSeek|map(.*1000)),
 activeFirstP95:($activeFirst|sort|.[18]*1000),activeSeekP95:($activeSeek|sort|.[18]*1000),archivedFirstP95:($archivedFirst|sort|.[18]*1000),archivedSeekP95:($archivedSeek|sort|.[18]*1000)},
 budget:{searchPageP95Ms:500,kind:"engineering read budget"}}
 | .status=(if ([.milliseconds.activeFirstP95,.milliseconds.activeSeekP95,.milliseconds.archivedFirstP95,.milliseconds.archivedSeekP95]|max)<500 then "passed" else "failed" end)' > artifacts/capacity/search.json
jq -e '.status=="passed"' artifacts/capacity/search.json >/dev/null
echo 'Exact-image search capacity: bounded active/archive seek pages, persisted IDs, unchanged read state and serial warm page budgets passed.'
