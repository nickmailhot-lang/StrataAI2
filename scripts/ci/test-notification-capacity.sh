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
test "$(admin "SELECT count(*) FROM notification_events WHERE tenant_id='$org' AND recipient_id='$recipient';")" = 100000
test "$(admin "SELECT last_sequence FROM notification_event_streams WHERE tenant_id='$org' AND recipient_id='$recipient';")" = 100000
counter_clocks() { admin "SELECT isfinite(s.created_at) AND isfinite(s.updated_at) AND s.updated_at>=s.created_at
 AND s.created_at=(SELECT created_at FROM notification_events WHERE tenant_id='$org' AND recipient_id='$recipient' AND sequence=1)
 AND s.updated_at=(SELECT max(created_at) FROM notification_events WHERE tenant_id='$org' AND recipient_id='$recipient')
 AND s.last_sequence=(SELECT count(*) FROM notification_events WHERE tenant_id='$org' AND recipient_id='$recipient')
 FROM notification_event_streams s WHERE tenant_id='$org' AND recipient_id='$recipient';"; }
test "$(counter_clocks)" = t
state() { admin "SELECT md5(jsonb_build_object('notifications',(SELECT md5(string_agg(md5(to_jsonb(n)::text),'' ORDER BY id)) FROM card_assignment_notifications n WHERE tenant_id='$org' AND recipient_id='$recipient'),
 'journal',(SELECT md5(string_agg(md5(to_jsonb(e)::text),'' ORDER BY sequence)) FROM notification_events e WHERE tenant_id='$org' AND recipient_id='$recipient'),
 'stream',(SELECT to_jsonb(s) FROM notification_event_streams s WHERE tenant_id='$org' AND recipient_id='$recipient'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'),
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'))::text);"; }
path="/organizations/$org/notifications"
read_page() {
 local cursor=$1 output=$2; local args=()
 if test -n "$cursor"; then args=(--get --data-urlencode "after=$cursor"); fi
 curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" "${args[@]}" -o "$scratch/$output.json" -w '%{time_total}\n' "$base$path"
}
before=$(state); read_page '' first >/dev/null
for after in 0 50; do
 curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" "$base$path/sync?after=$after" > "$scratch/sync-$after.json"
 jq -e --arg org "$org" --arg recipient "$recipient" --arg actor "$owner" --arg board "$board" --argjson after "$after" '
 .organizationId==$org and .recipientId==$recipient and .cursor==(($after+50)|tostring) and .hasMore and (.resetRequired|not) and (.events|length)==50 and
 all(.events[]; .organizationId==$org and .recipientId==$recipient and .actorId==$actor and .boardId==$board and .entityType=="Notification" and
 .eventType=="NOTIFICATION_CREATED" and .version==1 and .metadata=={}) and
 ([.events[].sequence|tonumber]==[range($after+1;$after+51)])' "$scratch/sync-$after.json" >/dev/null
done
jq -se '[.[].events[].eventId]|length==100 and (unique|length)==100' "$scratch/sync-0.json" "$scratch/sync-50.json" >/dev/null
curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" "$base$path/sync?after=100001" > "$scratch/reset.json"
jq -e '.cursor=="100000" and .resetRequired and (.hasMore|not) and (.events|length)==0' "$scratch/reset.json" >/dev/null
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
: > "$scratch/receipts.jsonl"
for ((sample=0;sample<20;sample++)); do
 id=$(jq -r --argjson index "$sample" '.items[$index].id' "$scratch/first.json"); key=$(uuid); body=$(jq -nc --arg id "$id" '{ids:[$id]}')
 curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "$body" -o "$scratch/read.json" -w '%{time_total}\n' "$base$path/read" >> "$scratch/read.seconds"
 jq -e --arg org "$org" --arg id "$id" '.organizationId==$org and (.items|length)==1 and .items[0].id==$id and (.items[0].readAt|type)=="string"' "$scratch/read.json" >/dev/null
 jq -c '.items[0]' "$scratch/read.json" >> "$scratch/receipts.jsonl"
 curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "$body" "$base$path/read" > "$scratch/replay.json"
 jq -se '.[0]==.[1]' "$scratch/read.json" "$scratch/replay.json" >/dev/null
done
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$recipient' AND read_at IS NOT NULL;")" = 20
# Two real clients race distinct command keys against the same ten unread rows.
# Their acknowledgments must retain the same first read time, not the later clock.
jq -c '{ids:[.items[20:30][].id]}' "$scratch/first.json" > "$scratch/concurrent-body.json"
concurrent_read() {
 local name=$1 key; key=$(uuid)
 curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -H "Idempotency-Key: $key" --data-binary @"$scratch/concurrent-body.json" "$base$path/read" > "$scratch/concurrent-$name.json"
}
concurrent_read first & first_pid=$!
concurrent_read second & second_pid=$!
wait "$first_pid"; wait "$second_pid"
jq -se --arg org "$org" '.[0]==.[1] and .[0].organizationId==$org and (.[0].items|length)==10 and
 ([.[0].items[].id]|unique|length)==10 and all(.[0].items[]; (.readAt|type)=="string")' \
 "$scratch/concurrent-first.json" "$scratch/concurrent-second.json" >/dev/null
jq -c '.items[]' "$scratch/concurrent-first.json" >> "$scratch/receipts.jsonl"
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$recipient' AND read_at IS NOT NULL;")" = 30
test "$(admin "SELECT last_sequence FROM notification_event_streams WHERE tenant_id='$org' AND recipient_id='$recipient';")" = 100030
test "$(admin "SELECT count(*) FROM notification_events WHERE tenant_id='$org' AND recipient_id='$recipient' AND event_type='NOTIFICATION_READ';")" = 30
test "$(counter_clocks)" = t
curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" "$base$path/sync?after=100000" > "$scratch/read-events.json"
jq -e --arg org "$org" --arg recipient "$recipient" --slurpfile receipts "$scratch/receipts.jsonl" '
 .organizationId==$org and .recipientId==$recipient and .cursor=="100030" and (.hasMore|not) and (.resetRequired|not) and (.events|length)==30 and
 ([.events[].sequence|tonumber]==[range(100001;100031)]) and
 all(.events[]; . as $event | .organizationId==$org and .recipientId==$recipient and .actorId==$recipient and .entityType=="Notification" and
 .eventType=="NOTIFICATION_READ" and .version==2 and .metadata=={} and
 any($receipts[]; .id==$event.entityId and .readAt==$event.createdAt)) and
 ([.events[].eventId]|unique|length)==30' "$scratch/read-events.json" >/dev/null
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]; mkdir -p artifacts/capacity
jq -nc --arg revision "$revision" --slurpfile first "$scratch/first.seconds" --slurpfile seek "$scratch/seek.seconds" --slurpfile read "$scratch/read.seconds" '{schemaVersion:1,revision:$revision,status:"passed",topology:"exact release images through Nginx",
 fixture:{lists:200,activeCards:5000,archivedCards:100000,notifications:100000,pageSize:50,samplesPerOperation:20,clients:2,latencyClients:1},
 verified:{uniqueSeek:true,persistedOrder:true,readStateUnchanged:true,exactReplay:true,readCount:30,
 journalCreatedCount:100000,journalReadCount:30,boundedJournalSeek:true,journalReset:true,exactPersistedReadEvents:true,
 concurrentReaders:2,overlappingConcurrentSelection:10,concurrentFirstReadRetained:true},
 milliseconds:{first:($first|map(.*1000)),seek:($seek|map(.*1000)),markRead:($read|map(.*1000)),markReadP95:($read|sort|.[18]*1000)}}' > "$scratch/report.json"
jq -e '.milliseconds.markReadP95<500' "$scratch/report.json" >/dev/null
mv "$scratch/report.json" artifacts/capacity/notifications.json
echo 'Exact notification inbox capacity and original read-command replay passed.'
