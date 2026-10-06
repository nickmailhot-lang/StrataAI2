#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable navigation fixtures may run only in CI.' >&2; exit 1; }
umask 077
base=http://localhost:8088
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Navigation observation check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
request() {
  curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$4" -o "$scratch/response.json" -w '%{http_code}' "$base$3"
}
observe() {
  curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X POST -H 'X-StrataAI-Request: 1' \
    -H "X-StrataAI-Expected-Actor: $2" -H "Idempotency-Key: $3" -D "$scratch/headers" \
    -o "$scratch/response.json" -w '%{http_code}' "$base/navigation/observations?$4"
}
for account in owner outsider; do
  credentials=$(jq -nc --arg email "navigation-$account-$(uuid)@example.test" '{email:$email,password:"navigation-fixture-battery-horse",displayName:"Navigation fixture"}')
  test "$(request "$account" POST /auth/register "$credentials")" = 201
  test "$(curl --max-time 60 --silent --show-error -c "$scratch/$account.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" -o /dev/null -w '%{http_code}' "$base/auth/login")" = 200
  curl --fail --max-time 60 --silent --show-error -b "$scratch/$account.cookies" "$base/me" | jq -r '.id' > "$scratch/$account.actor"
done
actor=$(cat "$scratch/owner.actor"); outsider=$(cat "$scratch/outsider.actor")
test "$(request owner POST /organizations '{"name":"Navigation Organization"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response.json")
test "$(request owner POST /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Navigation Board",visibility:"PRIVATE"}')")" = 201
board=$(jq -r '.id' "$scratch/response.json"); board_version=$(jq -r '.version' "$scratch/response.json")
test "$(request owner POST "/boards/$board/lists" '{"name":"Navigation List"}')" = 201
list=$(jq -r '.id' "$scratch/response.json")
test "$(request owner POST "/lists/$list/cards" '{"title":"Navigation Card"}')" = 201
card=$(jq -r '.id' "$scratch/response.json"); card_version=$(jq -r '.version' "$scratch/response.json")
for id in "$actor" "$outsider" "$org" "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
test "$(curl --max-time 60 --silent --show-error -X POST -H 'X-StrataAI-Request: 1' -o /dev/null -w '%{http_code}' "$base/navigation/observations?kind=context")" = 401
test "$(observe owner "$outsider" "$(uuid)" 'kind=context')" = 401
for query in 'kind=context' "kind=context&organizationId=$org" "kind=board&organizationId=$org&boardId=$board&version=$board_version"; do
  key=$(uuid)
  test "$(observe owner "$actor" "$key" "$query")" = 200
  grep -iq '^cache-control: private, no-store' "$scratch/headers"
  jq -e --arg actor "$actor" '(keys|sort)==(["eventId","eventType","actorId","organizationId","boardId","entityType","entityId","version","metadata","createdAt"]|sort) and .actorId==$actor and .metadata=={} and .version>0' "$scratch/response.json" >/dev/null
  cp "$scratch/response.json" "$scratch/original.json"
  test "$(observe owner "$actor" "$key" "$query")" = 200
  cmp "$scratch/original.json" "$scratch/response.json"
done
card_key=$(uuid); card_query="kind=card&organizationId=$org&boardId=$board&cardId=$card&version=$card_version"
concurrent_key=$(uuid)
pids=()
for index in 1 2 3; do
  (
    code=$(curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -X POST -H 'X-StrataAI-Request: 1' \
      -H "X-StrataAI-Expected-Actor: $actor" -H "Idempotency-Key: $concurrent_key" \
      -o "$scratch/concurrent-$index.json" -w '%{http_code}' "$base/navigation/observations?kind=context")
    test "$code" = 200
  ) &
  pids+=("$!")
done
for pid in "${pids[@]}"; do wait "$pid"; done
cmp "$scratch/concurrent-1.json" "$scratch/concurrent-2.json"
cmp "$scratch/concurrent-1.json" "$scratch/concurrent-3.json"
jq -e --arg actor "$actor" '.actorId==$actor and .eventType=="APPLICATION_CONTEXT_CHANGED" and .organizationId==null and .boardId==null and .entityId==.eventId and .metadata=={}' "$scratch/concurrent-1.json" >/dev/null
test "$(observe owner "$actor" "$card_key" "$card_query")" = 200
jq -e --arg card "$card" '.eventType=="CARD_OPENED" and .entityType=="Card" and .entityId==$card and .metadata=={}' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/card-original.json"
test "$(request owner PATCH "/cards/$card" "$(jq -nc --argjson version "$card_version" '{title:"Later navigation Card",version:$version}')")" = 200
test "$(observe owner "$actor" "$card_key" "$card_query")" = 200
cmp "$scratch/card-original.json" "$scratch/response.json"
test "$(observe outsider "$outsider" "$(uuid)" "$card_query")" = 404
test "$(observe owner "$actor" "$(uuid)" "kind=card&organizationId=$org&boardId=$board&cardId=$card&version=$((card_version+2))")" = 404
test "$(request owner POST "/lists/$list/archive" '{"version":1}')" = 200
test "$(observe owner "$actor" "$card_key" "$card_query")" = 404
jq -e '.code=="navigation_unavailable" and (has("eventId")|not)' "$scratch/response.json" >/dev/null
echo 'Exact-image navigation: proxy routing, account binding, canonical originals, concurrent replay, private scope, stale revision and archived-parent refusal passed.'
