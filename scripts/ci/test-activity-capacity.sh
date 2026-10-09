#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable activity capacity fixture requires CI.' >&2; exit 1; }
org=$1; board=$2; card=$3; owner=$4; cookies=$5
for id in "$org" "$board" "$card" "$owner"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
base=${STRATAAI_TEST_BASE_URL:-http://localhost:8088}; scratch=$(mktemp -d)
topology=${STRATAAI_CAPACITY_TOPOLOGY:-exact-release-images}
case "$topology" in exact-release-images|local-compiled-runtime) ;; *) exit 1;; esac
report_directory=${STRATAAI_CAPACITY_ARTIFACTS_DIRECTORY:-artifacts/capacity}
trap 'rm -rf "$scratch"' EXIT
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
counts=$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")
test "$counts" = '200/5000/100000'
# Synthetic scale history is not evidence of 100,000 audited commands.
admin "BEGIN;
 WITH stream AS (UPDATE work_event_streams SET last_sequence=last_sequence+100000,updated_at=clock_timestamp()
  WHERE tenant_id='$org' AND board_id='$board' RETURNING last_sequence)
 INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 SELECT '$org',gen_random_uuid(),'$board',s.last_sequence-100000+n,'$owner','CARD_UPDATED','Card','$card',n,'activity-capacity',
  date_trunc('second',now())+interval '1 minute' FROM stream s CROSS JOIN generate_series(1,100000) n;
 COMMIT; ANALYZE work_events;" >/dev/null
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND correlation_id='activity-capacity';")" = 100000
state() { admin "SELECT md5(jsonb_build_object(
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
read_page() { curl --max-time 60 --fail --silent --show-error -b "$cookies" -o "$scratch/$2.json" -w '%{time_total}' "$base$1"; }
before=$(state)
for kind in boards cards; do
 if test "$kind" = boards; then target=$board; expected=BOARD; else target=$card; expected=CARD; fi
 path="/$kind/$target/activity"
 read_page "$path" "$kind-first" >/dev/null
 jq -e --arg kind "$expected" --arg target "$target" --arg org "$org" --arg board "$board" --arg card "$card" \
  '.organizationId==$org and .kind==$kind and .targetId==$target and (.items|length)==50 and .nextCursor!=null and
   all(.items[];.boardId==$board and .currentBoardId==$board and .entityType=="Card" and .entityId==$card and .metadata=={} and (.version|type)=="string")' "$scratch/$kind-first.json" >/dev/null
 cursor=$(jq -r '.nextCursor' "$scratch/$kind-first.json")
 read_page "$path?after=$cursor" "$kind-second" >/dev/null
 jq -se '[.[].items[].eventId]|length==100 and (unique|length)==100' "$scratch/$kind-first.json" "$scratch/$kind-second.json" >/dev/null
 : > "$scratch/$kind-seconds"
 for ((sample=0;sample<20;sample++)); do
  elapsed=$(read_page "$path" "$kind-sample")
  jq -e '(.items|length)==50 and .nextCursor!=null and all(.items[];.metadata=={})' "$scratch/$kind-sample.json" >/dev/null
  printf '%s\n' "$elapsed" >> "$scratch/$kind-seconds"
 done
done
test "$before" = "$(state)"
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p "$report_directory"
# Only fixed fixture sizes/timings and the immutable build identity are retained.
jq -nc --arg revision "$revision" --arg topology "$topology" --slurpfile boardSamples "$scratch/boards-seconds" --slurpfile cardSamples "$scratch/cards-seconds" \
 '{schemaVersion:1,revision:(if $topology=="exact-release-images" then $revision else null end),
 topology:(if $topology=="exact-release-images" then "exact release images through Nginx" else $topology end),status:"passed",
 fixture:{lists:200,activeCards:5000,archivedCards:100000,seededActivityEvents:100000,pageSize:50,samplesPerEndpoint:20},
 verified:{boardPages:[50,50],cardPages:[50,50],uniqueSeek:true,bodyFree:true,readStateUnchanged:true},
 milliseconds:{boardSamples:($boardSamples|map(.*1000)),cardSamples:($cardSamples|map(.*1000)),
 boardP95:($boardSamples|sort|.[18]*1000),cardP95:($cardSamples|sort|.[18]*1000)}}
 | if $topology=="local-compiled-runtime" then .+{sourceRevision:$revision} else . end' > "$scratch/capacity.json"
mv "$scratch/capacity.json" "$report_directory/activity.json"
echo 'Exact activity capacity: 100,000 sources with 200 Lists, 5,000 active/100,000 archived Cards; bounded unique Board/Card seeks, body-free reads and 20 samples per endpoint passed. Timings do not claim a browser rendering budget.'
