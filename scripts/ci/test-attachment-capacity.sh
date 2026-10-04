#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || exit 1
umask 077
org=$1; board=$2; card=$3; cookies=$4
for id in "$org" "$board" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
scratch=$(mktemp -d); base=http://localhost:8088
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Attachment capacity failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")" = '200/5000/100000'
version=$(admin "SELECT version FROM cards WHERE tenant_id='$org' AND id='$card' AND board_id='$board' AND lifecycle_state='ACTIVE';")
[[ "$version" =~ ^[1-9][0-9]*$ ]]; original_version=$version
test "$(admin "SELECT count(*) FROM attachments WHERE tenant_id='$org' AND card_id='$card';")" = 0
effects() { admin "SELECT (SELECT count(*) FROM work_events WHERE tenant_id='$org')||'/'||
 (SELECT count(*) FROM audit_events WHERE tenant_id='$org')||'/'||(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org');"; }
before=$(effects); IFS=/ read -r events audits receipts <<< "$before"
measure() {
  local phase=$1 method=$2 path=$3 key=$4 body=$5
  curl --max-time 60 --fail --silent --show-error -b "$cookies" -X "$method" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $key" -d "$body" -o "$scratch/response.json" -w '%{time_total}\n' "$base$path" >> "$scratch/$phase.seconds"
}
path="/cards/$card/attachments"
for phase in create archive delete; do
  : > "$scratch/$phase.seconds"
  for ((sample=0;sample<20;sample++)); do
    key=$(uuid)
    if test "$phase" = create; then
      body=$(jq -nc --argjson version "$version" '{title:"Capacity reference",url:"https://example.test/capacity",cardVersion:$version}')
      measure "$phase" POST "$path/url" "$key" "$body"
      attachment=$(jq -r '.attachment.id' "$scratch/response.json"); [[ "$attachment" =~ ^[0-9a-fA-F-]{36}$ ]]; printf '%s\n' "$attachment" >> "$scratch/ids"
    else
      attachment=$(sed -n "$((sample+1))p" "$scratch/ids")
      if test "$phase" = archive; then
        body=$(jq -nc --argjson version "$version" '{cardVersion:$version,version:1}')
        measure "$phase" POST "$path/$attachment/archive" "$key" "$body"
      else
        body='{}'; command_path="$path/$attachment?cardVersion=$version&version=2&confirmed=true"
        measure "$phase" DELETE "$command_path" "$key" "$body"
        if test "$sample" = 0; then original_key=$key; original_path=$command_path; cp "$scratch/response.json" "$scratch/original.json"; fi
      fi
    fi
    version=$((version+1))
    jq -e --argjson version "$version" --arg attachment "$attachment" '.cardVersion==$version and .attachment.id==$attachment' "$scratch/response.json" >/dev/null
  done
done
test "$(sort -u "$scratch/ids" | wc -l)" -eq 20
test "$(effects)" = "$((events+60))/$((audits+60))/$((receipts+60))"
test "$(admin "SELECT version FROM cards WHERE tenant_id='$org' AND id='$card';")" = "$((original_version+60))"
test "$(admin "SELECT count(*) FROM attachments WHERE tenant_id='$org' AND card_id='$card' AND lifecycle_state='DELETED' AND version=3 AND deleted_by IS NOT NULL;")" = 20
before=$(effects)
curl --max-time 60 --fail --silent --show-error -b "$cookies" -X DELETE -H 'X-StrataAI-Request: 1' -H "Idempotency-Key: $original_key" -o "$scratch/replay.json" "$base$original_path"
cmp "$scratch/original.json" "$scratch/replay.json"; test "$before" = "$(effects)"
test "$(admin "SELECT version FROM cards WHERE tenant_id='$org' AND id='$card';")" = "$version"
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]; mkdir -p artifacts/capacity
jq -nc --arg revision "$revision" --slurpfile create "$scratch/create.seconds" --slurpfile archive "$scratch/archive.seconds" --slurpfile delete "$scratch/delete.seconds" \
 '{schemaVersion:1,revision:$revision,topology:"exact release images through Nginx",conditions:{clients:1,serial:true,intentionalNetworkLatencyMs:0},
 fixture:{lists:200,activeCards:5000,archivedCards:100000,attachments:20},verified:{independentIds:true,sixtyAtomicCommands:true,deletedTombstones:true,originalRetryUnchanged:true},
 milliseconds:{createSamples:($create|map(.*1000)),archiveSamples:($archive|map(.*1000)),deleteSamples:($delete|map(.*1000)),createP95:($create|sort|.[18]*1000),archiveP95:($archive|sort|.[18]*1000),deleteP95:($delete|sort|.[18]*1000)},budget:{mutationP95Ms:500}}
 | .status=(if ([.milliseconds.createP95,.milliseconds.archiveP95,.milliseconds.deleteP95]|max)<500 then "passed" else "failed" end)' > artifacts/capacity/attachments.json
jq -e '.status=="passed"' artifacts/capacity/attachments.json >/dev/null
echo 'Exact-image attachment capacity: 20 URL creations, archives and confirmed deletions at supported Board scale passed.'
