#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable comment fixtures require CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d)
gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'GRANT INSERT ON card_assignment_notifications TO strataai_api_runtime;' >/dev/null || true
  admin 'GRANT INSERT ON mass_mention_reservations TO strataai_api_runtime;' >/dev/null || true
  rm -rf "$scratch"
}
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
 'quotas',(SELECT jsonb_agg(to_jsonb(q) ORDER BY event_id) FROM mass_mention_reservations q WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
before=$(state)
gate() {
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 & gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\n%s\nSELECT '\''mention_locked'\'';\n' "$1" >&3
  for ((attempt=0;attempt<100;attempt++)); do
    if grep -q '^mention_locked$' "$scratch/gate.log"; then return; fi
    kill -0 "$gate_pid" || return 1; sleep 0.05
  done
  return 1
}
wait_counts() {
  admin "SELECT jsonb_build_object('apiLockWaits',count(*) FILTER(WHERE wait_event_type='Lock'),
    'fullGroupLockWaits',count(*) FILTER(WHERE wait_event_type='Lock' AND query LIKE '%ORDER BY m.user_id FOR SHARE OF m,o,u%'))
    FROM pg_stat_activity WHERE usename='strataai_api_runtime';" >&2
}
blocked() {
  for ((attempt=0;attempt<100;attempt++)); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '$1';")" = 1; then return; fi
    if ! kill -0 "$request_pid"; then wait_counts; return 1; fi; sleep 0.05
  done
  wait_counts
  return 1
}
release() { printf '%s\nCOMMIT;\n\\q\n' "$1" >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; rm "$scratch/gate.in" "$scratch/gate.log"; }
# Each request must actually be waiting in the recipient locking query before
# eligibility changes. No comment, receipt, event or notification may survive.
for boundary in board organization account; do
  case "$boundary" in
    board) row="board_members WHERE tenant_id='$org' AND board_id='$board' AND user_id='$recipient'"; column=status; refused=REMOVED; ;;
    organization) row="organization_members WHERE tenant_id='$org' AND user_id='$recipient'"; column=status; refused=REMOVED; ;;
    account) row="users WHERE id='$recipient'"; column=status; refused=DEACTIVATED; ;;
  esac
  gate "SELECT 1 FROM $row FOR UPDATE;"
  request owner POST "$path" "$key" "$body" > "$scratch/status" & request_pid=$!
  blocked '%FOR SHARE OF m,o,u,h%'
  release "UPDATE ${row%% WHERE*} SET $column='$refused' WHERE ${row#* WHERE };"
  wait "$request_pid"; request_pid=''
  test "$(cat "$scratch/status")" = 409
  jq -e '.code=="mention_targets_changed"' "$scratch/response.json" >/dev/null
  test "$before" = "$(state)"
  admin "UPDATE ${row%% WHERE*} SET $column='ACTIVE' WHERE ${row#* WHERE };" >/dev/null
done
# Hold the account before command verification locks its issuing session.
# Deleting an already SHARE-locked session from a Card gate would invert the
# lock order. Observe the actual account wait before revocation; the command
# must refuse and a fresh cookie may use the uncommitted key.
gate "SELECT 1 FROM users WHERE id='$owner' FOR UPDATE;"
request owner POST "$path" "$key" "$body" > "$scratch/status" & request_pid=$!
blocked '%FROM users WHERE id%FOR SHARE%'
release "DELETE FROM sessions WHERE user_id='$owner';"
wait "$request_pid"; request_pid=''
test "$(cat "$scratch/status")" = 401
test "$before" = "$(state)"
curl --fail --silent --show-error -c "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/owner.credentials")" "$base/auth/login" >/dev/null
# Natural expiry can cross a late parent wait without deleting a session row
# already locked by the command. Require initial admission and the actual Card
# update wait before allowing the clock to expire the issuing cookie.
hash=$(awk '$6=="strataai_session" {print $7}' "$scratch/owner.cookies" | tr -d '\n' | sha256sum | cut -d ' ' -f 1)
[[ "$hash" =~ ^[0-9a-f]{64}$ ]]
gate "SELECT id FROM cards WHERE tenant_id='$org' AND id='$card' FOR UPDATE;"
admin "UPDATE sessions SET expires_at=clock_timestamp()+interval '15 seconds' WHERE token_hash='$hash';" >/dev/null
request owner POST "$path" "$key" "$body" > "$scratch/status" & request_pid=$!
blocked '%UPDATE cards%'
test "$(admin "SELECT expires_at>clock_timestamp() FROM sessions WHERE token_hash='$hash';")" = t
expired=false
for ((attempt=0;attempt<200;attempt++)); do
  if test "$(admin "SELECT expires_at<=clock_timestamp() FROM sessions WHERE token_hash='$hash';")" = t; then expired=true; break; fi
  kill -0 "$request_pid" || exit 1; sleep 0.1
