#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable retry fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
restore() { admin 'GRANT INSERT ON audit_events TO strataai_api_runtime; DROP TRIGGER IF EXISTS ci_retry_delay ON audit_events; DROP FUNCTION IF EXISTS public.ci_retry_delay();' >/dev/null; }
trap 'restore; rm -rf "$scratch"' EXIT
trap 'echo "Work retry check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
account() {
  local name="$1" email="retry-${RANDOM}-${RANDOM}@example.test"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg email "$email" '{email:$email,password:"retry-correct-horse-battery",displayName:"Retry fixture"}')" "$BASE_URL/auth/register" > "$scratch/$name.json"
  curl --fail --silent --show-error -c "$scratch/$name.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg email "$email" '{email:$email,password:"retry-correct-horse-battery"}')" "$BASE_URL/auth/login" >/dev/null
}
request() { curl --max-time 30 --fail --silent --show-error -b "$scratch/${5:-owner}.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -X "$1" -d "$3" "$BASE_URL$2"; }
rejected() {
  local status
  status="$(curl --max-time 30 --silent --show-error -b "$scratch/${7:-owner}.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -X "$1" -d "$3" -o "$scratch/failure.json" -w '%{http_code}' "$BASE_URL$2")"
  test "$status" = "$5"
  jq -e --arg code "$6" '.code == $code' "$scratch/failure.json" >/dev/null
  ! grep -Eq 'Npgsql|work_command_replays|permission denied|INSERT INTO' "$scratch/failure.json"
}
account owner
account member
owner_id="$(jq -r '.user.id' "$scratch/owner.json")"
member_id="$(jq -r '.user.id' "$scratch/member.json")"
organization="$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Retry Organization"}' "$BASE_URL/organizations" | jq -r '.organization.id')"
other_org="$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Other Retry Organization"}' "$BASE_URL/organizations" | jq -r '.organization.id')"
for id in "$owner_id" "$member_id" "$organization" "$other_org"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
board_key="$(uuid)"
board_body="$(jq -nc --arg org "$organization" '{organizationId:$org,name:"Private retry board"}')"
board="$(request POST /boards "$board_body" "$board_key" | jq -r '.id')"
test "$(request POST /boards "$board_body" "$board_key" | jq -r '.id')" = "$board"
other_board="$(request POST /boards "$(jq -nc --arg org "$other_org" '{organizationId:$org,name:"Other board"}')" "$board_key" | jq -r '.id')"
test "$board" != "$other_board"
[[ "$board" =~ ^[0-9a-fA-F-]{36}$ ]]
rejected PATCH "/boards/$board/members/$member_id" '{"role":"ADMIN"}' "$(uuid)" 400 member_not_eligible
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status,created_at,updated_at) VALUES (gen_random_uuid(),'$organization','$member_id','MEMBER','ACTIVE',now(),now());" >/dev/null
request PATCH "/boards/$board/members/$member_id" '{"role":"ADMIN"}' "$(uuid)" >/dev/null
list_key="$(uuid)"
audit_before="$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization';")"
# Force overlap inside the original mutation before it commits its claim/result.
admin "CREATE FUNCTION public.ci_retry_delay() RETURNS trigger LANGUAGE plpgsql AS \$\$ BEGIN IF NEW.tenant_id='$organization'::uuid THEN PERFORM pg_sleep(0.4); END IF; RETURN NEW; END; \$\$; CREATE TRIGGER ci_retry_delay BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION public.ci_retry_delay();" >/dev/null
request POST "/boards/$board/lists" '{"name":"Exactly one list"}' "$list_key" > "$scratch/first.json" & first=$!
request POST "/boards/$board/lists" '{"name":"Exactly one list"}' "$list_key" > "$scratch/second.json" & second=$!
wait "$first"
wait "$second"
cmp "$scratch/first.json" "$scratch/second.json"
list="$(jq -r '.id' "$scratch/first.json")"
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization';")" = "$((audit_before + 1))"
admin 'DROP TRIGGER ci_retry_delay ON audit_events; DROP FUNCTION public.ci_retry_delay();' >/dev/null
rejected POST "/boards/$board/lists" '{"name":"Changed input"}' "$list_key" 409 idempotency_key_reused
member_list="$(request POST "/boards/$board/lists" '{"name":"Member namespace"}' "$list_key" member | jq -r '.id')"
test "$member_list" != "$list"
card_key="$(uuid)"
card_body='{"title":"Private retry card"}'
# An audit failure rolls back the row, result claim, and domain mutation.
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
rejected POST "/lists/$list/cards" "$card_body" "$card_key" 503 work_storage_unavailable
test "$(admin "SELECT count(*) FROM work_command_replays WHERE tenant_id='$organization' AND key_id='$card_key';")" = 0
test "$(admin "SELECT count(*) FROM cards WHERE tenant_id='$organization' AND list_id='$list';")" = 0
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
card="$(request POST "/lists/$list/cards" "$card_body" "$card_key" | jq -r '.id')"
test "$(request POST "/lists/$list/cards" "$card_body" "$card_key" | jq -r '.id')" = "$card"
edit_key="$(uuid)"
edit='{"title":"Updated exactly once","version":1}'
request PATCH "/cards/$card" "$edit" "$edit_key" > "$scratch/edit.json"
request PATCH "/cards/$card" "$edit" "$edit_key" > "$scratch/edit-replay.json"
cmp "$scratch/edit.json" "$scratch/edit-replay.json"
jq -e '.version == 2' "$scratch/edit-replay.json" >/dev/null
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --force-recreate --wait --wait-timeout 180 api >/dev/null
request PATCH "/cards/$card" "$edit" "$edit_key" > "$scratch/edit-after-restart.json"
cmp "$scratch/edit.json" "$scratch/edit-after-restart.json"
rejected PATCH "/cards/$card" "$edit" "$(uuid)" 409 version_conflict
test "$(admin "SELECT count(*) FROM work_command_replays WHERE tenant_id='$organization' AND result_json IS NULL;")" = 0
# Cross-organization reads are hidden even using real API credentials.
test "$(admin "SET ROLE strataai_api_runtime; SET app.tenant_id='$organization'; SELECT count(*) FROM work_command_replays WHERE tenant_id='$other_org';" | tail -1)" = 0
if admin "SET ROLE strataai_api_runtime; SET app.tenant_id='$organization'; INSERT INTO work_command_replays(tenant_id,actor_id,key_id,fingerprint) VALUES ('$other_org','$owner_id',gen_random_uuid(),repeat('A',64));" >/dev/null 2>&1; then echo 'Cross-organization retry write escaped RLS'; exit 1; fi
test "$(admin "SELECT has_table_privilege('strataai_worker_runtime','work_command_replays','SELECT');")" = f
test "$(admin "SELECT has_table_privilege('strataai_api_runtime','work_command_replays','DELETE');")" = f
# The row survives expiration; the old intent is rejected instead of duplicated.
admin "UPDATE work_command_replays SET created_at=clock_timestamp()-interval '2 days',expires_at=clock_timestamp()-interval '1 day' WHERE tenant_id='$organization' AND key_id='$card_key';" >/dev/null
rejected POST "/lists/$list/cards" "$card_body" "$card_key" 409 idempotency_key_expired
test "$(admin "SELECT count(*) FROM cards WHERE tenant_id='$organization' AND list_id='$list';")" = 1
# Retain the old Board ADMIN row to prove fresh organization eligibility at replay.
admin "UPDATE organization_members SET status='SUSPENDED' WHERE tenant_id='$organization' AND user_id='$member_id';" >/dev/null
rejected POST "/boards/$board/lists" '{"name":"Member namespace"}' "$list_key" 404 board_not_found member
! grep -q 'Member namespace' "$scratch/failure.json"
echo 'Exact release API proves durable duplicate/concurrency, rollback, actor/tenant isolation, expiry, version and revoked-access retry safety.'
