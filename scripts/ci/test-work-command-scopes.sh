#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable write-scope fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
gate_pid=''
request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'DROP TRIGGER IF EXISTS ci_scope_hold ON audit_events; DROP FUNCTION IF EXISTS public.ci_scope_hold();' >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Work write-scope check failed at line $LINENO" >&2' ERR
account() {
  local label="$1" body
  body="$(jq -nc --arg email "scope-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"scope-correct-horse-battery",displayName:"Scope fixture"}')"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/$label.json"
  curl --fail --silent --show-error -c "$scratch/$label.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
}
request() {
  curl --max-time 60 --fail --silent --show-error -b "$scratch/${4:-owner}.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X "$1" -d "$3" "$BASE_URL$2"
}
account owner
account member
member="$(jq -r '.user.id' "$scratch/member.json")"
organization="$(request POST /organizations '{"name":"Write scope fixture"}' | jq -r '.organization.id')"
board="$(request POST /boards "$(jq -nc --arg org "$organization" '{organizationId:$org,name:"Protected scope board"}')" | jq -r '.id')"
list="$(request POST "/boards/$board/lists" '{"name":"Protected scope list"}' | jq -r '.id')"
destination="$(request POST "/boards/$board/lists" '{"name":"Active destination"}' | jq -r '.id')"
card="$(request POST "/lists/$list/cards" '{"title":"Protected scope card"}' | jq -r '.id')"
for id in "$member" "$organization" "$board" "$list" "$destination" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$organization','$member','MEMBER','ACTIVE');" >/dev/null
request PATCH "/boards/$board/members/$member" '{"role":"MEMBER"}' >/dev/null

