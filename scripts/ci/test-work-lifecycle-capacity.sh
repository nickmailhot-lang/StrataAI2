#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable lifecycle capacity fixture requires CI.' >&2; exit 1; }
org=$1; board=$2; card=$3; owner=$4; cookies=$5
for id in "$org" "$board" "$card" "$owner"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
base=${STRATAAI_TEST_BASE_URL:-http://localhost:8088}
topology=${STRATAAI_CAPACITY_TOPOLOGY:-exact-release-images}
case "$topology" in exact-release-images|local-compiled-runtime) ;; *) exit 1;; esac
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p artifacts/capacity; rm -f artifacts/capacity/work-lifecycle.json
scratch=$(mktemp -d); trap 'rm -rf "$scratch"' EXIT
trap 'echo "Lifecycle capacity fixture failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
uuid() { cat /proc/sys/kernel/random/uuid; }
sizes() { admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');"; }
test "$(sizes)" = '200/5000/100000'
list=$(admin "SELECT list_id FROM cards WHERE tenant_id='$org' AND board_id='$board' AND id='$card' AND lifecycle_state='ACTIVE';")
[[ "$list" =~ ^[0-9a-fA-F-]{36}$ ]]
neighbors() { admin "SELECT md5(jsonb_build_object(
 'cards',(SELECT md5(string_agg(md5(to_jsonb(c)::text),'' ORDER BY id)) FROM cards c WHERE tenant_id='$org' AND id<>'$card'),
 'lists',(SELECT md5(string_agg(md5(to_jsonb(l)::text),'' ORDER BY id)) FROM board_lists l WHERE tenant_id='$org' AND id<>'$list'))::text);"; }
effects() { admin "SELECT jsonb_build_object(
 'board',(SELECT md5(to_jsonb(b)::text) FROM boards b WHERE tenant_id='$org' AND id='$board'),
 'list',(SELECT md5(to_jsonb(l)::text) FROM board_lists l WHERE tenant_id='$org' AND id='$list'),
 'card',(SELECT md5(to_jsonb(c)::text) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org' AND correlation_id='work-lifecycle-capacity'),
 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND correlation_id='work-lifecycle-capacity'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND correlation_id='work-lifecycle-capacity' AND job_type='WORK_EVENT_READY'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'));"; }
neighbors > "$scratch/neighbors-before"
effects > "$scratch/before.json"
: > "$scratch/series.jsonl"
for kind in card list board; do
 case "$kind" in card) table=cards; entity=$card;; list) table=board_lists; entity=$list;; board) table=boards; entity=$board;; esac
 version=$(admin "SELECT version FROM $table WHERE tenant_id='$org' AND id='$entity' AND lifecycle_state='ACTIVE';")
 [[ "$version" =~ ^[1-9][0-9]*$ ]]
 : > "$scratch/archive-seconds"; : > "$scratch/restore-seconds"
 for ((sample=0;sample<20;sample++)); do
  for action in archive restore; do
   key=$(uuid)
   jq -nc --argjson version "$version" '{version:$version}' > "$scratch/body.json"
   elapsed=$(curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H 'X-Correlation-ID: work-lifecycle-capacity' -H "Idempotency-Key: $key" --data-binary "@$scratch/body.json" \
    -o "$scratch/ack.json" -w '%{time_total}' "$base/${kind}s/$entity/$action")
   next=$((version+1)); state=archived; if test "$action" = restore; then state=active; fi
   jq -e --arg id "$entity" --arg org "$org" --arg state "$state" --argjson version "$next" \
    '.id==$id and .organizationId==$org and .version==$version and .lifecycleState==$state
     and (.archivedAt|type)=="string" and (has("deletedAt")|not) and (has("deletedBy")|not)' "$scratch/ack.json" >/dev/null
   if test "$action" = archive; then
    archived_at=$(jq -r '.archivedAt' "$scratch/ack.json")
    jq -e '.archivedAt==.updatedAt' "$scratch/ack.json" >/dev/null
    if test "$sample" = 0; then
     original_key=$key; cp "$scratch/body.json" "$scratch/original-body.json"; cp "$scratch/ack.json" "$scratch/original-ack.json"
    fi
   else
    jq -e --arg at "$archived_at" '.archivedAt==$at' "$scratch/ack.json" >/dev/null
   fi
   # Canonical persistence is checked separately from the response and timing.
   test "$(admin "SELECT lifecycle_state=upper('$state') AND version=$next AND archived_at='$archived_at'::timestamptz
     AND deleted_at IS NULL AND deleted_by IS NULL FROM $table WHERE tenant_id='$org' AND id='$entity';")" = t
   printf '%s\n' "$elapsed" >> "$scratch/$action-seconds"
   version=$next
  done
 done
 effects > "$scratch/pre-replay.json"
 curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -H 'X-Correlation-ID: work-lifecycle-capacity' -H "Idempotency-Key: $original_key" --data-binary "@$scratch/original-body.json" \
  "$base/${kind}s/$entity/archive" > "$scratch/replay.json"
 jq -se '.[0]==.[1]' "$scratch/original-ack.json" "$scratch/replay.json" >/dev/null
 effects > "$scratch/post-replay.json"; cmp "$scratch/pre-replay.json" "$scratch/post-replay.json"
 curl --max-time 60 --fail --silent --show-error -b "$cookies" "$base/boards/$board" > "$scratch/active.json"
 jq -e --arg list "$list" --arg card "$card" '.board.lifecycleState=="active"
  and any(.lists[];.list.id==$list and .list.lifecycleState=="active" and any(.cards[];.id==$card and .lifecycleState=="active"))' "$scratch/active.json" >/dev/null
 for action in archive restore; do
  jq -nc --arg entity "$kind" --arg action "$action" --slurpfile samples "$scratch/$action-seconds" \
   '{entity:$entity,action:$action,samplesMs:($samples|map(.*1000)),p95Ms:($samples|sort|.[18]*1000)}' >> "$scratch/series.jsonl"
 done
