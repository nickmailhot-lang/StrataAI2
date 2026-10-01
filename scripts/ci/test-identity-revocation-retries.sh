#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable receipt fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
pids=()
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'GRANT INSERT ON audit_events,identity_events,identity_revocation_replays TO strataai_api_runtime;' >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Revocation receipt check failed at line $LINENO" >&2' ERR
request() {
  curl --max-time 60 --silent --show-error -b "$scratch/${3:-primary}.cookies" -H 'X-StrataAI-Request: 1' \
    -H "Idempotency-Key: $2" -X POST -D "$scratch/headers" -o "$scratch/response" -w '%{http_code}' "$BASE_URL$1"
}
state() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$user'),
    'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$user'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id='$user' AND tenant_id IS NULL),
    'stream',(SELECT last_sequence FROM identity_event_streams WHERE user_id='$user'),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id='$user'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_revocation_replays r WHERE user_id='$user'))::text;"
}
for operation in LOGOUT DEACTIVATE; do
  path=/auth/logout; other=/me/deactivate
  if test "$operation" = DEACTIVATE; then path=/me/deactivate; other=/auth/logout; fi
  body="$(jq -nc --arg email "receipt-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"receipt-correct-horse-battery",displayName:"Receipt account"}')"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/user"
  user="$(jq -r '.user.id' "$scratch/user")"
  for cookie in primary other; do
    curl --fail --silent --show-error -c "$scratch/$cookie.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
  done
  key="$(cat /proc/sys/kernel/random/uuid)"
  before="$(state)"
  # Failure at each publication boundary must restore session, user, audit, stream and receipt.
  for table in audit_events identity_events identity_revocation_replays; do
    admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
    test "$(request "$path" "$key")" = 503
    jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
    scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
    test "$before" = "$(state)"
    admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
  done
  # Retain the original cookie jar: these simulate lost acknowledgments, not fresh logins.
  for n in 1 2 3; do
    curl --max-time 60 --silent --show-error -b "$scratch/primary.cookies" -H 'X-StrataAI-Request: 1' \
      -H "Idempotency-Key: $key" -X POST -o "$scratch/retry-$n.body" -w '%{http_code}' "$BASE_URL$path" > "$scratch/retry-$n.status" &
    pids+=($!)
  done
  for pid in "${pids[@]}"; do wait "$pid"; done
  pids=()
  for n in 1 2 3; do test "$(cat "$scratch/retry-$n.status")" = 204; test ! -s "$scratch/retry-$n.body"; done
  test "$(admin "SELECT count(*) FROM identity_revocation_replays WHERE user_id='$user' AND key_id='$key' AND operation='$operation';")" = 1
  event=SESSION_REVOKED; version=1
  if test "$operation" = DEACTIVATE; then event=USER_DEACTIVATED; version=2; fi
  test "$(admin "SELECT count(*) FROM identity_events WHERE user_id='$user' AND event_type='$event';")" = 1
  test "$(admin "SELECT version FROM users WHERE id='$user';")" = "$version"
  saved="$(state)"
  test "$(request "$other" "$key")" = 409
  jq -e '.code=="idempotency_key_reused"' "$scratch/response" >/dev/null
  test "$(request "$path" "$key" other)" = 409
  test "$(request "$path" "$(cat /proc/sys/kernel/random/uuid)")" = 401
  for protected in /me /me/sync; do
    test "$(curl --silent --show-error -b "$scratch/primary.cookies" -o /dev/null -w '%{http_code}' "$BASE_URL$protected")" = 401
  done
  test "$saved" = "$(state)"
  # Durable receipt survives an API restart without reopening protected account reads.
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml restart api >/dev/null
  for attempt in $(seq 1 90); do
    if curl --fail --silent "$BASE_URL/readyz" >/dev/null; then break; fi
    sleep 1
  done
  test "$(request "$path" "$key")" = 204
  test ! -s "$scratch/response"
  test "$saved" = "$(state)"
  # The existing Worker purges expired receipts while preserving live receipts and history.
  admin "INSERT INTO identity_revocation_replays(user_id,key_id,session_id,operation,created_at,expires_at)
    SELECT user_id,gen_random_uuid(),session_id,operation,clock_timestamp()-interval '2 days',clock_timestamp()-interval '1 day'
    FROM identity_revocation_replays CROSS JOIN generate_series(1,101) WHERE user_id='$user' AND key_id='$key';" >/dev/null
  for attempt in $(seq 1 90); do
    if test "$(admin "SELECT count(*) FROM identity_revocation_replays WHERE user_id='$user' AND expires_at<=clock_timestamp();")" = 0; then break; fi
    sleep 1
  done
  test "$(admin "SELECT count(*) FROM identity_revocation_replays WHERE user_id='$user' AND expires_at<=clock_timestamp();")" = 0
  test "$saved" = "$(state)"
  # Expiry occurring during the account-lock wait must precede receipt disclosure.
  admin "BEGIN; SELECT id FROM users WHERE id='$user' FOR UPDATE; SELECT pg_sleep(10) /* receipt-expiry-gate */; COMMIT;" > "$scratch/gate" &
  gate=$!; pids+=($gate)
  for attempt in $(seq 1 100); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%receipt-expiry-gate%' AND wait_event='PgSleep';")" = 1; then break; fi
    sleep 0.1
  done
  test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%receipt-expiry-gate%' AND wait_event='PgSleep';")" = 1
  request "$path" "$key" > "$scratch/wait-status" &
  pending=$!; pids+=($pending)
  for attempt in $(seq 1 100); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock';")" -ge 1; then break; fi
    sleep 0.1
  done
  test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock';")" -ge 1
  admin "UPDATE sessions SET expires_at=clock_timestamp()-interval '1 second' WHERE user_id='$user';" >/dev/null
  expired="$(state)"
  wait "$gate"; wait "$pending"; pids=()
  test "$(cat "$scratch/wait-status")" = 401
  test "$(request "$path" "$key")" = 401
  test "$expired" = "$(state)"
done
echo 'Release PostgreSQL revocation receipts: atomic failures, concurrent retries, session-bound acknowledgments and restart passed.'
echo 'Release Worker revocation receipt retention preserves live acknowledgments and account/session/audit/event history.'
