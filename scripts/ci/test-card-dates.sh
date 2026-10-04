#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable Card date fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d); worker_scoped=false
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  admin 'GRANT INSERT ON card_assignment_notifications, background_jobs, work_events TO strataai_api_runtime;' >/dev/null || true
  if test "$worker_scoped" = true; then docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null || true; fi
  rm -rf "$scratch"
}
trap cleanup EXIT
failure() {
  echo "Card date check failed at line $1" >&2
  if test "$worker_scoped" = true; then
    # Safe aggregates survive Worker cleanup without exposing personal IDs,
    # message bodies, queue metadata or credentials in workflow logs.
    admin "SELECT job_type,state,last_error_code,count(*),max(attempt_count),count(*) FILTER(WHERE available_at<=clock_timestamp()) FROM background_jobs WHERE tenant_id='$org' GROUP BY job_type,state,last_error_code ORDER BY job_type,state;" >&2 || true
    admin "SELECT status,count(*) FROM card_reminders WHERE tenant_id='$org' GROUP BY status ORDER BY status;" >&2 || true
  fi
}
trap 'failure "$LINENO"' ERR
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
 'organization',(SELECT to_jsonb(o) FROM organizations o WHERE id='$org'),
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'reminders',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM card_reminders r WHERE tenant_id='$org'),
 'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id='$org'),
 'boards',(SELECT jsonb_agg(to_jsonb(b) ORDER BY id) FROM boards b WHERE tenant_id='$org'),
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
# PRD-16 completion filtering uses the canonical dates state through the exact
# release API/PostgreSQL path, with normal admission before invalid input.
filterBaseline=$(state)
filter_read() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/filter.json" -w '%{http_code}' "$base/boards/$board/cards?$2"; }
for dueState in overdue upcoming none; do
 test "$(filter_read owner "due=$dueState")" = 200
 jq -e '.items|length==0' "$scratch/filter.json" >/dev/null
done
test "$(filter_read owner 'due=unknown')" = 400
jq -e '.code=="invalid_board_filter"' "$scratch/filter.json" >/dev/null
test "$(filter_read outsider 'due=unknown')" = 404
test "$(filter_read owner 'completion=complete')" = 200
jq -e --arg card "$card" '.items | length==1 and .[0].id==$card and .[0].dueComplete' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'completion=incomplete')" = 200
jq -e '.items | length==0' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'completion=complete&keyword=absent&match=all')" = 200
jq -e '.items | length==0' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'completion=complete&keyword=absent&match=any')" = 200
jq -e --arg card "$card" '.items | length==1 and .[0].id==$card' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'completion=unknown')" = 400
jq -e '.code=="invalid_board_filter"' "$scratch/filter.json" >/dev/null
test "$(filter_read outsider 'completion=unknown')" = 404
test "$(state)" = "$filterBaseline"
# Trusted disposable capacity setup around the genuinely completed Card.
# The filtered reads still run through Nginx and the restricted exact API.
admin "WITH source AS (SELECT * FROM cards WHERE tenant_id='$org' AND id='$card'),
 fixtures AS (SELECT gen_random_uuid() id,n FROM generate_series(1,52) n)
 INSERT INTO cards SELECT populated.* FROM source c CROSS JOIN fixtures f CROSS JOIN LATERAL
 jsonb_populate_record(NULL::cards,to_jsonb(c)||jsonb_build_object('id',f.id,
 'title','Completion paging fixture','rank',lpad((400000000000000000000000000000::numeric+f.n)::text,30,'0'),
 'cover_attachment_id',NULL)) populated RETURNING id;" > "$scratch/completion-paging.ids"
test "$(wc -l < "$scratch/completion-paging.ids")" = 52
test "$(filter_read owner 'completion=complete')" = 200
cp "$scratch/filter.json" "$scratch/completion-first.json"
jq -e '.items|length==50 and all(.dueComplete)' "$scratch/completion-first.json" >/dev/null
after=$(jq -r '.nextCursor' "$scratch/completion-first.json"); [[ "$after" =~ ^[0-9a-fA-F-]{36}$ ]]
test "$(filter_read owner "completion=complete&after=$after")" = 200
jq -e '.nextCursor==null and (.items|length==3 and all(.dueComplete))' "$scratch/filter.json" >/dev/null
jq -se '[.[].items[].id] | length==53 and (unique|length==53)' "$scratch/completion-first.json" "$scratch/filter.json" >/dev/null
pagingIds=''
while read -r fixtureCard; do
 [[ "$fixtureCard" =~ ^[0-9a-fA-F-]{36}$ ]]
 pagingIds+="${pagingIds:+,}'$fixtureCard'"