done
test "$(sizes)" = '200/5000/100000'
neighbors > "$scratch/neighbors-after"; cmp "$scratch/neighbors-before" "$scratch/neighbors-after"
effects > "$scratch/after.json"
jq -se '.[1].events==.[0].events+120 and .[1].audit==.[0].audit+120 and .[1].jobs==.[0].jobs+120
 and .[1].receipts==.[0].receipts+120' "$scratch/before.json" "$scratch/after.json" >/dev/null
# Failed latency evidence is retained before the mandatory budget gate.
jq -nc --arg revision "$revision" --arg topology "$topology" --slurpfile series "$scratch/series.jsonl" \
 '{schemaVersion:1,revision:$revision,topology:$topology,status:(if all($series[];.p95Ms<500) then "passed" else "failed" end),
 conditions:{clients:1,serial:true,intentionalNetworkLatencyMs:0,clock:"curl-total"},
 fixture:{lists:200,activeCards:5000,archivedCards:100000,samplesPerAction:20,commands:120},
 verified:{canonicalVersions:true,retainedArchiveHistory:true,activeReadRecovery:true,originalRetryUnchanged:true,
  neighborsAndArchivedRecordsUnchanged:true,auditEventJobReceiptDeltasMatchCommandCount:true},
 scope:{archive:true,restore:true,permanentDeletion:false},milliseconds:{series:$series},budget:{mutationP95Ms:500}}' > artifacts/capacity/work-lifecycle.json
jq -e 'all(.milliseconds.series[];.p95Ms<500)' artifacts/capacity/work-lifecycle.json >/dev/null
echo 'Lifecycle capacity: actual Board/List/Card archive and restore commands, retained history, canonical reads, exact retries, untouched neighbors and per-action p95 below 500ms passed.'