done
test "$expired" = true
release ''
wait "$request_pid"; request_pid=''
test "$(cat "$scratch/status")" = 401
jq -e '.code=="session_unavailable" and (has("comment")|not)' "$scratch/response.json" >/dev/null
test "$before" = "$(state)"
curl --fail --silent --show-error -c "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/owner.credentials")" "$base/auth/login" >/dev/null
echo 'Mention commands: issuing session expiry after a verified Card update wait rolls back Card/comment/history/event/audit/job/inbox/quota/receipt effects; fresh-cookie original-key recovery follows.'
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

admin "UPDATE board_members SET status='ACTIVE',version=version+1 WHERE tenant_id='$org' AND board_id='$board' AND user_id='$recipient';
 INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by) VALUES('$org','$board','$card','$recipient','$owner');" >/dev/null
# Current group eligibility is evaluated after real membership/account waits.
# Use a separate Board so its empty/self-only publications cannot alter the
# original Board's quota/fanout assertions. Membership onboarding is synthetic.
test "$(request owner POST /boards "$(cat /proc/sys/kernel/random/uuid)" "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Group wait boundaries",visibility:"PRIVATE"}')")" = 201
race_board=$(jq -r '.id' "$scratch/response.json")
test "$(request owner POST "/boards/$race_board/lists" "$(cat /proc/sys/kernel/random/uuid)" '{"name":"Wait boundaries"}')" = 201
race_list=$(jq -r '.id' "$scratch/response.json")
admin "INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),'$org','$race_board','$recipient','MEMBER','ACTIVE',clock_timestamp(),clock_timestamp());" >/dev/null
for scope in card board; do
  for boundary in board organization account; do
    test "$(request owner POST "/lists/$race_list/cards" "$(cat /proc/sys/kernel/random/uuid)" '{"title":"Current group roster"}')" = 201
    race_card=$(jq -r '.id' "$scratch/response.json")
    admin "INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by)
     VALUES('$org','$race_board','$race_card','$recipient','$owner');" >/dev/null
    case "$boundary" in
      board) row="board_members WHERE tenant_id='$org' AND board_id='$race_board' AND user_id='$recipient'"; refused=REMOVED; ;;
      organization) row="organization_members WHERE tenant_id='$org' AND user_id='$recipient'"; refused=REMOVED; ;;
      account) row="users WHERE id='$recipient'"; refused=DEACTIVATED; ;;
    esac
    race_key=$(cat /proc/sys/kernel/random/uuid)
    race_body=$(jq -nc --arg scope "$scope" '{content:("Current @"+$scope),cardVersion:1,massMentionConfirmation:{card:($scope=="card"),board:($scope=="board")}}')
    gate "SELECT 1 FROM $row FOR UPDATE;"
    request owner POST "/cards/$race_card/comments" "$race_key" "$race_body" > "$scratch/status" & request_pid=$!
    # Match the full-group query rather than requiring a trailing statement
    # delimiter in the text actually sent by the database driver.
    blocked '%ORDER BY m.user_id FOR SHARE OF m,o,u%'
    release "UPDATE ${row%% WHERE*} SET status='$refused' WHERE ${row#* WHERE };"
    wait "$request_pid"; request_pid=''
    test "$(cat "$scratch/status")" = 200
    jq -e '.changed==true and .cardVersion==2 and .comment.version==1' "$scratch/response.json" >/dev/null
    race_comment=$(jq -r '.comment.id' "$scratch/response.json")
    cp "$scratch/response.json" "$scratch/race-receipt.json"
    test "$(admin "SELECT count(*) FROM comment_mention_recipients WHERE tenant_id='$org' AND comment_id='$race_comment' AND recipient_id='$recipient';")" = 0
    expected=0; if test "$scope" = board; then expected=1; fi
    test "$(admin "SELECT recipient_count FROM comment_mention_snapshots WHERE tenant_id='$org' AND comment_id='$race_comment' AND comment_version=1;")" = "$expected"
    test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND board_id='$race_board';")" = 0
    test "$(admin "SELECT count(*) FROM mass_mention_reservations WHERE tenant_id='$org' AND board_id='$race_board';")" = 0
    # Restoring eligibility cannot revise history or send a fresh group on an
    # exact original receipt; all current rights of the issuing actor still hold.
    admin "UPDATE ${row%% WHERE*} SET status='ACTIVE' WHERE ${row#* WHERE };" >/dev/null
    race_after=$(state)
    test "$(request owner POST "/cards/$race_card/comments" "$race_key" "$race_body")" = 200
    cmp "$scratch/response.json" "$scratch/race-receipt.json"
    test "$race_after" = "$(state)"
  done
