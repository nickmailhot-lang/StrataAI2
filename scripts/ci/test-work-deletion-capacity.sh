#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable deletion capacity fixture requires CI.' >&2; exit 1; }
org=$1; source=$2; card=$3; owner=$4; cookies=$5
for id in "$org" "$source" "$card" "$owner"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
base=${STRATAAI_TEST_BASE_URL:-http://localhost:8088}
topology=${STRATAAI_CAPACITY_TOPOLOGY:-exact-release-images}
case "$topology" in exact-release-images|local-compiled-runtime) ;; *) exit 1;; esac
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p artifacts/capacity; rm -f artifacts/capacity/work-deletion.json
scratch=$(mktemp -d); trap 'rm -rf "$scratch"' EXIT
trap 'echo "Deletion capacity fixture failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
uuid() { cat /proc/sys/kernel/random/uuid; }
test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$source')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$source' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")" = '200/5000/100000'
source_list=$(admin "SELECT list_id FROM cards WHERE tenant_id='$org' AND id='$card';")
[[ "$source_list" =~ ^[0-9a-fA-F-]{36}$ ]]
# Independent large Boards allow 20 real irreversible commands, never 20 retries.
# Synthetic scale setup is not evidence of audited creation/archive commands.
admin "BEGIN;
 CREATE TEMP TABLE deletion_boards AS SELECT n,gen_random_uuid() id FROM generate_series(1,20) n;
 INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
 SELECT id,'$org','Deletion capacity Board',now(),now() FROM deletion_boards;
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 SELECT gen_random_uuid(),'$org',id,'$owner','ADMIN','ACTIVE',now(),now() FROM deletion_boards;
 CREATE TEMP TABLE deletion_lists AS SELECT b.id board_id,g.n,gen_random_uuid() id FROM deletion_boards b CROSS JOIN generate_series(1,200) g(n);
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,archived_at,created_at,updated_at)
 SELECT id,'$org',board_id,'Deletion capacity List',lpad((n*1000)::text,30,'0'),
 CASE WHEN n=1 THEN 'ARCHIVED' ELSE 'ACTIVE' END,CASE WHEN n=1 THEN now() ELSE NULL END,now(),now() FROM deletion_lists;
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
 SELECT gen_random_uuid(),'$org',l.board_id,l.id,'Deletion capacity child',lpad((g.n*1000)::text,30,'0'),now(),now()
 FROM generate_series(1,5000) g(n) JOIN deletion_lists l ON l.n=(g.n%200)+1;
 CREATE TEMP TABLE deletion_cards AS SELECT b.id board_id,gen_random_uuid() id FROM deletion_boards b;
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,created_at,updated_at)
 SELECT c.id,'$org',c.board_id,l.id,'Deletion capacity Card',lpad('900000000',30,'0'),'ARCHIVED',now(),now(),now()
 FROM deletion_cards c JOIN deletion_lists l ON l.board_id=c.board_id AND l.n=2;
 SELECT b.id||'|'||l.id||'|'||c.id FROM deletion_boards b JOIN deletion_lists l ON l.board_id=b.id AND l.n=1
 JOIN deletion_cards c ON c.board_id=b.id ORDER BY b.n;
 COMMIT; ANALYZE boards; ANALYZE board_lists; ANALYZE cards;" > "$scratch/targets"
