#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable comment capacity fixture requires CI.' >&2; exit 1; }
org=$1; board=$2; card=$3; owner=$4; cookies=$5
for id in "$org" "$board" "$card" "$owner"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
base=http://localhost:8088; scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")" = '200/5000/100000'
# Synthetic scale rows are not evidence of 100,000 audited comment commands.
# The normal revision guard remains enabled for both insertion and redaction.
admin "BEGIN;
 INSERT INTO card_comments(id,tenant_id,card_id,author_id,content,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$card','$owner','Scale comment',date_trunc('second',now()),date_trunc('second',now())
 FROM generate_series(1,100000);
 UPDATE card_comments SET content=NULL,version=version+1,updated_at=created_at+interval '1 second',
 deleted_at=created_at+interval '1 second',deleted_by=author_id
 WHERE tenant_id='$org' AND card_id='$card' AND id=(SELECT id FROM card_comments
 WHERE tenant_id='$org' AND card_id='$card' ORDER BY created_at,id LIMIT 1);
 COMMIT; ANALYZE card_comments;" >/dev/null
test "$(admin "SELECT count(*) FROM card_comments WHERE tenant_id='$org' AND card_id='$card';")" = 100000
state() { admin "SELECT md5(jsonb_build_object(
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'comments',(SELECT md5(string_agg(md5(to_jsonb(c)::text),'' ORDER BY id)) FROM card_comments c WHERE tenant_id='$org' AND card_id='$card'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
path="/cards/$card/comments"
read_page() {
 local cursor_args=()
 if test -n "${2:-}"; then cursor_args=(--data-urlencode "after=$2"); fi
 curl --max-time 60 --fail --silent --show-error -b "$cookies" -D "$scratch/headers" -o "$scratch/$1.json" -w '%{time_total}' \
  --get "${cursor_args[@]}" "$base$path"
 grep -qi '^Cache-Control:.*no-store' "$scratch/headers"
}
before=$(state)
read_page first >/dev/null
version=$(jq -r '.cardVersion' "$scratch/first.json")
[[ "$version" =~ ^[1-9][0-9]*$ ]]
jq -e --arg org "$org" --arg board "$board" --arg card "$card" --arg author "$owner" \
 '.organizationId==$org and .boardId==$board and .cardId==$card and (.items|length)==50 and .nextCursor!=null and
  all(.items[];.organizationId==$org and .cardId==$card and .authorId==$author and .content=="Scale comment" and .version==1 and .deletedAt==null)' "$scratch/first.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/first.json")
read_page second "$cursor" >/dev/null
jq -se '[.[].items[].id]|length==100 and (unique|length)==100' "$scratch/first.json" "$scratch/second.json" >/dev/null
# Seek after the second-oldest source, using the documented versioned cursor
# coordinates. This tests the final tombstone page without a 2,000-request scan.
last_cursor=$(admin "SELECT '$card/$version/'||((extract(epoch FROM created_at)*10000000)::bigint+621355968000000000)||'/'||id
 FROM card_comments WHERE tenant_id='$org' AND card_id='$card' ORDER BY created_at,id OFFSET 1 LIMIT 1;")
read_page last "$last_cursor" >/dev/null
jq -e '(.items|length)==1 and .nextCursor==null and .items[0].content==null and .items[0].deletedAt!=null and .items[0].version==2' "$scratch/last.json" >/dev/null
: > "$scratch/seconds"
for ((sample=0;sample<20;sample++)); do
 elapsed=$(read_page sample)
 jq -e '(.items|length)==50 and .nextCursor!=null and all(.items[];.content=="Scale comment")' "$scratch/sample.json" >/dev/null
 printf '%s\n' "$elapsed" >> "$scratch/seconds"
done
test "$before" = "$(state)"
effects() { admin "SELECT jsonb_build_object(
 'comments',(SELECT count(*) FROM card_comments WHERE tenant_id='$org' AND card_id='$card'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id='$card' AND event_type='COMMENT_ADDED'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'snapshots',(SELECT count(*) FROM comment_mention_snapshots WHERE tenant_id='$org' AND card_id='$card'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'),
 'version',(SELECT version FROM cards WHERE tenant_id='$org' AND id='$card'));"; }
effects > "$scratch/effects-before.json"
: > "$scratch/mutation-seconds"
write_comment() {
 curl --max-time 60 --fail --silent --show-error -b "$cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $1" --data-binary "@$2" -o "$3" -w '%{time_total}' "$base$path"
}
# Real serial commands exercise the PRD mutation budget, separate from the
# synthetic read fixture and its already-proven unchanged-state boundary.
for ((sample=0;sample<20;sample++)); do
 key=$(cat /proc/sys/kernel/random/uuid)
 jq -nc --argjson version "$((version+sample))" '{content:"Measured comment",cardVersion:$version}' > "$scratch/write-$sample.json"
 elapsed=$(write_comment "$key" "$scratch/write-$sample.json" "$scratch/ack-$sample.json")
 jq -e --arg org "$org" --arg board "$board" --arg card "$card" --arg actor "$owner" --argjson version "$((version+sample+1))" \
  '.organizationId==$org and .boardId==$board and .cardId==$card and .changed==true and .cardVersion==$version and
   .comment.organizationId==$org and .comment.cardId==$card and .comment.authorId==$actor and .comment.content=="Measured comment" and .comment.version==1' "$scratch/ack-$sample.json" >/dev/null
 printf '%s\n' "$elapsed" >> "$scratch/mutation-seconds"
 if test "$sample" = 0; then first_key=$key; fi
done
jq -se 'length==20 and ([.[].comment.id]|unique|length)==20' "$scratch"/ack-*.json >/dev/null
effects > "$scratch/effects-after.json"
jq -se --argjson version "$version" '.[1].comments==.[0].comments+20 and .[1].events==.[0].events+20 and
 .[1].audits==.[0].audits+20 and .[1].snapshots==.[0].snapshots+20 and .[1].receipts==.[0].receipts+20 and
 .[0].version==$version and .[1].version==$version+20' "$scratch/effects-before.json" "$scratch/effects-after.json" >/dev/null
after_commands=$(state)
write_comment "$first_key" "$scratch/write-0.json" "$scratch/replay.json" >/dev/null
jq -e --slurpfile original "$scratch/ack-0.json" '.==$original[0]' "$scratch/replay.json" >/dev/null
test "$(curl --max-time 60 --silent --show-error -b "$cookies" --get --data-urlencode "after=$cursor" -o "$scratch/stale.json" -w '%{http_code}' "$base$path")" = 409
jq -e '.code=="version_conflict" and (has("items")|not)' "$scratch/stale.json" >/dev/null
test "$after_commands" = "$(state)"
# Nearest-rank p95 over 20 complete HTTP acknowledgments. The condition is
# one serial authenticated client, with no intentional latency/concurrent work.
jq -se 'length==20 and all(.[];type=="number" and .>=0) and (sort|.[18])<0.5' "$scratch/mutation-seconds" >/dev/null
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p artifacts/capacity
# Bodies, authors, scopes and cursors never enter the retained artifact.
jq -nc --arg revision "$revision" --slurpfile samples "$scratch/seconds" --slurpfile mutation "$scratch/mutation-seconds" \
 '{schemaVersion:1,revision:$revision,topology:"exact release images through Nginx",status:"passed",
 fixture:{lists:200,activeCards:5000,archivedCards:100000,seededComments:100000,pageSize:50,samples:20,realCommentCommands:20},
 condition:"one serial authenticated client; no intentional latency or concurrent commands",
 verified:{pages:[50,50,1],uniqueSeek:true,redactedFinalPage:true,noStore:true,readStateUnchanged:true,
  atomicPublication:true,retryStateUnchanged:true,staleCursorRefused:true,mutationP95Under500ms:true},
 milliseconds:{samples:($samples|map(.*1000)),p95:($samples|sort|.[18]*1000),
  mutationSamples:($mutation|map(.*1000)),mutationP95:($mutation|sort|.[18]*1000)}}' > "$scratch/capacity.json"
mv "$scratch/capacity.json" artifacts/capacity/comments.json
echo 'Exact comment capacity: bounded unique read/redaction pages, no-store, unchanged reads, 20 real atomic commands, exact retry/stale-cursor refusal and mutation p95 below 500 ms passed. This does not claim browser rendering or a concurrent/load-test budget.'
