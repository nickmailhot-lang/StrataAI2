#!/usr/bin/env bash
set -euo pipefail
# PRD-02/60-TC-05/08: atomic profile/deactivation audit and post-wait session admission.
test "${CI:-}" = true || { echo 'Disposable identity fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
gate_pid=''
request_pid=''
small_pool_started=0
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
  if test "$small_pool_started" = 1; then
    docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Identity command check failed at line $LINENO" >&2' ERR
body="$(jq -nc --arg email "identity-atomic-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"identity-atomic-correct-horse-battery",displayName:"Atomic account"}')"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/user.json"
user="$(jq -r '.user.id' "$scratch/user.json")"
[[ "$user" =~ ^[0-9a-fA-F-]{36}$ ]]
login() {
  curl --fail --silent --show-error -c "$scratch/${1:-primary}.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
}
request() {
  curl --max-time 60 --silent --show-error -b "$scratch/primary.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X "$1" -d "$3" -D "$scratch/headers" -o "$scratch/response.json" -w '%{http_code}' "$BASE_URL$2"
}
state() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$user'),
    'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$user'),
    'audits',(SELECT count(*) FROM audit_events WHERE actor_id='$user' AND tenant_id IS NULL))::text;"
}
profile_state() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$user'),
    'audits',(SELECT count(*) FROM audit_events WHERE actor_id='$user' AND tenant_id IS NULL))::text;"
}
login
login other
before="$(state)"
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
for operation in profile deactivate login logout; do
  if test "$operation" = profile; then status="$(request PATCH /me '{"displayName":"Must roll back","version":1}')"
  elif test "$operation" = deactivate; then status="$(request POST /me/deactivate '{}')"
  elif test "$operation" = login; then status="$(request POST /auth/login "$body")"
  else status="$(request POST /auth/logout '{}')"; fi
  test "$status" = 503
  jq -e '.code=="identity_storage_unavailable" and .status==503' "$scratch/response.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Atomic account|Npgsql|audit_events|permission denied|UPDATE users|INSERT INTO' "$scratch/response.json"
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  test "$before" = "$(state)"
done
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$(request PATCH /me '{"displayName":"Atomic profile saved","locale":"en-CA","timezone":"UTC","version":1}')" = 200
jq -e '.displayName=="Atomic profile saved" and .version==2 and .locale=="en-CA" and .timezone=="UTC"' "$scratch/response.json" >/dev/null
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='USER_PROFILE_UPDATED';")" = 1
before="$(state)"
test "$(request PATCH /me '{"displayName":"Stale profile","version":1}')" = 409
test "$before" = "$(state)"
curl --fail --silent --show-error -b "$scratch/other.cookies" "$BASE_URL/me" | jq -e '.displayName=="Atomic profile saved" and .version==2' >/dev/null

# Controlled transaction helpers are defined below before admission checks.
hold() {
  rm -f "$scratch/gate.in" "$scratch/gate.log"
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 &
  gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\n%s\n\\echo org_locked\n' "$1" >&3
  for ((attempt=0; attempt<100; attempt++)); do
    if grep -q '^org_locked$' "$scratch/gate.log"; then return; fi
    kill -0 "$gate_pid" || return 1
    sleep 0.05
  done
  return 1
}
release() {
  printf '%s\nCOMMIT;\n\\q\n' "$1" >&3
  exec 3>&-
  wait "$gate_pid"
  gate_pid=''
}
blocked() {
  local count
  for ((attempt=0; attempt<100; attempt++)); do
    count="$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '$1';")" || return 1
    [[ "$count" =~ ^[0-9]+$ ]] || return 1
    if ((count >= ${2:-1})); then return; fi
    sleep 0.05
  done
  echo 'Expected organization lock wait was not observed.' >&2
  return 1
}

for operation in profile deactivate; do
  before="$(profile_state)"
  hash="$(awk '$6=="strataai_session" {print $7}' "$scratch/primary.cookies" | tr -d '\n' | sha256sum | cut -d ' ' -f 1)"
  hold "SELECT id FROM users WHERE id='$user' FOR UPDATE;"
  if test "$operation" = profile; then
    request PATCH /me '{"displayName":"Logged out profile","version":2}' > "$scratch/status" &
  else
    request POST /me/deactivate '{}' > "$scratch/status" &
  fi
  request_pid=$!
  blocked '%SELECT id FROM users%FOR UPDATE%'
  release "UPDATE sessions SET revoked_at=clock_timestamp() WHERE token_hash='$hash';"
  wait "$request_pid"
  request_pid=''
  test "$(cat "$scratch/status")" = 401
  jq -e '.code=="session_unavailable"' "$scratch/response.json" >/dev/null
  test "$before" = "$(profile_state)"
  test "$(curl --silent --show-error -b "$scratch/primary.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/me")" = 401
  curl --fail --silent --show-error -b "$scratch/other.cookies" "$BASE_URL/me" | jq -e '.version==2' >/dev/null
  login
done
# Sign-in must check the account again after waiting, before issuing a session.
before="$(admin "SELECT jsonb_build_object('sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$user'),'audits',(SELECT count(*) FROM audit_events WHERE actor_id='$user'))::text;")"
hold "SELECT id FROM users WHERE id='$user' FOR UPDATE;"
request POST /auth/login "$body" > "$scratch/status" &
request_pid=$!
blocked '%SELECT%FROM users WHERE email_normalized%FOR UPDATE%'
release "UPDATE users SET status='DEACTIVATED' WHERE id='$user';"
wait "$request_pid"
request_pid=''
test "$(cat "$scratch/status")" = 401
jq -e '.code=="account_unavailable"' "$scratch/response.json" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
test "$before" = "$(admin "SELECT jsonb_build_object('sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$user'),'audits',(SELECT count(*) FROM audit_events WHERE actor_id='$user'))::text;")"
admin "UPDATE users SET status='ACTIVE' WHERE id='$user';" >/dev/null

# Borrowed reads, writes and audit must work even with exactly one API connection.
small_pool_started=1
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
login
test "$(request POST /auth/logout '{}')" = 204
test "$(curl --silent --show-error -b "$scratch/primary.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/me")" = 401
curl --fail --silent --show-error -b "$scratch/other.cookies" "$BASE_URL/me" >/dev/null
login
test "$(request PATCH /me '{"displayName":"Single connection profile","version":2}')" = 200
jq -e '.version==3' "$scratch/response.json" >/dev/null
test "$(request POST /me/deactivate '{}')" = 204
test "$(admin "SELECT status||':'||version FROM users WHERE id='$user';")" = 'DEACTIVATED:4'
test "$(admin "SELECT count(*) FROM sessions WHERE user_id='$user' AND revoked_at IS NULL;")" = 0
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='USER_DEACTIVATED';")" = 1
for cookie in primary other; do
  test "$(curl --silent --show-error -b "$scratch/$cookie.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/me")" = 401
done
echo 'Identity sign-in/logout/profile/deactivation audits are atomic; post-wait checks and one-connection execution succeed.'
