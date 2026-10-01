#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable rank fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Rank allocation check failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
body="$(jq -nc --arg email "rank-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"rank-correct-horse-battery",displayName:"Rank fixture"}')"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" >/dev/null
curl --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
request() { curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" -X POST -d "$2" "$BASE_URL$1"; }
organization="$(request /organizations '{"name":"Rank fixture"}' | jq -r '.organization.id')"
board="$(request /boards "$(jq -nc --arg org "$organization" '{organizationId:$org,name:"Rank capacity"}')" | jq -r '.id')"
[[ "$organization" =~ ^[0-9a-fA-F-]{36}$ && "$board" =~ ^[0-9a-fA-F-]{36}$ ]]
# Independent commands, rather than retries of one key, contend on the parent.
for ((batch=0; batch<8; batch++)); do
  pids=()
  for ((index=0; index<16; index++)); do
    request "/boards/$board/lists" '{"name":"Concurrent list"}' > "$scratch/list-$batch-$index.json" &
    pids+=("$!")
  done
  for pid in "${pids[@]}"; do wait "$pid"; done
done
jq -s -e 'length==128 and ([.[].rank]|unique|length)==128 and all(.[];.rank|test("^[0-9]{30}$"))' "$scratch"/list-*.json >/dev/null
list="$(jq -r '.id' "$scratch/list-0-0.json")"
for ((batch=0; batch<8; batch++)); do
  pids=()
  for ((index=0; index<16; index++)); do
    request "/lists/$list/cards" '{"title":"Concurrent card"}' > "$scratch/card-$batch-$index.json" &
    pids+=("$!")
  done
  for pid in "${pids[@]}"; do wait "$pid"; done
done
jq -s -e 'length==128 and ([.[].rank]|unique|length)==128' "$scratch"/card-*.json >/dev/null
# Populate the scale boundary without thousands of network round trips. These
# fixtures test the production append query on a full group, not API throughput.
admin "INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,created_at,updated_at,version)
SELECT gen_random_uuid(),'$organization','$board','$list','Scale fixture',repeat('x',1024),
lpad((500000000000000000000000000000::numeric+i*1000000000000000000::numeric)::text,30,'0'),'ACTIVE',now(),now(),1
FROM generate_series(129,5000) i;" >/dev/null
request "/lists/$list/cards" '{"title":"After five thousand"}' > "$scratch/last.json"
test "$(jq -r '.rank' "$scratch/last.json")" = '500000005001000000000000000000'
test "$(admin "SELECT count(*)=5001 AND count(DISTINCT rank)=5001 FROM cards WHERE tenant_id='$organization' AND list_id='$list';")" = t
echo 'Concurrent default allocation and append on a 5,000-card PostgreSQL fixture passed.'
# Rank-free moves allocate under the destination parent lock after a fresh read.
source="$(jq -r '.id' "$scratch/list-0-1.json")"
for ((index=0; index<16; index++)); do
  request "/lists/$source/cards" '{"title":"Concurrent append move"}' > "$scratch/move-source-$index.json"
done
move_request() { curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $3" -X POST -d "$2" "$BASE_URL$1"; }
pids=()
for ((index=0; index<16; index++)); do
  card="$(jq -r '.id' "$scratch/move-source-$index.json")"
  cat /proc/sys/kernel/random/uuid > "$scratch/move-key-$index"
  move_request "/cards/$card/move" "$(jq -nc --arg list "$list" '{destinationListId:$list,expectedVersion:1}')" "$(cat "$scratch/move-key-$index")" > "$scratch/move-result-$index.json" &
  pids+=("$!")
done
for pid in "${pids[@]}"; do wait "$pid"; done
jq -s -e --arg list "$list" 'length==16 and ([.[].rank]|unique|length)==16 and all(.[];.listId==$list and .version==2 and .rank>"500000005001000000000000000000")' "$scratch"/move-result-*.json >/dev/null
test "$(admin "SELECT count(*)=5017 AND count(DISTINCT rank)=5017 AND max(rank)='500000005017000000000000000000' FROM cards WHERE tenant_id='$organization' AND list_id='$list';")" = t
# A durable append receipt returns its first allocated rank after a later move,
# without appending again or restoring its former destination.
card="$(jq -r '.id' "$scratch/move-source-0.json")"
request "/cards/$card/move" "$(jq -nc --arg list "$source" '{destinationListId:$list,expectedVersion:2}')" > "$scratch/later-move.json"
jq -e --arg list "$source" '.listId==$list and .version==3' "$scratch/later-move.json" >/dev/null
move_request "/cards/$card/move" "$(jq -nc --arg list "$list" '{destinationListId:$list,expectedVersion:1}')" "$(cat "$scratch/move-key-0")" > "$scratch/replayed-move.json"
test "$(jq -r '.rank' "$scratch/replayed-move.json")" = "$(jq -r '.rank' "$scratch/move-result-0.json")"
jq -e --arg list "$list" '.listId==$list and .version==2' "$scratch/replayed-move.json" >/dev/null
test "$(admin "SELECT list_id='$source' AND version=3 FROM cards WHERE tenant_id='$organization' AND id='$card';")" = t
echo 'Concurrent rank-free moves on a 5,000-card destination and non-reapplying durable replay passed.'