done < "$scratch/completion-paging.ids"
admin "DELETE FROM cards WHERE tenant_id='$org' AND id=ANY(ARRAY[$pagingIds]::uuid[]) AND title='Completion paging fixture';" >/dev/null
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.dueComplete=false | .version=3' <<< "$complete")")" = 200
test "$(filter_read owner 'completion=complete')" = 200
jq -e '.items | length==0' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'completion=incomplete')" = 200
jq -e --arg card "$card" '.items | length==1 and .[0].id==$card and (.[0].dueComplete|not)' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'due=overdue')" = 200
jq -e --arg card "$card" '.items|length==1 and .[0].id==$card' "$scratch/filter.json" >/dev/null
# One explicit trusted future-deadline fixture verifies actual PostgreSQL UTC
# comparison without relying on a fixed calendar date becoming overdue later.
futureCard=$(uuid)
admin "INSERT INTO cards SELECT populated.* FROM cards c CROSS JOIN LATERAL
 jsonb_populate_record(NULL::cards,to_jsonb(c)||jsonb_build_object('id','$futureCard',
 'title','Future deadline fixture','rank','400000000000000000000000000000',
 'due_at',clock_timestamp()+interval '1 day','due_timezone','UTC','due_has_time',true,
 'created_at',statement_timestamp()-interval '14 days','updated_at',statement_timestamp()-interval '14 days')) populated
 WHERE c.tenant_id='$org' AND c.id='$card';" >/dev/null
test "$(filter_read owner 'due=upcoming')" = 200
jq -e --arg card "$futureCard" '.items|length==1 and .[0].id==$card' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'due=overdue&keyword=Future&match=any')" = 200
jq -e '.items|length==2' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'due=overdue&keyword=Future&match=all')" = 200
jq -e '.items|length==0' "$scratch/filter.json" >/dev/null
recentBaseline=$(state)
for window in day week; do
 test "$(filter_read owner "activity=$window")" = 200
 jq -e --arg card "$card" '.items|length==1 and .[0].id==$card' "$scratch/filter.json" >/dev/null
done
test "$(filter_read owner 'activity=month')" = 200
jq -e '.items|length==2' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'activity=day&keyword=Future&match=any')" = 200
jq -e '.items|length==2' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'activity=day&keyword=Future&match=all')" = 200
jq -e '.items|length==0' "$scratch/filter.json" >/dev/null
test "$(filter_read owner 'activity=unknown')" = 400
jq -e '.code=="invalid_board_filter"' "$scratch/filter.json" >/dev/null
test "$(filter_read outsider 'activity=unknown')" = 404
test "$(state)" = "$recentBaseline"
admin "DELETE FROM cards WHERE tenant_id='$org' AND id='$futureCard' AND title='Future deadline fixture';" >/dev/null
test "$(request owner PATCH "$path" "$(uuid)" '{"version":4,"dueHasTime":false,"dueComplete":false}')" = 200
jq -e '.card.version==5 and .card.startAt==null and .card.dueAt==null and .card.dueTimezone==null and .card.dueComplete==false' "$scratch/response.json" >/dev/null
test "$(filter_read owner 'due=none')" = 200
jq -e --arg card "$card" '.items|length==1 and .[0].id==$card' "$scratch/filter.json" >/dev/null
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
reminderPath="/cards/$card/reminders"; reminderKey=$(uuid)
choice='{"intervalCode":"1_HOUR","enabled":true,"cardVersion":2,"version":0}'
test "$(request outsider POST "$reminderPath" "$(uuid)" "$choice")" = 404
test "$(request member POST "$reminderPath" "$reminderKey" "$choice")" = 200
jq -e --arg user "$member" '.changed and .userId==$user and .cardVersion==2 and .reminder.userId==$user and .reminder.generation==1 and .reminder.status=="SCHEDULED" and (.options|length)==4' "$scratch/response.json" >/dev/null
reminder=$(jq -r '.reminder.id' "$scratch/response.json")
cp "$scratch/response.json" "$scratch/reminder-created.json"
test "$(request member POST "$reminderPath" "$reminderKey" "$choice")" = 200
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
test "$(request member POST "$reminderPath" "$(uuid)" '{"intervalCode":"1_HOUR","enabled":true,"cardVersion":6,"version":6}')" = 400
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_type='Reminder' AND entity_id='$reminder' AND event_type IN ('REMINDER_SCHEDULED','REMINDER_CANCELLED');")" = 6
test "$(request owner PATCH "$path" "$(uuid)" "$(jq -c '.version=6' <<< "$payload")")" = 200
test "$(request member POST "$reminderPath" "$(uuid)" '{"intervalCode":"1_HOUR","enabled":true,"cardVersion":7,"version":6}')" = 200
archiveKey=$(uuid)
test "$(request owner POST "/cards/$card/archive" "$archiveKey" '{"version":7}')" = 200
test "$(admin "SELECT generation=8 AND enabled AND status='SUSPENDED' AND trigger_at IS NULL FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
after=$(state)
test "$(request owner POST "/cards/$card/archive" "$archiveKey" '{"version":7}')" = 200
test "$after" = "$(state)"
test "$(request member POST "$reminderPath" "$reminderKey" "$choice")" = 404
test "$(request owner POST "/cards/$card/restore" "$(uuid)" '{"version":8}')" = 200
test "$(admin "SELECT generation=9 AND status='SCHEDULED' AND trigger_at=due_at-interval '1 hour' FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND safe_metadata->>'reminderId'='$reminder';")" = 5
test "$(request owner POST "$reminderPath" "$(uuid)" '{"intervalCode":"1_HOUR","enabled":true,"cardVersion":9,"version":0}')" = 200
cancelledReminder=$(jq -r '.reminder.id' "$scratch/response.json")
test "$(request owner DELETE "$reminderPath?cardVersion=9&version=1" "$(uuid)" '{}')" = 200
# Internal lifecycle processing must reach every chosen Card, rather than a
# bounded UI page. Seed 76 valid personal choices for the admitted recipient.
admin "INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,created_at,updated_at,version,due_at,due_timezone,due_has_time,due_complete)
 SELECT gen_random_uuid(),'$org','$board','$list','Container fanout '||n,lpad((500000000000000000000000000000::numeric+n)::text,30,'0'),'ACTIVE',clock_timestamp(),clock_timestamp(),1,'$due','UTC',true,false FROM generate_series(1,76) n;
 INSERT INTO card_reminders(tenant_id,id,user_id,card_id,interval_code,enabled,due_at,trigger_at,status,generation,version,created_at,updated_at)
 SELECT tenant_id,gen_random_uuid(),'$member',id,'1_HOUR',true,due_at,due_at-interval '1 hour','SCHEDULED',1,1,clock_timestamp(),clock_timestamp()
 FROM cards WHERE tenant_id='$org' AND title LIKE 'Container fanout %';" >/dev/null