test "$(wc -l < "$scratch/targets")" = 20
effects() { admin "SELECT jsonb_build_object(
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org' AND correlation_id='work-deletion-capacity'),
 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND correlation_id='work-deletion-capacity'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND correlation_id='work-deletion-capacity' AND job_type='WORK_EVENT_READY'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'));"; }
children() { admin "SELECT md5(string_agg(md5(to_jsonb(c)::text),'' ORDER BY id)) FROM cards c
 WHERE tenant_id='$org' AND board_id='$1' AND ('$2'='' OR list_id=NULLIF('$2','')::uuid);"; }
effects > "$scratch/before.json"; : > "$scratch/series.jsonl"
for kind in card list board; do
 : > "$scratch/seconds"; sample=0
 while IFS='|' read -r board list target_card; do
  case "$kind" in
   card) table=cards; entity=$target_card; parent=$board; scope_list=''; extra='';;
   list) table=board_lists; entity=$list; parent=$board; scope_list=$list
    # Include the worst supported archived population in one List deletion.
    if test "$sample" = 19; then entity=$source_list; parent=$source; scope_list=$source_list
     curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
      -H "Idempotency-Key: $(uuid)" -d "$(jq -nc --argjson version "$(admin "SELECT version FROM board_lists WHERE id='$entity' AND tenant_id='$org';")" '{version:$version}')" \
      "$base/lists/$entity/archive" >/dev/null
    fi
    impact=$(admin "SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$parent' AND list_id='$entity' AND lifecycle_state<>'DELETED';")
    [[ "$impact" =~ ^[0-9]+$ ]]; extra="&containedCardCount=$impact";;
   board) table=boards; entity=$board; parent=$board; scope_list=''; extra=''
    test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||
     (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE');")" = '200/5000'
    curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
     -H "Idempotency-Key: $(uuid)" -d '{"version":1}' "$base/boards/$board/archive" >/dev/null;;
  esac
  version=$(admin "SELECT version FROM $table WHERE tenant_id='$org' AND id='$entity' AND lifecycle_state='ARCHIVED';")
  [[ "$version" =~ ^[1-9][0-9]*$ ]]
  at=$(admin "SELECT archived_at FROM $table WHERE tenant_id='$org' AND id='$entity';")
  if test "$kind" != card; then children "$parent" "$scope_list" > "$scratch/children-before"; fi
  path="/${kind}s/$entity?version=$version&confirmed=true$extra"; key=$(uuid)
  if test "$sample" = 0; then
   effects > "$scratch/pre-denied.json"
   before_denied=$(admin "SELECT md5(to_jsonb(r)::text) FROM $table r WHERE tenant_id='$org' AND id='$entity';")
   status=$(curl --max-time 60 --silent --show-error -b "$cookies" -X DELETE -H 'X-StrataAI-Request: 1' \
    -H "Idempotency-Key: $(uuid)" -o "$scratch/denied.json" -w '%{http_code}' "$base/${kind}s/$entity?version=$version&confirmed=false$extra")
   test "$status" = 400; jq -e '.code=="delete_confirmation_required"' "$scratch/denied.json" >/dev/null
   test "$before_denied" = "$(admin "SELECT md5(to_jsonb(r)::text) FROM $table r WHERE tenant_id='$org' AND id='$entity';")"
   effects > "$scratch/post-denied.json"; cmp "$scratch/pre-denied.json" "$scratch/post-denied.json"
  fi
  elapsed=$(curl --max-time 60 --fail --silent --show-error -b "$cookies" -X DELETE -H 'X-StrataAI-Request: 1' \
   -H 'X-Correlation-ID: work-deletion-capacity' -H "Idempotency-Key: $key" -o "$scratch/ack.json" -w '%{time_total}' "$base$path")
  jq -e --arg id "$entity" --arg org "$org" --arg actor "$owner" --argjson version "$((version+1))" \
   '.id==$id and .organizationId==$org and .version==$version and .lifecycleState=="deleted"
    and .deletedBy==$actor and (.deletedAt|type)=="string" and (.archivedAt|type)=="string"' "$scratch/ack.json" >/dev/null
  test "$(admin "SELECT lifecycle_state='DELETED' AND version=$((version+1)) AND archived_at='$at'::timestamptz
   AND deleted_by='$owner' AND deleted_at IS NOT NULL FROM $table WHERE tenant_id='$org' AND id='$entity';")" = t
  if test "$kind" != card; then children "$parent" "$scope_list" > "$scratch/children-after"; cmp "$scratch/children-before" "$scratch/children-after"; fi
  printf '%s\n' "$elapsed" >> "$scratch/seconds"
  if test "$kind" = list && test "$sample" = 19; then largest_list_seconds=$elapsed; fi
  # Recover each irreversible acknowledgment and prove no second effects.
  effects > "$scratch/pre-replay.json"
  curl --max-time 60 --fail --silent --show-error -b "$cookies" -X DELETE -H 'X-StrataAI-Request: 1' \
   -H 'X-Correlation-ID: work-deletion-capacity' -H "Idempotency-Key: $key" "$base$path" > "$scratch/replay.json"
  jq -se '.[0]==.[1]' "$scratch/ack.json" "$scratch/replay.json" >/dev/null
  effects > "$scratch/post-replay.json"; cmp "$scratch/pre-replay.json" "$scratch/post-replay.json"
  case "$kind" in
   card) test "$(curl --max-time 60 --silent --show-error -b "$cookies" -o "$scratch/hidden.json" -w '%{http_code}' \
    "$base/boards/$parent/cards/$entity/archived-details")" = 404;;
   list) curl --max-time 60 --fail --silent --show-error -b "$cookies" "$base/boards/$parent" > "$scratch/hidden.json"
    jq -e --arg id "$entity" 'all(.lists[];.list.id!=$id)' "$scratch/hidden.json" >/dev/null;;
   board) test "$(curl --max-time 60 --silent --show-error -b "$cookies" -o "$scratch/hidden.json" -w '%{http_code}' "$base/boards/$entity")" = 404;;
  esac
  sample=$((sample+1))
 done < "$scratch/targets"
 test "$sample" = 20
 jq -nc --arg entity "$kind" --slurpfile samples "$scratch/seconds" \
  '{entity:$entity,action:"delete",samplesMs:($samples|map(.*1000)),p95Ms:($samples|sort|.[18]*1000),maximumMs:($samples|max*1000)}' >> "$scratch/series.jsonl"
