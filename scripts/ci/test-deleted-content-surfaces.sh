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
surviving_search() {
 test "$(search active)" = 200
 jq -e --arg card "$card" --arg board "$destination" --arg list "$destination_list" \
  '(.items|length)==1 and .items[0].card.id==$card and .items[0].card.boardId==$board
   and .items[0].card.listId==$list and .items[0].card.lifecycleState=="active" and .items[0].card.version==3' "$scratch/page.json" >/dev/null
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
 for kind in card list board source_list source_board destination_list destination_board; do
  moved=false; survivor=false; notification_visible=false; archived_cursor=''
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
  if [[ "$kind" == source_* || "$kind" == destination_* ]]; then
   moved=true
   test "$(request owner POST /boards "$(jq -nc --arg org "$org" --arg v "$visibility" '{organizationId:$org,name:"Destination surface Board",visibility:$v}')")" = 201
   destination=$(jq -r '.id' "$scratch/response.json")
   test "$(request owner PATCH "/boards/$destination/members/$recipient" '{"role":"MEMBER"}')" = 200
   test "$(request owner POST "/boards/$destination/lists" '{"name":"Destination surface List"}')" = 201
   destination_list=$(jq -r '.id' "$scratch/response.json")
   for id in "$destination" "$destination_list"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
   move_key=$(uuid)
   move_body=$(jq -nc --arg board "$board" --arg list "$destination_list" '{sourceBoardId:$board,destinationListId:$list,expectedVersion:2}')
   test "$(request owner POST "/cards/$card/move" "$move_body" "$move_key")" = 200
   cp "$scratch/response.json" "$scratch/move-receipt.json"
   jq -e --arg board "$destination" --arg list "$destination_list" '.boardId==$board and .listId==$list and .version==3' "$scratch/response.json" >/dev/null
   surviving_search
   test "$(get "$path")" = 200
   jq -e --arg source "$board" --arg destination "$destination" --arg card "$card" --arg org "$org" \
    '(.items|length)==1 and .items[0].boardId==$source and .items[0].currentBoardId==$destination
     and .items[0].entityLink==("/app/"+$org+"/boards/"+$destination+"/cards/"+$card)' "$scratch/page.json" >/dev/null
  fi
  case "$kind" in
   card) entity=$card; resource="/cards/$card"; version=2; table=cards; extra='';;
   list) entity=$list; resource="/lists/$list"; version=1; table=board_lists; extra='&containedCardCount=1';;
   board) entity=$board; resource="/boards/$board"; version=1; table=boards; extra='';;
   source_list) entity=$list; resource="/lists/$list"; version=1; table=board_lists; extra='&containedCardCount=0'; survivor=true; notification_visible=true;;
   source_board) entity=$board; resource="/boards/$board"; version=1; table=boards; extra=''; survivor=true;;
   destination_list) entity=$destination_list; resource="/lists/$destination_list"; version=1; table=board_lists; extra='&containedCardCount=1';;
   destination_board) entity=$destination; resource="/boards/$destination"; version=1; table=boards; extra='';;
  esac
  test "$(request owner POST "$resource/archive" "$(jq -nc --argjson v "$version" '{version:$v}')")" = 200
  archive_at=$(jq -r '.archivedAt' "$scratch/response.json"); version=$((version+1))
  if $survivor; then
   surviving_search; empty_search archived
   surviving_card=$(admin "SELECT md5(to_jsonb(r)::text) FROM cards r WHERE tenant_id='$org' AND id='$card';")
  else
   empty_search active
   test "$(search archived)" = 200
   jq -e --arg card "$card" '(.items|length)==1 and .items[0].card.id==$card and (.nextCursor|type)=="string"' "$scratch/page.json" >/dev/null
   archived_cursor=$(jq -r '.nextCursor' "$scratch/page.json")
  fi
  delete_key=$(uuid)
  test "$(request owner DELETE "$resource?version=$version&confirmed=true$extra" '{}' "$delete_key")" = 200
  cp "$scratch/response.json" "$scratch/delete-receipt.json"
  jq -e --arg id "$entity" --arg actor "$owner" --arg at "$archive_at" '.id==$id and .lifecycleState=="deleted" and .deletedBy==$actor and .archivedAt==$at and (.deletedAt|type)=="string"' "$scratch/response.json" >/dev/null
  test "$(admin "SELECT lifecycle_state='DELETED' AND archived_at='$archive_at'::timestamptz AND deleted_by='$owner' FROM $table WHERE tenant_id='$org' AND id='$entity';")" = t
  before=$(effects)
  if $survivor; then
   surviving_search; empty_search archived
   test "$(admin "SELECT md5(to_jsonb(r)::text) FROM cards r WHERE tenant_id='$org' AND id='$card';")" = "$surviving_card"
  else
   empty_search active; empty_search archived; empty_search archived "$archived_cursor"
  fi
  test "$(get "$path")" = 200
  if $notification_visible; then
   jq -e --arg id "$notification" --arg destination "$destination" '(.items|length)==1 and .items[0].id==$id and .items[0].currentBoardId==$destination and .items[0].readAt!=null' "$scratch/page.json" >/dev/null
  else
   jq -e '.items==[] and .nextCursor==null' "$scratch/page.json" >/dev/null
  fi
  grep -iq '^cache-control: private, no-store' "$scratch/headers"
  # Retained history requires current source/destination admission.
  test "$(get "$path/sync?after=0")" = 200
  if $notification_visible; then
   jq -e --arg id "$notification" '(.events|length)==2 and .cursor=="2" and all(.events[];.entityId==$id)' "$scratch/page.json" >/dev/null
   test "$(request recipient POST "$path/$notification/read" '{}' "$read_key")" = 200
   jq -se '.[0]==.[1]' "$scratch/read-receipt.json" "$scratch/response.json" >/dev/null
   test "$(request recipient POST "$path/read" "$selection" "$bulk_key")" = 200
   jq -se '.[0]==.[1]' "$scratch/read-receipt.json" "$scratch/response.json" >/dev/null
  else
   jq -e '.events==[] and .cursor=="2" and (.hasMore|not) and (.resetRequired|not)' "$scratch/page.json" >/dev/null
   test "$(request recipient POST "$path/$notification/read" '{}' "$read_key")" = 404
   jq -e '.code=="notification_not_found" and (has("items")|not)' "$scratch/response.json" >/dev/null
   test "$(request recipient POST "$path/$notification/read" '{}')" = 404
   test "$(request recipient POST "$path/read" "$selection" "$bulk_key")" = 404
   jq -e '.code=="notification_not_found" and (has("items")|not)' "$scratch/response.json" >/dev/null
   test "$(request recipient POST "$path/read" "$selection")" = 404
  fi
  if $moved; then
   if $notification_visible; then
    test "$(request owner POST "/cards/$card/move" "$move_body" "$move_key")" = 200
    jq -se '.[0]==.[1]' "$scratch/move-receipt.json" "$scratch/response.json" >/dev/null
   else
    test "$(request owner POST "/cards/$card/move" "$move_body" "$move_key")" = 404
    jq -e '.code=="card_not_found"' "$scratch/response.json" >/dev/null
   fi
  fi
  test "$(request owner POST "$resource/restore" "$(jq -nc --argjson v "$((version+1))" '{version:$v}')")" = 404
  test "$(request owner DELETE "$resource?version=$version&confirmed=true$extra" '{}' "$delete_key")" = 200
  jq -se '.[0]==.[1]' "$scratch/delete-receipt.json" "$scratch/response.json" >/dev/null
  test "$(effects)" = "$before"
  completed=$((completed+1))
 done
done
test "$completed" = 21
echo 'Deleted content: 21 real deletion workflows across all Board visibilities suppress deleted search/notification content and receipts, preserve moved survivors and permitted historical recovery, refuse irreversible restoration, and preserve protected records/effects on original deletion recovery.'
