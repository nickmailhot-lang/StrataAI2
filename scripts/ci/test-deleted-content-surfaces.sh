#!/usr/bin/env bash
set -euo pipefail
umask 077
# PRD-18 LIFE-FR-011: actual deletion, current search/inbox/sync/read admission.
test "${CI:-}" = true || { echo 'Disposable deleted-content fixture requires CI.' >&2; exit 1; }
base=${STRATAAI_TEST_BASE_URL:-http://localhost:8088}
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
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
 local scope=$1 after=${2:-}; local args=(--get --data-urlencode "q=$keyword" --data-urlencode "scope=$scope")
 if test -n "$after"; then args+=(--data-urlencode "after=$after"); fi
 curl --max-time 60 --silent --show-error -b "$scratch/recipient.cookies" "${args[@]}" \
  -D "$scratch/headers" -o "$scratch/page.json" -w '%{http_code}' "$base/search"
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
 for kind in card list board; do
  keyword="deleted-surface-$visibility-$kind-$(uuid)"
  test "$(request owner POST /organizations '{"name":"Deletion surface scope"}')" = 201
  org=$(jq -r '.organization.id' "$scratch/response.json"); [[ "$org" =~ ^[0-9a-fA-F-]{36}$ ]]
  test "$(request owner POST "/organizations/$org/invitations" "$(jq -nc --arg email "$(cat "$scratch/recipient.email")" '{email:$email,surface:"INTERNAL",targetRole:"MEMBER"}')")" = 201
  invite=$(jq -r '.id' "$scratch/response.json")
  test "$(request recipient POST "/me/invitations/$invite/accept" '{}')" = 200
  test "$(request owner POST /boards "$(jq -nc --arg org "$org" --arg v "$visibility" '{organizationId:$org,name:"Surface Board",visibility:$v}')")" = 201
  board=$(jq -r '.id' "$scratch/response.json")
  test "$(request owner PATCH "/boards/$board/members/$recipient" '{"role":"MEMBER"}')" = 200
  test "$(request owner POST "/boards/$board/lists" '{"name":"Surface List"}')" = 201
  list=$(jq -r '.id' "$scratch/response.json")
  test "$(request owner POST "/lists/$list/cards" "$(jq -nc --arg title "$keyword" '{title:$title}')")" = 201
  card=$(jq -r '.id' "$scratch/response.json")
  for id in "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
  test "$(request owner PUT "/cards/$card/members/$recipient?version=1" '{}')" = 200
  test "$(search active)" = 200
  jq -e --arg card "$card" '(.items|length)==1 and .items[0].card.id==$card' "$scratch/page.json" >/dev/null
  path="/organizations/$org/notifications"
  test "$(get "$path")" = 200
  jq -e --arg card "$card" '(.items|length)==1 and .items[0].entityId==$card and .items[0].readAt==null' "$scratch/page.json" >/dev/null
  notification=$(jq -r '.items[0].id' "$scratch/page.json"); read_key=$(uuid)
  test "$(request recipient POST "$path/$notification/read" '{}' "$read_key")" = 200
  cp "$scratch/response.json" "$scratch/read-receipt.json"
  bulk_key=$(uuid); selection=$(jq -nc --arg id "$notification" '{ids:[$id]}')
  test "$(request recipient POST "$path/read" "$selection" "$bulk_key")" = 200
  jq -se '.[0]==.[1]' "$scratch/read-receipt.json" "$scratch/response.json" >/dev/null
  test "$(get "$path/sync?after=0")" = 200
  jq -e --arg id "$notification" '(.events|length)==2 and .cursor=="2" and (.hasMore|not) and all(.events[];.entityId==$id)' "$scratch/page.json" >/dev/null
  case "$kind" in
   card) entity=$card; resource="/cards/$card"; version=2; table=cards; extra='';;
   list) entity=$list; resource="/lists/$list"; version=1; table=board_lists; extra='&containedCardCount=1';;
   board) entity=$board; resource="/boards/$board"; version=1; table=boards; extra='';;
  esac
  test "$(request owner POST "$resource/archive" "$(jq -nc --argjson v "$version" '{version:$v}')")" = 200
  archive_at=$(jq -r '.archivedAt' "$scratch/response.json"); version=$((version+1))
  empty_search active
  test "$(search archived)" = 200
  jq -e --arg card "$card" '(.items|length)==1 and .items[0].card.id==$card and (.nextCursor|type)=="string"' "$scratch/page.json" >/dev/null
  archived_cursor=$(jq -r '.nextCursor' "$scratch/page.json")
  delete_key=$(uuid)
  test "$(request owner DELETE "$resource?version=$version&confirmed=true$extra" '{}' "$delete_key")" = 200
  cp "$scratch/response.json" "$scratch/delete-receipt.json"
  jq -e --arg id "$entity" --arg actor "$owner" --arg at "$archive_at" '.id==$id and .lifecycleState=="deleted" and .deletedBy==$actor and .archivedAt==$at and (.deletedAt|type)=="string"' "$scratch/response.json" >/dev/null
  test "$(admin "SELECT lifecycle_state='DELETED' AND archived_at='$archive_at'::timestamptz AND deleted_by='$owner' FROM $table WHERE tenant_id='$org' AND id='$entity';")" = t
  before=$(effects)
  empty_search active; empty_search archived; empty_search archived "$archived_cursor"
  test "$(get "$path")" = 200
  jq -e '.items==[] and .nextCursor==null' "$scratch/page.json" >/dev/null
  grep -iq '^cache-control: private, no-store' "$scratch/headers"
  # Historical sources survive; current admission suppresses both created/read events.
  test "$(get "$path/sync?after=0")" = 200
  jq -e '.events==[] and .cursor=="2" and (.hasMore|not) and (.resetRequired|not)' "$scratch/page.json" >/dev/null
  test "$(request recipient POST "$path/$notification/read" '{}' "$read_key")" = 404
  jq -e '.code=="notification_not_found" and (has("items")|not)' "$scratch/response.json" >/dev/null
  test "$(request recipient POST "$path/$notification/read" '{}')" = 404
  test "$(request recipient POST "$path/read" "$selection" "$bulk_key")" = 404
  jq -e '.code=="notification_not_found" and (has("items")|not)' "$scratch/response.json" >/dev/null
  test "$(request recipient POST "$path/read" "$selection")" = 404
  test "$(request owner POST "$resource/restore" "$(jq -nc --argjson v "$((version+1))" '{version:$v}')")" = 404
  test "$(request owner DELETE "$resource?version=$version&confirmed=true$extra" '{}' "$delete_key")" = 200
  jq -se '.[0]==.[1]' "$scratch/delete-receipt.json" "$scratch/response.json" >/dev/null
  test "$(effects)" = "$before"
  completed=$((completed+1))
 done
done
test "$completed" = 9
echo 'Deleted content: nine real Card/List/Board deletion workflows across all Board visibilities suppress fresh/continued search, inbox, historical sync and old/new single/bulk read commands; irreversible restoration is refused and original deletion recovery preserves protected records/effects.'
