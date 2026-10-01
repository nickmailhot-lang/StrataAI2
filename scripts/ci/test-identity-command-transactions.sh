#!/usr/bin/env bash
set -euo pipefail
# PRD-02/60-TC-05/08: atomic profile/deactivation audit and post-wait session admission.
test "${CI:-}" = true || { echo 'Disposable identity fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
gate_pid=''
request_pid=''
retry_pids=()
small_pool_started=0
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  for pid in "${retry_pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'GRANT INSERT ON audit_events,identity_events,identity_profile_replays TO strataai_api_runtime;' >/dev/null
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
  local retry=()
  if test -n "${4:-}"; then retry=(-H "Idempotency-Key: $4"); fi
  curl --max-time 60 --silent --show-error -b "$scratch/primary.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' "${retry[@]}" -X "$1" -d "$3" -D "$scratch/headers" -o "$scratch/response.json" -w '%{http_code}' "$BASE_URL$2"
}
state() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$user'),
    'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$user'),
    'audits',(SELECT count(*) FROM audit_events WHERE actor_id='$user' AND tenant_id IS NULL),
    'stream',(SELECT last_sequence FROM identity_event_streams WHERE user_id='$user'),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id='$user'),
    'retries',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_profile_replays r WHERE user_id='$user'))::text;"
}
profile_state() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$user'),
    'audits',(SELECT count(*) FROM audit_events WHERE actor_id='$user' AND tenant_id IS NULL),
    'stream',(SELECT last_sequence FROM identity_event_streams WHERE user_id='$user'),
    'events',(SELECT count(*) FROM identity_events WHERE user_id='$user'),
    'retries',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_profile_replays r WHERE user_id='$user'))::text;"
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

# Event publication failure must roll back profile, audit and stream allocation.
admin 'REVOKE INSERT ON identity_events FROM strataai_api_runtime;' >/dev/null
test "$(request PATCH /me '{"displayName":"Event must roll back","version":1}')" = 503
jq -e '.code=="identity_storage_unavailable"' "$scratch/response.json" >/dev/null
test "$before" = "$(state)"
admin 'GRANT INSERT ON identity_events TO strataai_api_runtime;' >/dev/null
curl --fail --silent --show-error -b "$scratch/primary.cookies" "$BASE_URL/me/sync" |
  jq -e '.cursor==1 and .latestSequence==1 and (.events|length)==0 and .profile.version==1' >/dev/null
retry_key="$(cat /proc/sys/kernel/random/uuid)"
saved_body='{"displayName":"Atomic profile saved","locale":"en-CA","timezone":"UTC","version":1}'
# Publication of a retry acknowledgment must share the state/audit/event transaction.
admin 'REVOKE INSERT ON identity_profile_replays FROM strataai_api_runtime;' >/dev/null
test "$(request PATCH /me "$saved_body" "$retry_key")" = 503
jq -e '.code=="identity_storage_unavailable"' "$scratch/response.json" >/dev/null
test "$before" = "$(state)"
admin 'GRANT INSERT ON identity_profile_replays TO strataai_api_runtime;' >/dev/null
# Three concurrent copies of the initial intent commit only once.
retry_pids=()
for n in 1 2 3; do
  curl --max-time 60 --silent --show-error -b "$scratch/primary.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $retry_key" -X PATCH -d "$saved_body" -o "$scratch/retry-$n.json" -w '%{http_code}' "$BASE_URL/me" > "$scratch/retry-$n.status" &
  retry_pids+=($!)
done
for pid in "${retry_pids[@]}"; do wait "$pid"; done
retry_pids=()
for n in 1 2 3; do test "$(cat "$scratch/retry-$n.status")" = 200; cmp "$scratch/retry-1.json" "$scratch/retry-$n.json"; done
cp "$scratch/retry-1.json" "$scratch/response.json"
jq -e '.displayName=="Atomic profile saved" and .version==2 and .locale=="en-CA" and .timezone=="UTC"' "$scratch/response.json" >/dev/null
saved_state="$(state)"
test "$(request PATCH /me '{"displayName":"Different retry intent","version":1}' "$retry_key")" = 409
jq -e '.code=="idempotency_key_reused"' "$scratch/response.json" >/dev/null
test "$saved_state" = "$(state)"
test "$(admin "SELECT count(*) FROM identity_profile_replays WHERE user_id='$user';")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='USER_PROFILE_UPDATED';")" = 1
curl --fail --silent --show-error -b "$scratch/other.cookies" "$BASE_URL/me/sync?after=1" > "$scratch/replay.json"
jq -e --arg subject "$user" '.cursor==2 and .latestSequence==2 and .hasMore==false and .profile.version==2
  and (.events|length)==1 and .events[0].sequence==2 and .events[0].eventType=="USER_PROFILE_UPDATED"
  and .events[0].entityId==$subject and .events[0].actorId==$subject and .events[0].version==2
  and .events[0].metadata=={} and .events[0].organizationId==null and .events[0].boardId==null' "$scratch/replay.json" >/dev/null