protected_state() {
  # Parent lifecycle and actor grants are deliberately changed by the fixture.
  # Child contents, ordering, versions, counts and all command effects must agree.
  admin "SELECT json_build_object(
    'boards',(SELECT json_agg(json_build_array(id,name,description) ORDER BY id) FROM boards WHERE tenant_id='$organization'),
    'lists',(SELECT json_agg(json_build_array(id,name,rank) ORDER BY id) FROM board_lists WHERE tenant_id='$organization'),
    'cards',(SELECT json_agg(row_to_json(c) ORDER BY id) FROM cards c WHERE tenant_id='$organization'),
    'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$organization'),
    'events',(SELECT count(*) FROM work_events WHERE tenant_id='$organization'),
    'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$organization'),
    'replays',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$organization'))::text;"
}
hold() {
  local sql="$1"
  rm -f "$scratch/gate.in" "$scratch/gate.log"
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 &
  gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\n%s\n\\echo scope_locked\n' "$sql" >&3
  for ((attempt=0; attempt<100; attempt++)); do
    if grep -q '^scope_locked$' "$scratch/gate.log"; then return; fi
    kill -0 "$gate_pid" || { cat "$scratch/gate.log" >&2; return 1; }
    sleep 0.05
  done
  cat "$scratch/gate.log" >&2
  return 1
}
release() {
  printf '%s\nCOMMIT;\n\\q\n' "$1" >&3
  exec 3>&-
  wait "$gate_pid"
  gate_pid=''
}
blocked() {
  local condition="$1" count
  for ((attempt=0; attempt<100; attempt++)); do
    count="$(admin "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type='Lock' AND $condition;")" || return 1
    [[ "$count" =~ ^[0-9]+$ ]] || return 1
    if ((count > 0)); then return; fi
    sleep 0.05
  done
  echo 'Expected live database lock wait was not observed.' >&2
  return 1
}
case_denied() {
  local lock_sql="$1" change_sql="$2" lock_query="$3" method="$4" route="$5" body="$6" code="$7" actor="$8" key="$9"
  local before
  before="$(protected_state)"
  hold "$lock_sql"
  local headers=()
  if test "$key" = keyed; then headers=(-H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)"); fi
  curl --max-time 60 --silent --show-error -b "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' "${headers[@]}" -X "$method" -d "$body" -o "$scratch/failure.json" -w '%{http_code}' "$BASE_URL$route" > "$scratch/status" &
  request_pid=$!
  blocked "usename='strataai_api_runtime' AND query LIKE '$lock_query'"
  release "$change_sql"
  wait "$request_pid"
  request_pid=''
  test "$(cat "$scratch/status")" = 404
  jq -e --arg code "$code" '.code==$code' "$scratch/failure.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Protected|Npgsql|SELECT|FOR SHARE|FOR UPDATE' "$scratch/failure.json"
  test "$before" = "$(protected_state)"
}
board_lock="SELECT id FROM boards WHERE id='$board' FOR UPDATE;"
board_query='%SELECT id FROM boards%FOR UPDATE%'
case_denied "$board_lock" "UPDATE boards SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$board';" "$board_query" POST "/boards/$board/lists" '{"name":"Denied"}' board_not_found member unkeyed
admin "UPDATE boards SET lifecycle_state='ACTIVE' WHERE id='$board';" >/dev/null
case_denied "$board_lock" "UPDATE board_lists SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$list';" "$board_query" POST "/lists/$list/cards" '{"title":"Denied"}' list_not_found member keyed
admin "UPDATE board_lists SET lifecycle_state='ACTIVE' WHERE id='$list';" >/dev/null
case_denied "$board_lock" "UPDATE board_lists SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$list';" "$board_query" PATCH "/cards/$card" '{"title":"Denied","version":1}' card_not_found member keyed
admin "UPDATE board_lists SET lifecycle_state='ACTIVE' WHERE id='$list';" >/dev/null
case_denied "$board_lock" "UPDATE board_lists SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$list';" "$board_query" POST "/cards/$card/move" "$(jq -nc --arg dest "$destination" '{destinationListId:$dest,rank:"500000000000000000000000000000",expectedVersion:1}')" card_not_found member unkeyed
admin "UPDATE board_lists SET lifecycle_state='ACTIVE' WHERE id='$list'; UPDATE cards SET lifecycle_state='ARCHIVED',version=2 WHERE id='$card';" >/dev/null
case_denied "$board_lock" "UPDATE board_lists SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$list';" "$board_query" POST "/cards/$card/restore" '{"version":2}' card_not_found member unkeyed
admin "UPDATE board_lists SET lifecycle_state='ACTIVE' WHERE id='$list';" >/dev/null
case_denied "$board_lock" '' "$board_query" DELETE "/cards/$card?version=2" '{}' card_not_found member keyed
admin "UPDATE cards SET lifecycle_state='ACTIVE',version=1 WHERE id='$card';" >/dev/null
case_denied "SELECT id FROM organization_members WHERE tenant_id='$organization' AND user_id='$member' FOR UPDATE;" "UPDATE organization_members SET status='SUSPENDED',version=version+1 WHERE tenant_id='$organization' AND user_id='$member';" '%SELECT id FROM organization_members%FOR SHARE%' POST "/boards/$board/lists" '{"name":"Denied"}' board_not_found member keyed
admin "UPDATE organization_members SET status='ACTIVE',version=version+1,updated_at=clock_timestamp() WHERE tenant_id='$organization' AND user_id='$member';" >/dev/null
case_denied "$board_lock" "UPDATE board_members SET status='REMOVED',version=version+1 WHERE board_id='$board' AND user_id='$member';" "$board_query" PATCH "/cards/$card" '{"title":"Denied","version":1}' card_not_found member unkeyed
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
org_lock="SELECT id FROM organizations WHERE id='$organization' FOR UPDATE;"
org_query='%SELECT id FROM organizations%FOR SHARE%'
case_denied "$org_lock" "UPDATE organizations SET status='ARCHIVED',version=version+1 WHERE id='$organization';" "$org_query" POST "/boards/$board/lists" '{"name":"Denied"}' board_not_found owner keyed
curl --fail --silent --show-error -b "$scratch/owner.cookies" "$BASE_URL/boards/$board" | jq -e '(.access.canEdit|not) and (.access.canAdminister|not) and (.access.canMove|not)' >/dev/null
# Archived Organizations retain authorized snapshot viewing with frozen edits.
# Snapshot reads use their own locked admission, never write admission.
curl --fail --silent --show-error -b "$scratch/member.cookies" "$BASE_URL/boards/$board" | jq -e '.access.canView and (.access.canEdit|not) and (.access.canAdminister|not) and (.access.canMove|not)' >/dev/null
case_denied "$board_lock" "UPDATE board_members SET status='REMOVED',version=version+1 WHERE board_id='$board' AND user_id='$member';" "$board_query" GET "/boards/$board" '{}' board_not_found member unkeyed
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
case_denied "$org_lock" "UPDATE organizations SET status='DELETING',version=version+1 WHERE id='$organization';" "$org_query" GET "/boards/$board" '{}' board_not_found owner unkeyed
admin "UPDATE organizations SET status='ACTIVE' WHERE id='$organization';" >/dev/null
case_denied "$org_lock" "UPDATE organizations SET status='DELETING',version=version+1 WHERE id='$organization';" "$org_query" POST /boards "$(jq -nc --arg org "$organization" '{organizationId:$org,name:"Denied"}')" organization_not_found owner unkeyed
test "$(curl --silent --show-error -b "$scratch/owner.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/boards/$board/sync")" = 404
admin "UPDATE organizations SET status='ACTIVE' WHERE id='$organization';" >/dev/null