done
effects > "$scratch/after.json"
# Receipts also include 20 preparatory Board archives and one List archive.
jq -se '.[1].events==.[0].events+60 and .[1].audit==.[0].audit+60 and .[1].jobs==.[0].jobs+60
 and .[1].receipts==.[0].receipts+81' "$scratch/before.json" "$scratch/after.json" >/dev/null
jq -nc --arg revision "$revision" --arg topology "$topology" --argjson largestImpact "$impact" --argjson largestListSeconds "$largest_list_seconds" --slurpfile series "$scratch/series.jsonl" \
 '{schemaVersion:1,revision:$revision,topology:$topology,status:(if all($series[];.p95Ms<500) then "passed" else "failed" end),
 conditions:{clients:1,serial:true,intentionalNetworkLatencyMs:0,clock:"curl-total"},
 fixture:{independentBoards:20,listsPerBoard:200,activeCardsPerBoard:5000,archivedCardsInOrganizationBefore:100000,
  largestReviewedListImpact:$largestImpact,samplesPerEntity:20,deletions:60,preparatoryArchives:21},
 verified:{confirmationRefusalUnchanged:true,retainedArchiveHistory:true,deletionAttribution:true,childRecordsRetained:true,originalRetryUnchanged:true,deletedContentHidden:true,
  auditEventJobReceiptDeltasMatchCommandCount:true},milliseconds:{series:$series,largestListDeletionMs:($largestListSeconds*1000)},budget:{mutationP95Ms:500}}' > artifacts/capacity/work-deletion.json
jq -e 'all(.milliseconds.series[];.p95Ms<500)' artifacts/capacity/work-deletion.json >/dev/null
echo 'Permanent-deletion capacity: 60 independent irreversible commands, retained child records/history, actor attribution, exact replay and per-entity p95 below 500ms passed.'
