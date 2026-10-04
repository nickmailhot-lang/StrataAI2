#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || exit 1
org=$1; board=$2; card=$3; owner=$4; owner_cookies=$5
for id in "$org" "$board" "$card" "$owner"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$owner_cookies"
base=http://localhost:8088; scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
uuid() { cat /proc/sys/kernel/random/uuid; }
email="notification-capacity-$(uuid)@example.test"
credentials=$(jq -nc --arg email "$email" '{email:$email,password:"notification-capacity-correct-horse",displayName:"Capacity recipient"}')
curl --max-time 60 --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/register" > "$scratch/user.json"
recipient=$(jq -r '.user.id' "$scratch/user.json"); [[ "$recipient" =~ ^[0-9a-fA-F-]{36}$ ]]
curl --max-time 60 --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null
invite=$(curl --max-time 60 --fail --silent --show-error -b "$owner_cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg email "$email" '{email:$email,surface:"INTERNAL",targetRole:"MEMBER"}')" "$base/organizations/$org/invitations" | jq -r '.id')
[[ "$invite" =~ ^[0-9a-fA-F-]{36}$ ]]
curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -X POST -H 'X-StrataAI-Request: 1' "$base/me/invitations/$invite/accept" >/dev/null
curl --max-time 60 --fail --silent --show-error -b "$owner_cookies" -X PATCH -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"role":"MEMBER"}' "$base/boards/$board/members/$recipient" >/dev/null
test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||(SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE')||'/'||(SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")" = '200/5000/100000'
# Disposable synthetic notifications reuse existing synthetic activity sources.
# This measures authorized consumer commands, not producer fan-out or audit generation.
admin "INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
 SELECT tenant_id,gen_random_uuid(),board_id,entity_id,event_id,'$recipient',actor_id,entity_version,created_at,'CARD_UPDATED'
 FROM work_events WHERE tenant_id='$org' AND board_id='$board' AND entity_id='$card' AND correlation_id='activity-capacity';
 ANALYZE card_assignment_notifications;" >/dev/null
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$recipient';")" = 100000
state() { admin "SELECT md5(jsonb_build_object('notifications',(SELECT md5(string_agg(md5(to_jsonb(n)::text),'' ORDER BY id)) FROM card_assignment_notifications n WHERE tenant_id='$org' AND recipient_id='$recipient'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'),
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'))::text);"; }
path="/organizations/$org/notifications"
read_page() {
 local cursor=$1 output=$2; local args=()
 if test -n "$cursor"; then args=(--get --data-urlencode "after=$cursor"); fi
 curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" "${args[@]}" -o "$scratch/$output.json" -w '%{time_total}\n' "$base$path"
}
before=$(state); read_page '' first >/dev/null
jq -e --arg org "$org" --arg recipient "$recipient" --arg actor "$owner" --arg card "$card" --arg board "$board" '.organizationId==$org and (.items|length)==50 and .nextCursor!=null and all(.items[];.recipientId==$recipient and .actorId==$actor and .entityId==$card and .boardId==$board and .type=="CARD_UPDATED" and .readAt==null and .entityLink==("/app/"+$org+"/boards/"+$board+"/cards/"+$card))' "$scratch/first.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/first.json"); read_page "$cursor" seek >/dev/null
jq -se '[.[].items[].id]|length==100 and (unique|length)==100' "$scratch/first.json" "$scratch/seek.json" >/dev/null
admin "SELECT id FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$recipient' ORDER BY created_at DESC,id DESC LIMIT 100;" | sort > "$scratch/expected"
jq -sr '[.[].items[].id]|.[]' "$scratch/first.json" "$scratch/seek.json" | sort > "$scratch/actual"
diff -u "$scratch/expected" "$scratch/actual"
for kind in first seek; do
 : > "$scratch/$kind.seconds"; after=''; if test "$kind" = seek; then after=$cursor; fi
 for ((sample=0;sample<20;sample++)); do
  read_page "$after" sample >> "$scratch/$kind.seconds"
  jq -se '.[0].items==.[1].items and .[0].nextCursor==.[1].nextCursor' "$scratch/$kind.json" "$scratch/sample.json" >/dev/null
 done
done
test "$before" = "$(state)"
: > "$scratch/read.seconds"
for ((sample=0;sample<20;sample++)); do
 id=$(jq -r --argjson index "$sample" '.items[$index].id' "$scratch/first.json"); key=$(uuid); body=$(jq -nc --arg id "$id" '{ids:[$id]}')
 curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "$body" -o "$scratch/read.json" -w '%{time_total}\n' "$base$path/read" >> "$scratch/read.seconds"
 jq -e --arg org "$org" --arg id "$id" '.organizationId==$org and (.items|length)==1 and .items[0].id==$id and (.items[0].readAt|type)=="string"' "$scratch/read.json" >/dev/null
 curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "$body" "$base$path/read" > "$scratch/replay.json"
 jq -se '.[0]==.[1]' "$scratch/read.json" "$scratch/replay.json" >/dev/null
done
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$recipient' AND read_at IS NOT NULL;")" = 20
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]; mkdir -p artifacts/capacity
jq -nc --arg revision "$revision" --slurpfile first "$scratch/first.seconds" --slurpfile seek "$scratch/seek.seconds" --slurpfile read "$scratch/read.seconds" '{schemaVersion:1,revision:$revision,status:"passed",topology:"exact release images through Nginx",
 fixture:{lists:200,activeCards:5000,archivedCards:100000,notifications:100000,pageSize:50,samplesPerOperation:20,clients:1},
 verified:{uniqueSeek:true,persistedOrder:true,readStateUnchanged:true,exactReplay:true,readCount:20},
 milliseconds:{first:($first|map(.*1000)),seek:($seek|map(.*1000)),markRead:($read|map(.*1000)),markReadP95:($read|sort|.[18]*1000)}}' > "$scratch/report.json"
jq -e '.milliseconds.markReadP95<500' "$scratch/report.json" >/dev/null
mv "$scratch/report.json" artifacts/capacity/notifications.json
echo 'Exact notification inbox capacity and original read-command replay passed.'