login_member() {
  local body
  body="$(jq -nc --arg email "$(jq -r '.user.email' "$scratch/member.json")" '{email:$email,password:"scope-correct-horse-battery"}')"
  curl --fail --silent --show-error -c "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
}
member_hash() {
  awk '$6=="strataai_session" {print $7}' "$scratch/member.cookies" | tr -d '\n' | sha256sum | cut -d ' ' -f 1
}
session_denied() {
  local change="$1" method="$2" route="$3" body="$4" key="${5:-}" before
  before="$(protected_state)"
  hold "$org_lock"
  local headers=()
  if test -n "$key"; then headers=(-H "Idempotency-Key: $key"); fi
  curl --max-time 60 --silent --show-error -b "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' "${headers[@]}" -X "$method" -d "$body" -o "$scratch/failure.json" -w '%{http_code}' "$BASE_URL$route" > "$scratch/status" &
  request_pid=$!
  blocked "usename='strataai_api_runtime' AND query LIKE '$org_query'"
  release "$change"
  wait "$request_pid"
  request_pid=''
  test "$(cat "$scratch/status")" = 401
  jq -e '.code=="session_unavailable"' "$scratch/failure.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Protected|Npgsql|SELECT|token_hash|strataai_session' "$scratch/failure.json"
  test "$before" = "$(protected_state)"
}
session_denied "UPDATE sessions SET revoked_at=clock_timestamp() WHERE token_hash='$(member_hash)';" POST "/boards/$board/lists" '{"name":"Logged out write"}'
login_member
cached_key="$(cat /proc/sys/kernel/random/uuid)"
curl --fail --silent --show-error -b "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H "Idempotency-Key: $cached_key" -H 'Content-Type: application/json' -d '{"name":"Session replay"}' "$BASE_URL/boards/$board/lists" | jq -e '.name=="Session replay"' >/dev/null
session_denied "UPDATE sessions SET revoked_at=clock_timestamp() WHERE token_hash='$(member_hash)';" POST "/boards/$board/lists" '{"name":"Session replay"}' "$cached_key"
login_member
session_denied "UPDATE sessions SET expires_at=clock_timestamp()-interval '1 second' WHERE token_hash='$(member_hash)';" PATCH "/cards/$card" '{"title":"Expired write","version":1}'
login_member
session_denied "UPDATE users SET status='DEACTIVATED' WHERE id='$member'; UPDATE sessions SET revoked_at=clock_timestamp() WHERE user_id='$member';" PATCH "/cards/$card" '{"title":"Deactivated write","version":1}'
admin "UPDATE users SET status='ACTIVE' WHERE id='$member';" >/dev/null
login_member

# Once authorization is locked, a revocation must wait for the accepted command's
# commit. Pause audit insertion to observe that ordering in the opposite direction.
advisory="$((1000000000 + RANDOM))"
admin "CREATE FUNCTION public.ci_scope_hold() RETURNS trigger LANGUAGE plpgsql AS \$\$ BEGIN IF NEW.tenant_id='$organization'::uuid AND NEW.correlation_id='ci-scope-held' THEN PERFORM pg_advisory_xact_lock($advisory); END IF; RETURN NEW; END; \$\$; CREATE TRIGGER ci_scope_hold BEFORE INSERT ON audit_events FOR EACH ROW EXECUTE FUNCTION public.ci_scope_hold();" >/dev/null
hold "SELECT pg_advisory_xact_lock($advisory);"
curl --max-time 60 --silent --show-error -b "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'X-Correlation-ID: ci-scope-held' -H 'Content-Type: application/json' -X PATCH -d '{"title":"Accepted before revocation","version":1}' -o "$scratch/accepted.json" -w '%{http_code}' "$BASE_URL/cards/$card" > "$scratch/accepted.status" &
request_pid=$!
blocked "usename='strataai_api_runtime' AND wait_event='advisory' AND query LIKE '%INSERT INTO audit_events%'"
admin "SET application_name='ci-scope-revoker'; UPDATE organization_members SET status='SUSPENDED',version=version+1 WHERE tenant_id='$organization' AND user_id='$member';" > "$scratch/revoked" &
revoker_pid=$!
blocked "application_name='ci-scope-revoker' AND query LIKE '%UPDATE organization_members%'"
accepted_hash="$(member_hash)"
admin "SET application_name='ci-session-revoker'; UPDATE sessions SET revoked_at=clock_timestamp() WHERE token_hash='$accepted_hash';" > "$scratch/session-revoked" &
session_revoker_pid=$!
blocked "application_name='ci-session-revoker' AND query LIKE '%UPDATE sessions%'"
test "$(admin "SELECT status FROM organization_members WHERE tenant_id='$organization' AND user_id='$member';")" = ACTIVE
release ''
wait "$request_pid"
request_pid=''
wait "$revoker_pid"
wait "$session_revoker_pid"
test "$(cat "$scratch/accepted.status")" = 200
jq -e '.version==2 and .title=="Accepted before revocation"' "$scratch/accepted.json" >/dev/null
test "$(admin "SELECT status FROM organization_members WHERE tenant_id='$organization' AND user_id='$member';")" = SUSPENDED
test "$(admin "SELECT revoked_at IS NOT NULL FROM sessions WHERE token_hash='$accepted_hash';")" = t
test "$(curl --silent --show-error -b "$scratch/member.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/boards/$board")" = 404
test "$(curl --silent --show-error -b "$scratch/member.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/me")" = 401
echo 'Fresh write authorization after live lock waits, inactive parents, and commit-before-revocation ordering passed.'
