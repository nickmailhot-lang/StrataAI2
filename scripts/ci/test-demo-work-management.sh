#!/usr/bin/env bash
set -euo pipefail
trap 'echo "Work-management check failed at line $LINENO" >&2' ERR

BASE_URL="${1:-http://127.0.0.1:18080}"
COOKIE_JAR="${2:?Pass the authenticated cookie jar path}"

organization="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d '{"name":"Work Management CI","description":"PRD-04 through PRD-08"}'     "$BASE_URL/organizations"
)"
organization_id="$(printf '%s' "$organization" | jq -r '.organization.id')"

board="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d "$(jq -nc --arg org "$organization_id"       '{organizationId:$org,name:"Council Operations",description:"CI Board",visibility:"PRIVATE",backgroundType:"COLOR",backgroundValue:"#0f4c81"}')"     "$BASE_URL/boards"
)"
board_id="$(printf '%s' "$board" | jq -r '.id')"
board_version="$(printf '%s' "$board" | jq -r '.version')"
test "$board_version" = "1"
test "$(printf '%s' "$board" | jq -r '.visibility')" = "PRIVATE"
test "$(printf '%s' "$board" | jq -r '.lifecycleState')" = "active"

private_status="$(
  curl --silent --output /dev/null --write-out '%{http_code}'     "$BASE_URL/boards/$board_id"
)"
test "$private_status" = "404"

list_one="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d '{"name":"New","rank":null}'     "$BASE_URL/boards/$board_id/lists"
)"
list_one_id="$(printf '%s' "$list_one" | jq -r '.id')"
list_one_rank="$(printf '%s' "$list_one" | jq -r '.rank')"

list_two="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d '{"name":"In Progress","rank":null}'     "$BASE_URL/boards/$board_id/lists"
)"
list_two_id="$(printf '%s' "$list_two" | jq -r '.id')"
list_two_rank="$(printf '%s' "$list_two" | jq -r '.rank')"

if [ "$list_one_rank" = "$list_two_rank" ]; then
  echo "Automatically allocated List ranks collided." >&2
  exit 1
fi

card="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d '{"title":"Inspect roof","description":"Initial card","rank":null}'     "$BASE_URL/lists/$list_one_id/cards"
)"
card_id="$(printf '%s' "$card" | jq -r '.id')"
card_rank="$(printf '%s' "$card" | jq -r '.rank')"
card_version="$(printf '%s' "$card" | jq -r '.version')"
test "$card_version" = "1"

snapshot="$(curl --fail --silent -b "$COOKIE_JAR" "$BASE_URL/boards/$board_id")"
test "$(printf '%s' "$snapshot" | jq '.lists | length')" = "2"
test "$(printf '%s' "$snapshot" | jq '[.lists[].cards[]] | length')" = "1"

updated="$(
  curl --fail --silent     -X PATCH     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d '{"title":"Inspect roof and flashings","description":"Updated by CI","version":1}'     "$BASE_URL/cards/$card_id"
)"
card_version="$(printf '%s' "$updated" | jq -r '.version')"
test "$card_version" = "2"

moved="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d "$(jq -nc       --arg list "$list_two_id"       --arg rank "$card_rank"       '{destinationListId:$list,rank:$rank,expectedVersion:2}')"     "$BASE_URL/cards/$card_id/move"
)"
test "$(printf '%s' "$moved" | jq -r '.listId')" = "$list_two_id"
card_version="$(printf '%s' "$moved" | jq -r '.version')"
test "$card_version" = "3"

curl --fail --silent   -b "$COOKIE_JAR"   -H 'Content-Type: application/json'   -d '{"version":3}'   "$BASE_URL/cards/$card_id/archive" >/tmp/card-archived.json
test "$(jq -r '.lifecycleState' /tmp/card-archived.json)" = "archived"

snapshot="$(curl --fail --silent -b "$COOKIE_JAR" "$BASE_URL/boards/$board_id")"
test "$(printf '%s' "$snapshot" | jq '[.lists[].cards[]] | length')" = "0"

curl --fail --silent   -b "$COOKIE_JAR"   -H 'Content-Type: application/json'   -d '{"version":4}'   "$BASE_URL/cards/$card_id/restore" >/tmp/card-restored.json

curl --fail --silent   -X PUT   -b "$COOKIE_JAR"   "$BASE_URL/boards/$board_id/star" >/dev/null
snapshot="$(curl --fail --silent -b "$COOKIE_JAR" "$BASE_URL/boards/$board_id")"
test "$(printf '%s' "$snapshot" | jq -r '.starred')" = "true"

visibility_response="$(
  curl --fail --silent     -X PATCH     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d "$(jq -nc --argjson version "$board_version"       '{visibility:"PUBLIC",version:$version}')"     "$BASE_URL/boards/$board_id/visibility"
)"
board_version="$(printf '%s' "$visibility_response" | jq -r '.version')"
test "$board_version" = "2"

public_status="$(
  curl --silent --output /tmp/public-board.json --write-out '%{http_code}'     "$BASE_URL/boards/$board_id"
)"
test "$public_status" = "200"
grep -q '"canView":true' /tmp/public-board.json
grep -q '"canEdit":false' /tmp/public-board.json

archive="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d "$(jq -nc --argjson version "$board_version" '{version:$version}')"     "$BASE_URL/boards/$board_id/archive"
)"
test "$(printf '%s' "$archive" | jq -r '.lifecycleState')" = "archived"
board_version="$(printf '%s' "$archive" | jq -r '.version')"

restore="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d "$(jq -nc --argjson version "$board_version" '{version:$version}')"     "$BASE_URL/boards/$board_id/restore"
)"
test "$(printf '%s' "$restore" | jq -r '.lifecycleState')" = "active"

echo "Board/List/Card lifecycle and authorization checks passed."