listArchiveKey=$(uuid)
before=$(state)
admin 'REVOKE INSERT ON background_jobs FROM strataai_api_runtime;' >/dev/null
test "$(request owner POST "/lists/$list/archive" "$listArchiveKey" '{"version":1}')" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON background_jobs TO strataai_api_runtime;' >/dev/null
test "$(request owner POST "/lists/$list/archive" "$listArchiveKey" '{"version":1}')" = 200
test "$(admin "SELECT generation=10 AND status='SUSPENDED' AND trigger_at IS NULL FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(admin "SELECT count(*) FROM card_reminders r JOIN cards c ON c.tenant_id=r.tenant_id AND c.id=r.card_id WHERE r.tenant_id='$org' AND c.title LIKE 'Container fanout %' AND r.generation=2 AND r.status='SUSPENDED' AND r.trigger_at IS NULL;")" = 76
boardVersion=$(admin "SELECT version FROM boards WHERE tenant_id='$org' AND id='$board';")
test "$(request owner POST "/boards/$board/archive" "$(uuid)" "{\"version\":$boardVersion}")" = 200
boardVersion=$(admin "SELECT version FROM boards WHERE tenant_id='$org' AND id='$board';")
test "$(request owner POST "/boards/$board/restore" "$(uuid)" "{\"version\":$boardVersion}")" = 200
test "$(admin "SELECT generation=10 AND status='SUSPENDED' FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(request owner POST "/lists/$list/restore" "$(uuid)" '{"version":2}')" = 200
test "$(admin "SELECT generation=11 AND status='SCHEDULED' AND trigger_at=due_at-interval '1 hour' FROM card_reminders WHERE tenant_id='$org' AND id='$reminder';")" = t
test "$(admin "SELECT generation=2 AND NOT enabled AND status='CANCELLED' FROM card_reminders WHERE tenant_id='$org' AND id='$cancelledReminder';")" = t
test "$(admin "SELECT count(*) FROM card_reminders r JOIN cards c ON c.tenant_id=r.tenant_id AND c.id=r.card_id WHERE r.tenant_id='$org' AND c.title LIKE 'Container fanout %' AND r.generation=3 AND r.status='SCHEDULED' AND c.version=1 AND c.lifecycle_state='ACTIVE';")" = 76
test "$(admin "SELECT count(*) FROM background_jobs j JOIN card_reminders r ON r.tenant_id=j.tenant_id AND r.id::text=j.safe_metadata->>'reminderId' JOIN cards c ON c.tenant_id=r.tenant_id AND c.id=r.card_id WHERE j.tenant_id='$org' AND j.job_type='CARD_REMINDER' AND c.title LIKE 'Container fanout %' AND j.safe_metadata->>'generation'='3';")" = 76
after=$(state)
test "$(request owner POST "/lists/$list/archive" "$listArchiveKey" '{"version":1}')" = 200
test "$after" = "$(state)"
test "$(admin "SELECT version=9 FROM cards WHERE tenant_id='$org' AND id='$card';")" = t
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND safe_metadata->>'reminderId'='$reminder';")" = 6
# Exercise the complete production path: explicit personal choice -> canonical
# future outbox job -> leased release Worker -> one private in-app notification.
fireOwner=$(jq -r '.user.id' "$scratch/owner.user")
[[ "$fireOwner" =~ ^[0-9a-fA-F-]{36}$ ]]
# The release Worker requires verified recipients by default. Provision the
# disposable account accordingly; do not relax the production delivery policy.
admin "UPDATE users SET email_verified=true,version=version+1,updated_at=now() WHERE id='$fireOwner';" >/dev/null
fireCard=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"title":"Worker due reminder"}' "$base/lists/$list/cards" | jq -r '.id')
firePath="/cards/$fireCard/reminders"; fireDue=$(date -u -d '+20 seconds' '+%Y-%m-%dT%H:%M:%SZ')
test "$(request owner PATCH "/cards/$fireCard/dates" "$(uuid)" "$(jq -nc --arg due "$fireDue" '{dueAt:$due,dueTimezone:"UTC",dueHasTime:true,dueComplete:false,version:1}')")" = 200
fireKey=$(uuid); fireChoice='{"intervalCode":"AT_DUE","enabled":true,"cardVersion":2,"version":0}'
test "$(request owner POST "$firePath" "$fireKey" "$fireChoice")" = 200
fireReminder=$(jq -r '.reminder.id' "$scratch/response.json")
cp "$scratch/response.json" "$scratch/fire-created.json"
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND card_id='$fireCard';")" = 0
export STRATAAI_TEST_EVENT_ORGANIZATION_ID="$org"; worker_scoped=true
docker compose -f compose.release.yml -f scripts/ci/compose.work-event-test.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
for ((attempt=0;attempt<60;attempt++)); do
  if test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND safe_metadata->>'reminderId'='$fireReminder' AND state='SUCCEEDED';")" = 1; then break; fi
  sleep 1