done
echo 'Exact-image Card/Board groups: verified recipient Board/Organization/account waits use current complete eligibility; restored recipients cannot alter original history, create inbox deliveries or consume quota on replay.'
# Disposable active/verified membership fixtures make this actual API fanout
# exceed the 20-name window. Their onboarding is synthetic, not a signup test.
admin "WITH seeded AS (
 INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 SELECT id,'mass-group-'||id::text||'@example.test',upper('mass-group-'||id::text||'@example.test'),
 'Mass fixture','ACTIVE',true,'fixture',clock_timestamp(),clock_timestamp()
 FROM (SELECT gen_random_uuid() id FROM generate_series(1,24)) ids RETURNING id)
 INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),'$org',id,'MEMBER','ACTIVE' FROM seeded;
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board',m.user_id,'MEMBER','ACTIVE',clock_timestamp(),clock_timestamp()
 FROM organization_members m JOIN users u ON u.id=m.user_id WHERE m.tenant_id='$org' AND u.email LIKE 'mass-group-%';" >/dev/null
before=$(state)
test "$(request recipient POST "$path" 21111111-1111-1111-1111-111111111120 '{"content":"@board","cardVersion":6,"massMentionConfirmation":{"card":false,"board":true}}')" = 404
test "$before" = "$(state)"
test "$(request owner POST "$path" 21111111-1111-1111-1111-111111111121 '{"content":"https://example.test/@board","cardVersion":6,"massMentionConfirmation":{"card":false,"board":true}}')" = 400
test "$before" = "$(state)"
group_key=21111111-1111-1111-1111-111111111122
group_body=$(jq -nc --arg text "Group @card @board @u_${recipient//-/}" '{content:$text,cardVersion:6,massMentionConfirmation:{card:true,board:true}}')
admin 'REVOKE INSERT ON mass_mention_reservations FROM strataai_api_runtime;' >/dev/null
test "$(request owner POST "$path" "$group_key" "$group_body")" = 503
admin 'GRANT INSERT ON mass_mention_reservations TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
admin 'REVOKE INSERT ON card_assignment_notifications FROM strataai_api_runtime;' >/dev/null
test "$(request owner POST "$path" "$group_key" "$group_body")" = 503
admin 'GRANT INSERT ON card_assignment_notifications TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request owner POST "$path" "$group_key" "$group_body")" = 200
group_comment=$(jq -r '.comment.id' "$scratch/response.json")
cp "$scratch/response.json" "$scratch/group-receipt.json"
after=$(state)
test "$(request owner POST "$path" "$group_key" "$group_body")" = 200
cmp "$scratch/response.json" "$scratch/group-receipt.json"
test "$after" = "$(state)"
test "$(admin "SELECT recipient_count FROM comment_mention_snapshots WHERE tenant_id='$org' AND comment_id='$group_comment' AND comment_version=1;")" = 26
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND event_id=(SELECT event_id FROM mass_mention_reservations WHERE tenant_id='$org');")" = 25
test "$(admin "SELECT count(*) FROM mass_mention_reservations WHERE tenant_id='$org';")" = 1
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$recipient';")" = 3
test "$(request owner PATCH "$path/$group_comment" 21111111-1111-1111-1111-111111111123 "$(jq '.content += " edited" | .cardVersion=7 | .version=1' <<< "$group_body")")" = 200
test "$(admin "SELECT count(*) FROM mass_mention_reservations WHERE tenant_id='$org';")" = 1
for version in 8 9; do
  test "$(request owner POST "$path" "$(cat /proc/sys/kernel/random/uuid)" "$(jq -nc --argjson version "$version" '{content:("Group @board "+($version|tostring)),cardVersion:$version,massMentionConfirmation:{card:false,board:true}}')")" = 200
done
before=$(state)
limited_body='{"content":"Fourth group @board","cardVersion":10,"massMentionConfirmation":{"card":false,"board":true}}'
test "$(request owner POST "$path" 21111111-1111-1111-1111-111111111124 "$limited_body")" = 429
test "$before" = "$(state)"
test "$(request owner POST "$path" 21111111-1111-1111-1111-111111111124 "$limited_body")" = 429
test "$before" = "$(state)"
test "$(admin "SELECT count(*) FROM mass_mention_reservations WHERE tenant_id='$org';")" = 3
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org' AND recipient_id='$recipient';")" = 5
test "$(request recipient POST "$path" 21111111-1111-1111-1111-111111111125 '{"content":"Self group @card","cardVersion":10,"massMentionConfirmation":{"card":true,"board":false}}')" = 200
test "$(admin "SELECT count(*) FROM mass_mention_reservations WHERE tenant_id='$org';")" = 3
test "$(admin "SELECT count(*) FROM card_assignment_notifications WHERE tenant_id='$org';")" = 77
echo 'Exact-image confirmed groups: current administration, Card assignment scope, overlap/self suppression, quota and late notification rollback, original-key recovery, stable edits and rate-refusal full rollback passed.'
echo 'Exact-image username mentions: recipient Board/Organization/account and issuing session lock waits, atomic late-storage refusal, same-key recovery, actual source affinity, self suppression, stable recipient deltas, redaction history and current inbox authorization passed.'
