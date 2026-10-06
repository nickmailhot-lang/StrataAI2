#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable global-search fixtures may run only in CI.' >&2; exit 1; }
umask 077
base=http://localhost:8088
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Global search check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$4" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
search() {
  local actor=$1 scope=$2 after=${3:-} query=${4:-100%_}
  local args=(--get --data-urlencode "q=$query" --data-urlencode "scope=$scope")
  if test -n "$after"; then args+=(--data-urlencode "after=$after"); fi
  curl --max-time 60 --silent --show-error -b "$scratch/$actor.cookies" "${args[@]}" -D "$scratch/headers" -o "$scratch/page.json" -w '%{http_code}' "$base/search"
}
for actor in owner outsider; do
  credentials=$(jq -nc --arg email "global-search-$actor-$(uuid)@example.test" '{email:$email,password:"search-fixture-battery-horse",displayName:"Search fixture"}')
  test "$(request "$actor" POST /auth/register "$credentials")" = 201
  test "$(curl --max-time 60 --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" -o /dev/null -w '%{http_code}' "$base/auth/login")" = 200
done
test "$(request owner POST /organizations '{"name":"Search Organization"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response.json")
test "$(request owner POST /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Search Board",visibility:"PRIVATE"}')")" = 201
board=$(jq -r '.id' "$scratch/response.json")
test "$(request owner POST "/boards/$board/lists" '{"name":"Search List"}')" = 201
list=$(jq -r '.id' "$scratch/response.json")
for id in "$org" "$board" "$list"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
for index in $(seq 1 52); do
  test "$(request owner POST "/lists/$list/cards" "$(jq -nc --arg title "Search $index 100%_" '{title:$title}')")" = 201
  jq -r '.id' "$scratch/response.json" >> "$scratch/expected.ids"
done
# The exact web proxy must reach the authenticated API, never return SPA HTML.
test "$(curl --max-time 60 --silent --show-error -o /dev/null -w '%{http_code}' "$base/search?q=100%25_")" = 401
test "$(search outsider active)" = 200
jq -e '.items==[] and .nextCursor==null' "$scratch/page.json" >/dev/null
# Search must project policy from the authorized canonical Board read.
test "$(search owner active)" = 200
jq -e '(.items|length)==50 and all(.items[]; has("boardDateTimezone") and .boardDateTimezone==null)' "$scratch/page.json" >/dev/null
board_version=1
for zone in Pacific/Honolulu Asia/Tokyo CLEAR; do
  policy="$(jq -nc --arg zone "$zone" --argjson version "$board_version" '{timezone:(if $zone=="CLEAR" then null else $zone end),version:$version}')"
  test "$(request owner PATCH "/boards/$board/date-policy" "$policy")" = 200
  board_version="$(jq -r '.board.version' "$scratch/response.json")"
  test "$(search owner active)" = 200
  grep -iq '^cache-control: private, no-store' "$scratch/headers"
  jq -e --arg zone "$zone" '(.items|length)==50 and all(.items[]; has("boardDateTimezone") and .boardDateTimezone==(if $zone=="CLEAR" then null else $zone end))' "$scratch/page.json" >/dev/null
  test "$(search outsider active)" = 200
  jq -e '.items==[] and .nextCursor==null' "$scratch/page.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Pacific/Honolulu|Asia/Tokyo|Search Board|Search List' "$scratch/page.json"
done
test "$(search owner active)" = 200
grep -iq '^cache-control: private, no-store' "$scratch/headers"
jq -e --arg org "$org" --arg board "$board" --arg list "$list" '(.items|length)==50 and (.nextCursor|type)=="string" and all(.items[]; .sourceKind=="CARD" and .card.organizationId==$org and .card.boardId==$board and .card.listId==$list and .boardName=="Search Board" and .listName=="Search List")' "$scratch/page.json" >/dev/null
jq -r '.items[].card.id' "$scratch/page.json" > "$scratch/actual.ids"
cursor=$(jq -r '.nextCursor' "$scratch/page.json")
test "$(search outsider active "$cursor")" = 400
jq -e '.code=="invalid_search"' "$scratch/page.json" >/dev/null
test "$(search owner active "$cursor" changed)" = 400
jq -e '.code=="invalid_search"' "$scratch/page.json" >/dev/null
test "$(search owner active "$cursor")" = 200
jq -e '(.items|length)==2 and (.nextCursor|type)=="string"' "$scratch/page.json" >/dev/null
jq -r '.items[].card.id' "$scratch/page.json" >> "$scratch/actual.ids"
sort "$scratch/expected.ids" > "$scratch/expected.sorted"
sort "$scratch/actual.ids" > "$scratch/actual.sorted"
diff -u "$scratch/expected.sorted" "$scratch/actual.sorted"
cursor=$(jq -r '.nextCursor' "$scratch/page.json")
test "$(search owner active "$cursor")" = 200
jq -e '.items==[] and .nextCursor==null' "$scratch/page.json" >/dev/null
date_card="$(head -n 1 "$scratch/expected.ids")"
test "$(request owner PATCH "/cards/$date_card/dates" '{"startAt":null,"dueAt":"2040-01-02T00:30:00Z","dueTimezone":"UTC","dueHasTime":true,"dueComplete":false,"version":1}')" = 200
test "$(search owner active '' 'Search 1 100%_')" = 200
jq -e --arg card "$date_card" '(.items|length)==1 and .items[0].card.id==$card and .items[0].card.dueHasTime==true
  and (.items[0].card.dueAt|test("^2040-01-02T00:30:00(Z|\\+00:00)$"))' "$scratch/page.json" >/dev/null
test "$(request owner PATCH "/cards/$date_card/dates" '{"startAt":null,"dueAt":"2040-01-02","dueTimezone":"Pacific/Honolulu","dueHasTime":false,"dueComplete":false,"version":2}')" = 200
test "$(search owner active '' 'Search 1 100%_')" = 200
grep -iq '^cache-control: private, no-store' "$scratch/headers"
jq -e --arg card "$date_card" '(.items|length)==1 and .items[0].card.id==$card and .items[0].card.dueHasTime==false
  and .items[0].card.dueTimezone=="Pacific/Honolulu"
  and (.items[0].card.dueAt|test("^2040-01-03T09:59:59\\.9999990?(Z|\\+00:00)$"))' "$scratch/page.json" >/dev/null
test "$(search outsider active '' 'Search 1 100%_')" = 200
jq -e '.items==[] and .nextCursor==null' "$scratch/page.json" >/dev/null
scripts/ci/assert-file-excludes.sh 'Pacific/Honolulu|2040-01|Search Board|Search List' "$scratch/page.json"

test "$(search owner unexpected)" = 400
jq -e '.code=="invalid_search"' "$scratch/page.json" >/dev/null
test "$(request owner POST "/lists/$list/archive" '{"version":1}')" = 200
test "$(search owner active)" = 200
jq -e '.items==[] and .nextCursor==null' "$scratch/page.json" >/dev/null
test "$(search owner archived)" = 200
jq -e '(.items|length)==50 and (.nextCursor|type)=="string"' "$scratch/page.json" >/dev/null
echo 'Exact-image global search: private scope, literal matching, 50+2 continuation, actor/query binding and archived parent passed.'