done
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND safe_metadata->>'reminderId'='$fireReminder' AND state='SUCCEEDED';")" = 1
test "$(admin "SELECT generation=1 AND version=2 AND status='FIRED' FROM card_reminders WHERE tenant_id='$org' AND id='$fireReminder';")" = t
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND card_id='$fireCard' AND notification_type='REMINDER_FIRED' AND actor_id=recipient_id;")" = 1
curl --fail --silent --show-error -b "$scratch/owner.cookies" "$base$firePath" | jq -e '.reminder.status=="FIRED" and .reminder.version==2 and .reminder.generation==1' >/dev/null
curl --fail --silent --show-error -b "$scratch/owner.cookies" "$base/organizations/$org/notifications" | jq -e --arg card "$fireCard" '.items | any(.entityId==$card and .type=="REMINDER_FIRED")' >/dev/null
test "$(request owner POST "$firePath" "$fireKey" "$fireChoice")" = 200
cmp "$scratch/fire-created.json" "$scratch/response.json"
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='CARD_REMINDER' AND safe_metadata->>'reminderId'='$fireReminder';")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_type='Reminder' AND entity_id='$fireReminder' AND event_type='REMINDER_FIRED' AND ready_at IS NOT NULL;")" = 1
test "$(admin "SELECT version=2 AND lifecycle_state='ACTIVE' FROM cards WHERE tenant_id='$org' AND id='$fireCard';")" = t
# Board policy controls display only, using admin admission and Board CAS. It
# must not rewrite Card dates or renew already fired/future Reminder generations.
policyVersion=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" "$base/boards/$board" | jq -r '.board.version')
policyPath="/boards/$board/date-policy"; policyKey=$(uuid)
policyPayload=$(jq -nc --argjson version "$policyVersion" '{timezone:"Pacific/Honolulu",version:$version}')
before=$(state)
test "$(request member PATCH "$policyPath" "$(uuid)" "$policyPayload")" = 404
test "$(request owner PATCH "$policyPath" "$(uuid)" "$(jq -c '.timezone="Unknown/Place"' <<< "$policyPayload")")" = 400
test "$before" = "$(state)"
admin 'REVOKE INSERT ON work_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "$policyPath" "$policyKey" "$policyPayload")" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON work_events TO strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "$policyPath" "$policyKey" "$policyPayload")" = 200
jq -e --argjson version "$policyVersion" '.changed and .board.dateTimezoneOverride=="Pacific/Honolulu" and .board.version==$version+1' "$scratch/response.json" >/dev/null
after=$(state)
test "$(request owner PATCH "$policyPath" "$policyKey" "$policyPayload")" = 200
test "$after" = "$(state)"
test "$(admin "SELECT version=2 AND due_at='$fireDue' FROM cards WHERE tenant_id='$org' AND id='$fireCard';")" = t
test "$(admin "SELECT generation=1 AND version=2 AND status='FIRED' FROM card_reminders WHERE tenant_id='$org' AND id='$fireReminder';")" = t
test "$(request owner PATCH "$policyPath" "$(uuid)" "$(jq -nc --argjson version "$((policyVersion+1))" '{timezone:null,version:$version}')")" = 200
admin "UPDATE board_members SET status='REMOVED',version=version+1 WHERE tenant_id='$org' AND board_id='$board' AND user_id='$member';" >/dev/null
before=$(state)
test "$(request member POST "$reminderPath" "$reminderKey" "$choice")" = 404
test "$before" = "$(state)"
# The existing owner deletion-request command must suspend every chosen Card
# in the same Organization transaction, preserving dates and cancelled choices.
orgVersion=$(admin "SELECT version FROM organizations WHERE id='$org';")
before=$(state)
test "$(request member DELETE "/organizations/$org?version=$orgVersion" "$(uuid)" '{}')" = 404
test "$before" = "$(state)"
admin 'REVOKE INSERT ON work_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner DELETE "/organizations/$org?version=$orgVersion" "$(uuid)" '{}')" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON work_events TO strataai_api_runtime;' >/dev/null
test "$(request owner DELETE "/organizations/$org?version=$orgVersion" "$(uuid)" '{}')" = 202
test "$(admin "SELECT status='DELETING' AND version=$orgVersion+1 FROM organizations WHERE id='$org';")" = t
test "$(admin "SELECT count(*) FROM card_reminders WHERE tenant_id='$org' AND enabled AND (status<>'SUSPENDED' OR trigger_at IS NOT NULL);")" = 0
test "$(admin "SELECT count(*) FROM card_reminders r JOIN cards c ON c.tenant_id=r.tenant_id AND c.id=r.card_id WHERE r.tenant_id='$org' AND c.title LIKE 'Container fanout %' AND r.generation=4 AND r.status='SUSPENDED' AND c.version=1 AND c.lifecycle_state='ACTIVE';")" = 76
test "$(admin "SELECT generation=2 AND NOT enabled AND status='CANCELLED' FROM card_reminders WHERE tenant_id='$org' AND id='$cancelledReminder';")" = t
test "$(admin "SELECT version=2 AND due_at='$fireDue' AND lifecycle_state='ACTIVE' FROM cards WHERE tenant_id='$org' AND id='$fireCard';")" = t
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND card_id='$fireCard' AND notification_type='REMINDER_FIRED';")" = 1
test "$(curl --silent --show-error -b "$scratch/owner.cookies" -o "$scratch/response.json" -w '%{http_code}' "$base/boards/$board")" = 404
echo 'Date-command Reminder generations, canonical future jobs, replay/no-op, completion/reopen/clear and publication rollback passed.'
echo 'Personal Reminder configuration, recipient privacy, stable cancellation, private events and revoked replay passed.'
echo 'Card archive/restore Reminder suspension, future generation renewal and archive receipt deduplication passed.'
echo 'List/Board archive contexts, all 76 chosen Cards, selective renewal, cancelled-choice preservation and lifecycle publication rollback passed.'
echo 'Real release Worker Reminder delivery, self inbox, canonical FIRED revision and post-delivery receipt deduplication passed.'
echo 'Board timezone policy admin admission, event-publication rollback, receipt replay and unchanged UTC dates/Reminder generations passed.'
echo 'Organization deletion-request Reminder suspension, all chosen Cards, cancelled-choice/date/audit preservation and full event-publication rollback passed.'
