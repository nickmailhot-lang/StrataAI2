#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || exit 1
umask 077
org=$1; member=$2; owner_cookie=$3; member_cookie=$4
for id in "$org" "$member"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
base=http://localhost:8088
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Moved attachment receipt check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
request() { curl --max-time 60 --silent --show-error -b "$1" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -d "$5" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
new_board() {
  test "$(request "$owner_cookie" POST /boards "$(uuid)" "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Moved attachment receipt Board",visibility:"PRIVATE"}')")" = 201
  jq -r '.id' "$scratch/response.json"
}
source=$(new_board); destination=$(new_board)
for board in "$source" "$destination"; do
  [[ "$board" =~ ^[0-9a-fA-F-]{36}$ ]]
  test "$(request "$owner_cookie" POST "/boards/$board/lists" "$(uuid)" '{"name":"Receipt parent"}')" = 201
  parent=$(jq -r '.id' "$scratch/response.json"); [[ "$parent" =~ ^[0-9a-fA-F-]{36}$ ]]
  if test "$board" = "$source"; then source_list=$parent; else destination_list=$parent; fi
done
state() { admin "SELECT md5(jsonb_build_object(
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'child',(SELECT to_jsonb(a) FROM attachments a WHERE tenant_id='$org' AND id='$attachment'),
 'events',(SELECT jsonb_agg(to_jsonb(e)-'ready_at' ORDER BY event_id) FROM work_events e WHERE tenant_id='$org'),
 'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$org'),
 'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY actor_id,key_id) FROM work_command_replays r WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'))::text);"; }
for action in url archive restore delete; do
  for board in "$source" "$destination"; do
    test "$(request "$owner_cookie" PATCH "/boards/$board/members/$member" "$(uuid)" '{"role":"ADMIN"}')" = 200
  done
  test "$(request "$member_cookie" POST "/lists/$source_list/cards" "$(uuid)" '{"title":"Moved attachment command"}')" = 201
  card=$(jq -r '.id' "$scratch/response.json"); [[ "$card" =~ ^[0-9a-fA-F-]{36}$ ]]
  path="/cards/$card/attachments"; key=$(uuid); body='{"title":"Retained link","url":"https://example.test/moved-receipt","cardVersion":1}'
  test "$(request "$member_cookie" POST "$path/url" "$key" "$body")" = 200
  attachment=$(jq -r '.attachment.id' "$scratch/response.json"); [[ "$attachment" =~ ^[0-9a-fA-F-]{36}$ ]]
  revision=2; child_revision=1; method=POST; command_path="$path/url"
  if test "$action" != url; then
    if test "$action" != archive; then
      test "$(request "$member_cookie" POST "$path/$attachment/archive" "$(uuid)" '{"cardVersion":2,"version":1}')" = 200
      revision=3; child_revision=2
    fi
    key=$(uuid); body=$(jq -nc --argjson cardVersion "$revision" --argjson version "$child_revision" '{cardVersion:$cardVersion,version:$version}')
    command_path="$path/$attachment/$action"
    if test "$action" = delete; then method=DELETE; command_path="$path/$attachment?cardVersion=$revision&version=$child_revision&confirmed=true"; fi
    test "$(request "$member_cookie" "$method" "$command_path" "$key" "$body")" = 200
    revision=$((revision+1))
  fi
  cp "$scratch/response.json" "$scratch/receipt.json"
  move=$(jq -nc --arg source "$source" --arg destination "$destination_list" --argjson version "$revision" '{sourceBoardId:$source,destinationListId:$destination,expectedVersion:$version}')
  test "$(request "$member_cookie" POST "/cards/$card/move" "$(uuid)" "$move")" = 200
  before=$(state)
  test "$(request "$member_cookie" "$method" "$command_path" "$key" "$body")" = 200
  cmp "$scratch/receipt.json" "$scratch/response.json"; test "$before" = "$(state)"
  for board in "$destination" "$source"; do
    if test "$action" = delete; then
      test "$(request "$owner_cookie" PATCH "/boards/$board/members/$member" "$(uuid)" '{"role":"MEMBER"}')" = 200
    else
      test "$(request "$owner_cookie" DELETE "/boards/$board/members/$member" "$(uuid)" '{}')" = 204
    fi
    # Membership commands have their own legitimate audit/events/receipts.
    # Compare each attachment retry against the post-command state.
    before=$(state)
    test "$(request "$member_cookie" "$method" "$command_path" "$key" "$body")" = 404
    test "$before" = "$(state)"
    test "$(request "$owner_cookie" PATCH "/boards/$board/members/$member" "$(uuid)" '{"role":"ADMIN"}')" = 200
    before=$(state)
    test "$(request "$member_cookie" "$method" "$command_path" "$key" "$body")" = 200
    cmp "$scratch/receipt.json" "$scratch/response.json"; test "$before" = "$(state)"
  done
done
echo 'Exact-image moved URL/archive/restore/delete receipts: original/current authority, administrator demotion, exact recovery and unchanged command effects passed.'
