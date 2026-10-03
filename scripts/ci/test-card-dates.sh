#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable Card date fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d)
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() { admin 'GRANT INSERT ON card_assignment_notifications, background_jobs TO strataai_api_runtime;' >/dev/null || true; rm -rf "$scratch"; }
trap cleanup EXIT
trap 'echo "Card date check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
for actor in owner member outsider; do
  jq -nc --arg email "dates-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"dates-fixture-battery-horse",displayName:"Dates fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
member=$(jq -r '.user.id' "$scratch/member.user")
org=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Date transactions"}' "$base/organizations" | jq -r '.organization.id')
board=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Private dates Board",visibility:"PRIVATE"}')" "$base/boards" | jq -r '.id')
list=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Dates List"}' "$base/boards/$board/lists" | jq -r '.id')
card=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"title":"Dates Card"}' "$base/lists/$list/cards" | jq -r '.id')
for id in "$member" "$org" "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES(gen_random_uuid(),'$org','$board','$member','MEMBER','ACTIVE',now(),now());" >/dev/null
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -d "$5" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
state() { admin "SELECT md5(jsonb_build_object(
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'reminders',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM card_reminders r WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'notifications',(SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
test "$(request member PUT "/watch/CARD/$card?version=0" "$(uuid)" '{}')" = 200
path="/cards/$card/dates"; key=$(uuid)
payload='{"startAt":"2026-03-08","dueAt":"2026-03-08","dueTimezone":"America/Vancouver","dueHasTime":false,"dueComplete":false,"version":1}'
before=$(state)
test "$(request outsider PATCH "$path" "$(uuid)" "$payload")" = 404
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.dueTimezone="Unknown/Place"' <<< "$payload")")" = 400
test "$before" = "$(state)"
# A failure after the date update and event append must roll back the entire command.
admin 'REVOKE INSERT ON card_assignment_notifications FROM strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "$path" "$key" "$payload")" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON card_assignment_notifications TO strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "$path" "$key" "$payload")" = 200
jq -e '.changed and .card.version==2 and .card.dueTimezone=="America/Vancouver" and .card.dueHasTime==false and .card.dueComplete==false' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/changed.json"
test "$(admin "SELECT start_at='2026-03-08T08:00:00Z' AND due_at='2026-03-09T06:59:59.999999Z' AND NOT due_has_time FROM cards WHERE tenant_id='$org' AND id='$card';")" = t
after=$(state)
test "$(request owner PATCH "$path" "$key" "$payload")" = 200
cmp "$scratch/changed.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(request owner PATCH "$path" "$(uuid)" "$payload")" = 409
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.version=2' <<< "$payload")")" = 200
jq -e '.changed==false and .card.version==2' "$scratch/response.json" >/dev/null
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND notification_type='CARD_DATE_CHANGED' AND recipient_id='$member';")" = 1
complete=$(jq -c '.card | {startAt,dueAt,dueTimezone,dueHasTime,dueComplete:true,version}' "$scratch/changed.json")
test "$(request owner PATCH "$path" "$(uuid)" "$complete")" = 200
jq -e '.changed and .card.version==3 and .card.dueComplete and .card.lifecycleState=="active"' "$scratch/response.json" >/dev/null
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.dueComplete=false | .version=3' <<< "$complete")")" = 200
test "$(request owner PATCH "$path" "$(uuid)" '{"version":4,"dueHasTime":false,"dueComplete":false}')" = 200
jq -e '.card.version==5 and .card.startAt==null and .card.dueAt==null and .card.dueTimezone==null and .card.dueComplete==false' "$scratch/response.json" >/dev/null
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$member';")" = 4
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type IN ('CARD_DATE_CHANGED','CARD_DUE_COMPLETED','CARD_DUE_REOPENED');")" = 4
# Snapshot and mutation readers must carry canonical dates through unrelated revisions.
timed='{"dueAt":"2026-11-02T15:00:00Z","dueTimezone":"America/Vancouver","dueHasTime":true,"dueComplete":true,"version":5}'
test "$(request owner PATCH "$path" "$(uuid)" "$timed")" = 200
test "$(request owner PATCH "/cards/$card" "$(uuid)" '{"title":"Dates retained","version":6}')" = 200
jq -e '.dueAt!=null and .dueHasTime and .dueComplete and .version==7' "$scratch/response.json" >/dev/null
test "$(request owner POST "/cards/$card/archive" "$(uuid)" '{"version":7}')" = 200
jq -e '.dueAt!=null and .dueComplete and .version==8' "$scratch/response.json" >/dev/null
curl --fail --silent --show-error -b "$scratch/owner.cookies" "$base/boards/$board/archived-cards" > "$scratch/archived.json"
jq -e --arg card "$card" '.items | any(.card.id==$card and .card.dueAt!=null and .card.dueComplete)' "$scratch/archived.json" >/dev/null
test "$(request owner PATCH "$path" "$key" "$payload")" = 404
test "$(request owner POST "/cards/$card/restore" "$(uuid)" '{"version":8}')" = 200
# Old receipts must not disclose dates after access removal.
memberKey=$(uuid)
test "$(request member PATCH "$path" "$memberKey" '{"version":9,"dueHasTime":false,"dueComplete":false}')" = 200
admin "UPDATE board_members SET status='REMOVED',version=version+1 WHERE tenant_id='$org' AND board_id='$board' AND user_id='$member';" >/dev/null
before=$(state)
test "$(request member PATCH "$path" "$memberKey" '{"version":9,"dueHasTime":false,"dueComplete":false}')" = 404
test "$before" = "$(state)"
echo 'Card dates, UTC/DST, retry recovery, no-op, completion, clear, archive preservation, rollback and private replay passed.'

# Exercise personal configuration and date rescheduling through the real API.
card=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"title":"Reminder rescheduling"}' "$base/lists/$list/cards" | jq -r '.id')
path="/cards/$card/dates"
due=$(date -u -d '+2 days' '+%Y-%m-%dT%H:%M:%SZ')
payload=$(jq -nc --arg due "$due" '{dueAt:$due,dueTimezone:"UTC",dueHasTime:true,dueComplete:false,version:1}')
test "$(request owner PATCH "$path" "$(uuid)" "$payload")" = 200
admin "UPDATE board_members SET status='ACTIVE',version=version+1 WHERE tenant_id='$org' AND board_id='$board' AND user_id='$member';" >/dev/null
reminderPath="/cards/$card/reminder"; reminderKey=$(uuid)
choice='{"intervalCode":"1_HOUR","enabled":true,"cardVersion":2,"version":0}'
test "$(request outsider PUT "$reminderPath" "$(uuid)" "$choice")" = 404
test "$(request member PUT "$reminderPath" "$reminderKey" "$choice")" = 200
jq -e --arg user "$member" '.changed and .userId==$user and .cardVersion==2 and .reminder.userId==$user and .reminder.generation==1 and .reminder.status=="SCHEDULED" and (.options|length)==4' "$scratch/response.json" >/dev/null
reminder=$(jq -r '.reminder.id' "$scratch/response.json")
cp "$scratch/response.json" "$scratch/reminder-created.json"
test "$(request member PUT "$reminderPath" "$reminderKey" "$choice")" = 200
cmp "$scratch/reminder-created.json" "$scratch/response.json"
curl --fail --silent --show-error -b "$scratch/owner.cookies" "$base$reminderPath?userId=$member" | jq -e '.reminder==null' >/dev/null
due=$(date -u -d '+3 days' '+%Y-%m-%dT%H:%M:%SZ')
payload=$(jq -nc --arg due "$due" '{dueAt:$due,dueTimezone:"UTC",dueHasTime:true,dueComplete:false,version:2}')
key=$(uuid); before=$(state)
admin 'REVOKE INSERT ON background_jobs FROM strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "$path" "$key" "$payload")" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON background_jobs TO strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "$path" "$key" "$payload")" = 200
cp "$scratch/response.json" "$scratch/reminder-change.json"
test "$(admin "SELECT generation=2 AND version=2 AND enabled AND status='SCHEDULED' AND due_at='$due' AND trigger_at=due_at-interval '1 hour' FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND service_identity='card-reminder-delivery' AND safe_metadata=jsonb_build_object('reminderId','$reminder'::uuid,'generation',2) AND available_at='$due'::timestamptz-interval '1 hour';")" = 1
after=$(state)
test "$(request owner PATCH "$path" "$key" "$payload")" = 200
cmp "$scratch/reminder-change.json" "$scratch/response.json"
test "$after" = "$(state)"
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.version=3' <<< "$payload")")" = 200
test "$(admin "SELECT version=3 FROM cards WHERE tenant_id='$org' AND id='$card';")" = t
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND safe_metadata->>'reminderId'='$reminder';")" = 2
test "$(admin "SELECT generation=2 AND version=2 FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.version=3 | .dueComplete=true' <<< "$payload")")" = 200
test "$(admin "SELECT generation=3 AND enabled AND status='SUSPENDED' AND trigger_at IS NULL FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.version=4' <<< "$payload")")" = 200
test "$(admin "SELECT generation=4 AND status='SCHEDULED' FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(request owner PATCH "$path" "$(uuid)" '{"version":5,"dueHasTime":false,"dueComplete":false}')" = 200
test "$(admin "SELECT generation=5 AND enabled AND status='SUSPENDED' AND due_at IS NULL AND trigger_at IS NULL FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND safe_metadata->>'reminderId'='$reminder';")" = 3
cancelKey=$(uuid)
test "$(request member DELETE "$reminderPath?cardVersion=6&version=5" "$cancelKey" '{}')" = 200
jq -e --arg id "$reminder" '.changed and .reminder.id==$id and .reminder.generation==6 and .reminder.version==6 and .reminder.enabled==false and .reminder.status=="CANCELLED"' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/reminder-cancelled.json"
test "$(request member DELETE "$reminderPath?cardVersion=6&version=5" "$cancelKey" '{}')" = 200
cmp "$scratch/reminder-cancelled.json" "$scratch/response.json"
test "$(request member PUT "$reminderPath" "$(uuid)" '{"intervalCode":"1_HOUR","enabled":true,"cardVersion":6,"version":6}')" = 400
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_type='Reminder' AND entity_id='$reminder' AND event_type IN ('REMINDER_SCHEDULED','REMINDER_CANCELLED');")" = 6
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.version=6' <<< "$payload")")" = 200
test "$(request member PUT "$reminderPath" "$(uuid)" '{"intervalCode":"1_HOUR","enabled":true,"cardVersion":7,"version":6}')" = 200
archiveKey=$(uuid)
test "$(request owner POST "/cards/$card/archive" "$archiveKey" '{"version":7}')" = 200
test "$(admin "SELECT generation=8 AND enabled AND status='SUSPENDED' AND trigger_at IS NULL FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
after=$(state)
test "$(request owner POST "/cards/$card/archive" "$archiveKey" '{"version":7}')" = 200
test "$after" = "$(state)"
test "$(request member PUT "$reminderPath" "$reminderKey" "$choice")" = 404
test "$(request owner POST "/cards/$card/restore" "$(uuid)" '{"version":8}')" = 200
test "$(admin "SELECT generation=9 AND status='SCHEDULED' AND trigger_at=due_at-interval '1 hour' FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND safe_metadata->>'reminderId'='$reminder';")" = 5
admin "UPDATE board_members SET status='REMOVED',version=version+1 WHERE tenant_id='$org' AND board_id='$board' AND user_id='$member';" >/dev/null
before=$(state)
test "$(request member PUT "$reminderPath" "$reminderKey" "$choice")" = 404
test "$before" = "$(state)"
echo 'Date-command Reminder generations, canonical future jobs, replay/no-op, completion/reopen/clear and publication rollback passed.'
echo 'Personal Reminder configuration, recipient privacy, stable cancellation, private events and revoked replay passed.'
echo 'Card archive/restore Reminder suspension, future generation renewal and archive receipt deduplication passed.'
