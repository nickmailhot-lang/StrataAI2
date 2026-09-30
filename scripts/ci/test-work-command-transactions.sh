#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable transaction fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
small_pool_started=0
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
restore() {
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime; DROP TRIGGER IF EXISTS ci_work_delay ON audit_events; DROP FUNCTION IF EXISTS public.ci_work_command_delay();' >/dev/null
  if [ "$small_pool_started" = 1 ]; then
    docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
  fi
}
trap 'restore; rm -rf "$scratch"' EXIT
trap 'echo "Work transaction check failed at line $LINENO" >&2' ERR
email="atomic-work-${RANDOM}-${RANDOM}@example.test"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" '{email:$email,password:"atomic-work-correct-horse-battery",displayName:"Transaction fixture"}')" "$BASE_URL/auth/register" > "$scratch/user.json"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -c "$scratch/cookies" \
  -d "$(jq -nc --arg email "$email" '{email:$email,password:"atomic-work-correct-horse-battery"}')" "$BASE_URL/auth/login" >/dev/null
request() { curl --max-time 20 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X "$1" -d "$3" "$BASE_URL$2"; }
snapshot() { curl --fail --silent --show-error -b "$scratch/cookies" "$BASE_URL/boards/$board_a"; }
organization_a="$(request POST /organizations '{"name":"Atomic Organization A"}' | jq -r '.organization.id')"
organization_b="$(request POST /organizations '{"name":"Atomic Organization B"}' | jq -r '.organization.id')"
board_a="$(request POST /boards "$(jq -nc --arg org "$organization_a" '{organizationId:$org,name:"Atomic board A"}')" | jq -r '.id')"
board_b="$(request POST /boards "$(jq -nc --arg org "$organization_b" '{organizationId:$org,name:"Atomic board B"}')" | jq -r '.id')"
list_a="$(request POST "/boards/$board_a/lists" '{"name":"Source"}' | jq -r '.id')"
destination="$(request POST "/boards/$board_a/lists" '{"name":"Destination"}' | jq -r '.id')"
card="$(request POST "/lists/$list_a/cards" '{"title":"Original title"}')"
card_id="$(printf '%s' "$card" | jq -r '.id')"
user_id="$(jq -r '.user.id' "$scratch/user.json")"
rank="$(printf '%s' "$card" | jq -r '.rank')"
for id in "$organization_a" "$organization_b" "$board_a" "$board_b" "$list_a" "$destination" "$card_id" "$user_id"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
audit_before="$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization_a';")"
[[ "$audit_before" =~ ^[0-9]+$ ]]
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
rejected() {
  local status
  status="$(curl --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X "$1" -d "$3" -o "$scratch/failure.json" -w '%{http_code}' "$BASE_URL$2")"
  test "$status" = 503
  jq -e '.code == "work_storage_unavailable" and .status == 503' "$scratch/failure.json" >/dev/null
  ! grep -Eq 'audit_events|Npgsql|permission denied|INSERT INTO' "$scratch/failure.json"
}
rejected PATCH "/cards/$card_id" '{"title":"Must roll back","description":"Must roll back","version":1}'
snapshot | jq -e --arg id "$card_id" '[.lists[].cards[] | select(.id == $id)] | length == 1 and .[0].title == "Original title" and .[0].version == 1' >/dev/null
rejected POST "/lists/$list_a/cards" '{"title":"Must not exist"}'
snapshot | jq -e '[.lists[].cards[]] | length == 1' >/dev/null
rejected POST "/boards/$board_a/lists" '{"name":"Must not exist"}'
snapshot | jq -e '.lists | length == 2' >/dev/null
rejected POST /boards "$(jq -nc --arg org "$organization_a" '{organizationId:$org,name:"Must not exist"}')"
curl --fail --silent --show-error -b "$scratch/cookies" "$BASE_URL/organizations/$organization_a/boards" | jq -e 'length == 1' >/dev/null
rejected POST "/boards/$board_a/archive" '{"version":1}'
snapshot | jq -e '.board.lifecycleState == "active" and .board.version == 1' >/dev/null
rejected POST "/cards/$card_id/move" "$(jq -nc --arg destination "$destination" --arg rank "$rank" '{destinationListId:$destination,rank:$rank,expectedVersion:1}')"
snapshot | jq -e --arg list "$list_a" --arg card "$card_id" '[.lists[] | select(.list.id == $list) | .cards[] | select(.id == $card)] | length == 1 and .[0].version == 1' >/dev/null
rejected POST "/cards/$card_id/archive" '{"version":1}'
snapshot | jq -e '[.lists[].cards[]] | length == 1' >/dev/null
rejected PATCH "/boards/$board_a/members/$user_id" '{"role":"MEMBER"}'
rejected DELETE "/boards/$board_a/members/$user_id" '{}'
curl --fail --silent --show-error -b "$scratch/cookies" "$BASE_URL/boards/$board_a/members" | jq -e --arg user "$user_id" '[.[] | select(.userId == $user)] | length == 1 and .[0].role == "ADMIN" and .[0].version == 1' >/dev/null
audit_after="$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization_a';")"
test "$audit_before" = "$audit_after"
restore
request PATCH "/cards/$card_id" '{"title":"Committed with audit","description":"Saved","version":1}' | jq -e '.version == 2' >/dev/null
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization_a';")" = "$((audit_before + 1))"
status="$(curl --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X PATCH -d '{"title":"Stale overwrite","version":1}' -o /dev/null -w '%{http_code}' "$BASE_URL/cards/$card_id")"
test "$status" = 409
snapshot | jq -e --arg id "$card_id" '[.lists[].cards[] | select(.id == $id)] | .[0].title == "Committed with audit" and .[0].version == 2' >/dev/null
# Concurrent different-Organization commands must never share an ambient session.
admin "CREATE FUNCTION public.ci_work_command_delay() RETURNS trigger LANGUAGE plpgsql AS \$\$ BEGIN IF NEW.tenant_id IN ('$organization_a'::uuid, '$organization_b'::uuid) THEN PERFORM pg_sleep(0.4); END IF; RETURN NEW; END; \$\$; CREATE TRIGGER ci_work_delay BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION public.ci_work_command_delay();" >/dev/null
request POST "/boards/$board_a/lists" '{"name":"Concurrent A"}' > "$scratch/a.json" &
pid_a=$!
request POST "/boards/$board_b/lists" '{"name":"Concurrent B"}' > "$scratch/b.json" &
pid_b=$!
wait "$pid_a"
wait "$pid_b"
jq -e --arg org "$organization_a" '.organizationId == $org and .name == "Concurrent A"' "$scratch/a.json" >/dev/null
jq -e --arg org "$organization_b" '.organizationId == $org and .name == "Concurrent B"' "$scratch/b.json" >/dev/null
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization_a';")" = "$((audit_before + 2))"
small_pool_started=1
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
request POST "/boards/$board_a/lists" '{"name":"Single connection command"}' | jq -e '.name == "Single connection command"' >/dev/null
request PATCH "/cards/$card_id" '{"title":"Single connection edit","version":2}' | jq -e '.version == 3' >/dev/null
echo 'Work mutations and audits commit together; audit failures, conflicts and concurrent Organization scopes are isolated.'
