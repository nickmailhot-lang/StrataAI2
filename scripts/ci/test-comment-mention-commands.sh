#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable comment fixtures require CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d)
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() { admin 'GRANT INSERT ON card_assignment_notifications TO strataai_api_runtime;' >/dev/null || true; rm -rf "$scratch"; }
trap cleanup EXIT
trap 'echo "Comment mention fixture failed at line $LINENO" >&2' ERR
for actor in owner recipient; do
  jq -nc --arg email "mention-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"mention-correct-horse-battery",displayName:"Mention fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); recipient=$(jq -r '.user.id' "$scratch/recipient.user")
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -d "$5" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
get() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/response.json" -w '%{http_code}' "$base$2"; }
test "$(request owner POST /organizations 21111111-1111-1111-1111-111111111111 '{"name":"Mention commands"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response.json")
test "$(request owner POST /boards 21111111-1111-1111-1111-111111111112 "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Private mentions",visibility:"PRIVATE"}')")" = 201
board=$(jq -r '.id' "$scratch/response.json")
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$recipient','MEMBER','ACTIVE');
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),'$org','$board','$recipient','MEMBER','ACTIVE',now(),now());" >/dev/null
test "$(request owner POST "/boards/$board/lists" 21111111-1111-1111-1111-111111111113 '{"name":"Comments"}')" = 201
list=$(jq -r '.id' "$scratch/response.json")
test "$(request owner POST "/lists/$list/cards" 21111111-1111-1111-1111-111111111114 '{"title":"Mention Card"}')" = 201
card=$(jq -r '.id' "$scratch/response.json")
for id in "$owner" "$recipient" "$org" "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
path="/cards/$card/comments"
inbox="/organizations/$org/notifications"
key=21111111-1111-1111-1111-111111111115
text="Private comment @u_${recipient//-/} @u_${recipient//-/} @u_${owner//-/} @unknown_user"
body=$(jq -nc --arg text "$text" --arg recipient "$recipient" --arg handle "u_${recipient//-/}" '{content:$text,cardVersion:1,
 mentionSelections:[{userId:$recipient,handle:$handle,handleVersion:1}]}')
state() { admin "SELECT md5(jsonb_build_object(
 'card',(SELECT to_jsonb(c) FROM cards c WHERE id='$card'),
 'comments',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM card_comments c WHERE tenant_id='$org'),
 'snapshots',(SELECT jsonb_agg(to_jsonb(s) ORDER BY comment_id,comment_version) FROM comment_mention_snapshots s WHERE tenant_id='$org'),
 'recipients',(SELECT count(*) FROM comment_mention_recipients WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'notifications',(SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
before=$(state)
stale_body=$(jq '.mentionSelections[0].handleVersion=2' <<< "$body")
test "$(request owner POST "$path" "$key" "$stale_body")" = 409
test "$before" = "$(state)"
admin 'REVOKE INSERT ON card_assignment_notifications FROM strataai_api_runtime;' >/dev/null
test "$(request owner POST "$path" "$key" "$body")" = 503
admin 'GRANT INSERT ON card_assignment_notifications TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request owner POST "$path" "$key" "$body")" = 200
comment=$(jq -r '.comment.id' "$scratch/response.json")
jq -e '.changed==true and .cardVersion==2 and .comment.version==1' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/receipt.json"
after=$(state)
test "$(request owner POST "$path" "$key" "$body")" = 200
cmp "$scratch/response.json" "$scratch/receipt.json"
test "$after" = "$(state)"
test "$(admin "SELECT recipient_count FROM comment_mention_snapshots WHERE tenant_id='$org' AND comment_id='$comment' AND comment_version=1;")" = 2
test "$(admin "SELECT count(*) FROM card_assignment_notifications n JOIN work_events e
 ON e.tenant_id=n.tenant_id AND e.board_id=n.board_id AND e.event_id=n.event_id
 WHERE n.tenant_id='$org' AND n.card_id='$card' AND n.recipient_id='$recipient' AND n.actor_id='$owner'
 AND n.notification_type='MENTION_CREATED' AND e.event_type='MENTION_CREATED' AND e.entity_type='Card'
 AND e.entity_id=n.card_id AND e.actor_id=n.actor_id AND e.entity_version=n.card_version AND e.created_at=n.created_at AND e.metadata='{}'::jsonb;")" = 1
test "$(get recipient "$inbox")" = 200
jq -e --arg recipient "$recipient" --arg card "$card" '.items|length==1 and .[0].type=="MENTION_CREATED" and .[0].recipientId==$recipient and .[0].entityId==$card' "$scratch/response.json" >/dev/null
! grep -q 'Private comment' "$scratch/response.json"
test "$(get owner "$inbox")" = 200
jq -e '.items|length==0' "$scratch/response.json" >/dev/null
child="$path/$comment"
test "$(request owner PATCH "$child" 21111111-1111-1111-1111-111111111116 "$(jq -nc --arg text "$text updated" '{content:$text,cardVersion:2,version:1}')")" = 200
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org';")" = 1
test "$(request owner PATCH "$child" 21111111-1111-1111-1111-111111111117 '{"content":"Removed references","cardVersion":3,"version":2}')" = 200
test "$(request owner PATCH "$child" 21111111-1111-1111-1111-111111111118 "$(jq -nc --arg text "$text" '{content:$text,cardVersion:4,version:3}')")" = 200
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org';")" = 2
test "$(request owner DELETE "$child" 21111111-1111-1111-1111-111111111119 '{"cardVersion":5,"version":4,"confirmed":true}')" = 200
test "$(admin "SELECT count(*) FROM comment_mention_snapshots WHERE tenant_id='$org' AND comment_id='$comment' AND comment_version IN (3,5) AND recipient_count=0;")" = 2
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id='$card' AND event_type='MENTION_CREATED';")" = 2
admin "UPDATE board_members SET status='REMOVED',version=version+1 WHERE tenant_id='$org' AND board_id='$board' AND user_id='$recipient';" >/dev/null
test "$(get recipient "$inbox")" = 200
jq -e '.items|length==0' "$scratch/response.json" >/dev/null
test "$(get recipient "$path")" = 404
echo 'Exact-image username mentions: atomic late-storage refusal, same-key recovery, actual source affinity, self suppression, stable recipient deltas, redaction history and current inbox authorization passed.'
