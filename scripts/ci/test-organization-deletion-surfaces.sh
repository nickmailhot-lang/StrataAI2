#!/usr/bin/env bash
set -euo pipefail
umask 077
# PRD-03/18: accepted Organization deletion, separate Worker completion and
# current search/inbox/sync/receipt admission after original-session revocation.
test "${CI:-}" = true || { echo 'Disposable deleted-content fixture requires CI.' >&2; exit 1; }
base=${STRATAAI_TEST_BASE_URL:-http://localhost:8088}
scratch=$(mktemp -d)
worker_active=false
cleanup() {
 if $worker_active; then docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null || true; fi
 rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Deleted-content surface check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
request() {
 local actor=$1 method=$2 path=$3 body=$4 key=${5:-$(uuid)}
 curl --max-time 60 --silent --show-error -b "$scratch/$actor.cookies" -X "$method" \
  -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $key" \
  -d "$body" -o "$scratch/response.json" -w '%{http_code}' "$base$path"
}
get() { curl --max-time 60 --silent --show-error -b "$scratch/recipient.cookies" -D "$scratch/headers" -o "$scratch/page.json" -w '%{http_code}' "$base$1"; }
search() {
 local scope=$1 after=${2:-} code next page
 : > "$scratch/search-seen"
 if test -n "$after"; then printf '%s\n' "$after" >> "$scratch/search-seen"; fi
 # Empty bounded traversal pages can carry a continuation even when there is
 # no matching content. Follow them without relaxing the server's read budget.
 for ((page=0;page<64;page++)); do
  local args=(--get --data-urlencode "q=$keyword" --data-urlencode "scope=$scope")
  if test -n "$after"; then args+=(--data-urlencode "after=$after"); fi
  code=$(curl --max-time 60 --silent --show-error -b "$scratch/recipient.cookies" "${args[@]}" \
   -D "$scratch/headers" -o "$scratch/page.json" -w '%{http_code}' "$base/search")
  if test "$code" != 200; then printf '%s' "$code"; return; fi
  jq -e '(.items|type)=="array"' "$scratch/page.json" >/dev/null
  grep -iq '^cache-control: private, no-store' "$scratch/headers"
  next=$(jq -r '.nextCursor // empty' "$scratch/page.json")
  if jq -e '(.items|length)>0' "$scratch/page.json" >/dev/null || test -z "$next"; then printf '%s' "$code"; return; fi
  if grep -Fqx -- "$next" "$scratch/search-seen"; then echo 'Search traversal repeated a cursor.' >&2; return 1; fi
  printf '%s\n' "$next" >> "$scratch/search-seen"; after=$next
 done
 echo 'Search traversal exceeded the fixture page bound.' >&2; return 1
}
empty_search() {
 test "$(search "$1" "${2:-}")" = 200
 jq -e '.items==[] and .nextCursor==null' "$scratch/page.json" >/dev/null
 grep -iq '^cache-control: private, no-store' "$scratch/headers"
}
effects() {
 admin "SELECT md5(jsonb_build_object(
 'boards',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM boards r WHERE tenant_id='$org'),
 'lists',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM board_lists r WHERE tenant_id='$org'),
 'cards',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM cards r WHERE tenant_id='$org'),
 'notifications',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM card_assignment_notifications r WHERE tenant_id='$org'),
 'journal',(SELECT jsonb_agg(to_jsonb(r) ORDER BY recipient_id,sequence) FROM notification_events r WHERE tenant_id='$org'),
 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='WORK_EVENT_READY'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"
}
for actor in owner recipient; do
 email="deleted-content-$actor-$(uuid)@example.test"
 credentials=$(jq -nc --arg email "$email" '{email:$email,password:"deleted-content-fixture-correct-horse",displayName:"Surface fixture"}')
 test "$(request "$actor" POST /auth/register "$credentials")" = 201
 id=$(jq -r '.user.id' "$scratch/response.json"); [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]
 # Account activation/verification is fixture setup when provider delivery keeps
 # the token private. This contract does not establish email-provider delivery.
 verification=$(jq -r '.verificationToken // empty' "$scratch/response.json")
 if test -n "$verification"; then
  test "$(request "$actor" POST /auth/verify-email "$(jq -nc --arg token "$verification" '{token:$token}')")" = 200
 else
  admin "UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,now()) WHERE id='$id';" >/dev/null
 fi
 printf '%s' "$id" > "$scratch/$actor.id"; printf '%s' "$email" > "$scratch/$actor.email"
 login_status=$(curl --max-time 60 --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' \
  -H 'Content-Type: application/json' -d "$credentials" -o "$scratch/login.json" -w '%{http_code}' "$base/auth/login")
 if test "$login_status" != 200; then echo "Deleted-content login status: $login_status" >&2; exit 1; fi
done
owner=$(cat "$scratch/owner.id"); recipient=$(cat "$scratch/recipient.id")
completed=0
for visibility in PRIVATE ORGANIZATION PUBLIC; do
 keyword="terminal-surface-$visibility-$(uuid)"
 test "$(request owner POST /organizations '{"name":"Terminal surface scope"}')" = 201
 org=$(jq -r '.organization.id' "$scratch/response.json"); [[ "$org" =~ ^[0-9a-fA-F-]{36}$ ]]
 test "$(request owner POST "/organizations/$org/invitations" "$(jq -nc --arg email "$(cat "$scratch/recipient.email")" '{email:$email,surface:"INTERNAL",targetRole:"MEMBER"}')")" = 201
 invite=$(jq -r '.id' "$scratch/response.json")
 test "$(request recipient POST "/me/invitations/$invite/accept" '{}')" = 200
 test "$(request owner POST /boards "$(jq -nc --arg org "$org" --arg v "$visibility" '{organizationId:$org,name:"Terminal surface Board",visibility:$v}')")" = 201
 board=$(jq -r '.id' "$scratch/response.json")
 test "$(request owner PATCH "/boards/$board/members/$recipient" '{"role":"MEMBER"}')" = 200
 test "$(request owner POST "/boards/$board/lists" '{"name":"Terminal surface List"}')" = 201
 list=$(jq -r '.id' "$scratch/response.json")
 test "$(request owner POST "/lists/$list/cards" "$(jq -nc --arg title "$keyword" '{title:$title}')")" = 201
 card=$(jq -r '.id' "$scratch/response.json")
 for id in "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
 test "$(request owner PUT "/cards/$card/members/$recipient?version=1" '{}')" = 200
 path="/organizations/$org/notifications"
 test "$(get "$path")" = 200
 notification=$(jq -r '.items[0].id' "$scratch/page.json"); [[ "$notification" =~ ^[0-9a-fA-F-]{36}$ ]]
 read_key=$(uuid); bulk_key=$(uuid); selection=$(jq -nc --arg id "$notification" '{ids:[$id]}')
 test "$(request recipient POST "$path/$notification/read" '{}' "$read_key")" = 200
 test "$(request recipient POST "$path/read" "$selection" "$bulk_key")" = 200
 test "$(request owner POST "/cards/$card/archive" '{"version":2}')" = 200
 archive_at=$(jq -r '.archivedAt' "$scratch/response.json")
 test "$(search archived)" = 200
 jq -e --arg card "$card" '(.items|length)==1 and .items[0].card.id==$card' "$scratch/page.json" >/dev/null
 archived_cursor=$(jq -r '.nextCursor' "$scratch/page.json")
 test "$(request owner POST "/lists/$list/cards" "$(jq -nc --arg title "$keyword sibling" '{title:$title}')")" = 201
 sibling=$(jq -r '.id' "$scratch/response.json"); [[ "$sibling" =~ ^[0-9a-fA-F-]{36}$ ]]
 test "$(search active)" = 200
 jq -e --arg id "$sibling" '(.items|length)==1 and .items[0].card.id==$id' "$scratch/page.json" >/dev/null
 deletion_key=$(uuid)
 test "$(request owner DELETE "/organizations/$org?version=1&expectedActorId=$owner" '{}' "$deletion_key")" = 202
 cp "$scratch/response.json" "$scratch/request-receipt.json"
 # The separate Worker must derive authority from the accepted request after
 # original browser-session revocation. No fixture advances graph/checkpoints.
 test "$(request owner POST /auth/logout '{}')" = 204
 test "$(request owner GET /me '')" = 401
 worker_active=true
 STRATAAI_WORKER_ORGANIZATION_IDS="$org" STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED=false \
 STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=false STRATAAI_INVITATION_RECIPIENT_AUTHORITY_DISCOVERY_ENABLED=false \
 STRATAAI_INVITATION_ISSUER_AUTHORITY_DISCOVERY_ENABLED=false \
 docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
 complete=false
 for ((attempt=0;attempt<120;attempt++)); do
  if test "$(admin "SELECT EXISTS(SELECT 1 FROM organizations o JOIN organization_deletion_progress p ON p.tenant_id=o.id
   JOIN organization_lifecycle_events e ON e.tenant_id=o.id WHERE o.id='$org' AND o.status='DELETED' AND o.version=3 AND o.deleted_by='$owner'
   AND p.phase='COMPLETE' AND p.completed_at=o.deleted_at AND e.event_type='ORGANIZATION_DELETED'
   AND e.actor_id='$owner' AND e.entity_version=3 AND e.created_at=o.deleted_at AND e.ready_at IS NOT NULL)
   AND NOT EXISTS(SELECT 1 FROM background_jobs WHERE tenant_id='$org' AND job_type IN
    ('ORGANIZATION_DELETE_PAGE','ORGANIZATION_LIFECYCLE_EVENT_READY','WORK_EVENT_READY') AND state<>'SUCCEEDED');")" = t; then complete=true; break; fi
  sleep 1
 done
 $complete
 docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
 worker_active=false
 credentials=$(jq -nc --arg email "$(cat "$scratch/owner.email")" '{email:$email,password:"deleted-content-fixture-correct-horse"}')
 test "$(curl --max-time 60 --silent --show-error -c "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$credentials" -o "$scratch/login.json" -w '%{http_code}' "$base/auth/login")" = 200
 test "$(admin "SELECT (SELECT count(*) FROM boards WHERE tenant_id='$org' AND lifecycle_state='DELETED')=1
  AND (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND lifecycle_state='DELETED')=1
  AND (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='DELETED' AND deleted_by='$owner')=2
  AND EXISTS(SELECT 1 FROM cards WHERE tenant_id='$org' AND id='$card' AND archived_at='$archive_at'::timestamptz)
  AND EXISTS(SELECT 1 FROM cards WHERE tenant_id='$org' AND id='$sibling' AND archived_at IS NULL)
  AND (SELECT count(*) FROM notification_events WHERE tenant_id='$org')=2; ")" = t
 before=$(effects)
 empty_search active; empty_search archived; empty_search archived "$archived_cursor"
 for suffix in '' '/sync?after=0'; do
  test "$(get "$path$suffix")" = 404
  jq -e '.code=="notification_not_found" and (has("items")|not) and (has("events")|not)' "$scratch/page.json" >/dev/null
 done
 test "$(request recipient POST "$path/$notification/read" '{}' "$read_key")" = 404
 test "$(request recipient POST "$path/read" "$selection" "$bulk_key")" = 404
 test "$(request recipient POST "$path/$notification/read" '{}')" = 404
 test "$(request recipient POST "$path/read" "$selection")" = 404
 for resource in "/boards/$board" "/lists/$list" "/cards/$card"; do
  test "$(request owner POST "$resource/restore" '{"version":2}')" = 404
 done
 test "$(request owner DELETE "/organizations/$org?version=1&expectedActorId=$owner" '{}' "$deletion_key")" = 202
 jq -se '.[0]==.[1]' "$scratch/request-receipt.json" "$scratch/response.json" >/dev/null
 test "$(request owner GET "/organizations/$org/deletion-requests/$deletion_key?expectedActorId=$owner" '')" = 200
 jq -e --arg key "$deletion_key" 'keys==["completedAt","eventId","requestId","state","version"] and .requestId==$key
  and .state=="COMPLETED" and .version==3 and (.eventId|type)=="string" and (.completedAt|type)=="string"' "$scratch/response.json" >/dev/null
 test "$(request recipient GET "/organizations/$org/deletion-requests/$deletion_key" '')" = 404
 test "$(request recipient GET /me '')" = 200
 test "$(effects)" = "$before"
 completed=$((completed+1))
done
test "$completed" = 3
echo 'Organization terminal surfaces: three real accepted requests complete through a separate scoped Worker after original-session logout; retained active/archived descendants, search continuations, inbox, historical sync and old/new read receipts remain inaccessible, and fresh Owner completion/request recovery changes no protected records/effects.'