# Disposable history fixture exercises bounded continuation without changing profile state.
admin "BEGIN; SELECT id FROM users WHERE id='$user' FOR UPDATE;
  UPDATE identity_event_streams SET last_sequence=103 WHERE user_id='$user';
  INSERT INTO identity_events(event_id,user_id,sequence,actor_id,event_type,entity_id,entity_version,correlation_id)
  SELECT gen_random_uuid(),'$user',n,'$user','SESSION_REVOKED','$user',2,'pagination-fixture' FROM generate_series(3,103) n;
  COMMIT;" >/dev/null
curl --fail --silent --show-error -b "$scratch/primary.cookies" "$BASE_URL/me/sync?after=2" |
  jq -e '.cursor==102 and .latestSequence==103 and .hasMore==true and (.events|length)==100
    and .events[0].sequence==3 and .events[99].sequence==102' >/dev/null
curl --fail --silent --show-error -b "$scratch/primary.cookies" "$BASE_URL/me/sync?after=102" |
  jq -e '.cursor==103 and .latestSequence==103 and .hasMore==false and (.events|length)==1
    and .events[0].sequence==103' >/dev/null
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

for operation in profile deactivate replay profile_retry; do
  before="$(profile_state)"
  hash="$(awk '$6=="strataai_session" {print $7}' "$scratch/primary.cookies" | tr -d '\n' | sha256sum | cut -d ' ' -f 1)"
  hold "SELECT id FROM users WHERE id='$user' FOR UPDATE;"
  if test "$operation" = profile; then
    request PATCH /me '{"displayName":"Logged out profile","version":2}' > "$scratch/status" &
  elif test "$operation" = profile_retry; then
    request PATCH /me "$saved_body" "$retry_key" > "$scratch/status" &
  elif test "$operation" = deactivate; then
    request POST /me/deactivate '{}' > "$scratch/status" &
  else
    request GET /me/sync?after=0 '{}' > "$scratch/status" &
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
test "$(curl --silent --show-error -b "$scratch/primary.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/me/sync?after=0")" = 401
test "$(curl --silent --show-error -b "$scratch/primary.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/me")" = 401
curl --fail --silent --show-error -b "$scratch/other.cookies" "$BASE_URL/me" >/dev/null
login
test "$(request PATCH /me "$saved_body" "$retry_key")" = 200
cmp "$scratch/retry-1.json" "$scratch/response.json"
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='USER_PROFILE_UPDATED';")" = 1
test "$(admin "SELECT count(*) FROM identity_events WHERE user_id='$user' AND event_type='USER_PROFILE_UPDATED';")" = 1
test "$(request PATCH /me '{"displayName":"Single connection profile","version":2}')" = 200
jq -e '.version==3' "$scratch/response.json" >/dev/null
# A replay remains the original ACK after later edits; expired keys cease to replay it.
test "$(request PATCH /me "$saved_body" "$retry_key")" = 200
cmp "$scratch/retry-1.json" "$scratch/response.json"
admin "UPDATE identity_profile_replays SET created_at=clock_timestamp()-interval '2 days',expires_at=clock_timestamp()-interval '1 day' WHERE user_id='$user' AND key_id='$retry_key';" >/dev/null
test "$(request PATCH /me "$saved_body" "$retry_key")" = 409
jq -e '.code=="version_conflict"' "$scratch/response.json" >/dev/null
test "$(request PATCH /me '{"displayName":"Expired key new intent","version":3}' "$retry_key")" = 200
jq -e '.version==4 and .displayName=="Expired key new intent"' "$scratch/response.json" >/dev/null
test "$(admin "SELECT count(*) FROM identity_profile_replays WHERE user_id='$user' AND key_id='$retry_key' AND expires_at>clock_timestamp() AND result_json->>'Version'='4';")" = 1
test "$(request POST /me/deactivate '{}')" = 204
test "$(admin "SELECT status||':'||version FROM users WHERE id='$user';")" = 'DEACTIVATED:5'
test "$(admin "SELECT count(*) FROM sessions WHERE user_id='$user' AND revoked_at IS NULL;")" = 0
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='USER_DEACTIVATED';")" = 1
test "$(admin "SELECT count(*) FROM identity_events WHERE user_id='$user' AND event_type='USER_DEACTIVATED' AND entity_version=5;")" = 1
for cookie in primary other; do
  test "$(curl --silent --show-error -b "$scratch/$cookie.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/me")" = 401
done
echo 'Identity sign-in/logout/profile/deactivation audits are atomic; post-wait checks and one-connection execution succeed.'
