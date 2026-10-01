#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable sign-in fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
pids=()
original_version="${STRATAAI_AUTH_RETRY_CURRENT_KEY:?Runtime key version required}"
original_ring="${STRATAAI_AUTH_RETRY_KEYS:?Runtime key ring required}"
rotated=false
small_pool=false
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'GRANT INSERT ON audit_events,identity_login_replays TO strataai_api_runtime;' >/dev/null
  if test "$rotated" = true || test "$small_pool" = true; then
    STRATAAI_AUTH_RETRY_CURRENT_KEY="$original_version" STRATAAI_AUTH_RETRY_KEYS="$original_ring" \
      docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Sign-in retry check failed at line $LINENO" >&2' ERR
small_pool=true
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
body="$(jq -nc --arg email "login-retry-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"login-retry-correct-horse",displayName:"Sign-in retry"}')"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/user"
user="$(jq -r '.user.id' "$scratch/user")"
key="$(cat /proc/sys/kernel/random/uuid)"
request() {
  curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: ${1:-$key}" -d "${2:-$body}" -D "$scratch/headers" -o "$scratch/response" -w '%{http_code}' "$BASE_URL/auth/login"
}
state() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$user'),
    'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$user'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id='$user' AND tenant_id IS NULL),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_login_replays r WHERE user_id='$user'))::text;"
}
before="$(state)"
for table in audit_events identity_login_replays; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request)" = 503
  jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
for n in 1 2 3; do
  curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $key" -d "$body" -D "$scratch/retry-$n.headers" -c "$scratch/retry-$n.cookies" \
    -o "$scratch/retry-$n.body" -w '%{http_code}' "$BASE_URL/auth/login" > "$scratch/retry-$n.status" &
  pids+=($!)
done
for pid in "${pids[@]}"; do wait "$pid"; done
pids=()
test -n "$(awk '$6=="strataai_session" {print $7}' "$scratch/retry-1.cookies")"
for n in 1 2 3; do
  test "$(cat "$scratch/retry-$n.status")" = 200
  cmp "$scratch/retry-1.body" "$scratch/retry-$n.body"
  test "$(awk '$6=="strataai_session" {print $7}' "$scratch/retry-1.cookies")" = "$(awk '$6=="strataai_session" {print $7}' "$scratch/retry-$n.cookies")"
done
test "$(admin "SELECT count(*) FROM sessions WHERE user_id='$user';")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='SESSION_CREATED';")" = 1
test "$(admin "SELECT count(*) FROM identity_login_replays WHERE user_id='$user' AND key_id='$key';")" = 1
saved="$(state)"
wrong="$(jq -c '.password="incorrect-private-password"' <<< "$body")"
test "$(request "$key" "$wrong")" = 401
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
test "$saved" = "$(state)"
new_secret="$(openssl rand -base64 32)"
echo "::add-mask::$new_secret"
retained_ring="$(jq -c --arg value "$new_secret" '.+{"ci-auth-v2":$value}' <<< "$original_ring")"
rotated=true
STRATAAI_AUTH_RETRY_CURRENT_KEY=ci-auth-v2 STRATAAI_AUTH_RETRY_KEYS="$retained_ring" \
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
test "$(request)" = 200
cmp "$scratch/retry-1.body" "$scratch/response"
test "$saved" = "$(state)"
retired_ring="$(jq -nc --arg value "$new_secret" '{"ci-auth-v2":$value}')"
STRATAAI_AUTH_RETRY_CURRENT_KEY=ci-auth-v2 STRATAAI_AUTH_RETRY_KEYS="$retired_ring" \
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
test "$(request)" = 503
jq -e '.code=="identity_retry_key_unavailable"' "$scratch/response" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
test "$saved" = "$(state)"
STRATAAI_AUTH_RETRY_CURRENT_KEY="$original_version" STRATAAI_AUTH_RETRY_KEYS="$original_ring" \
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
rotated=false
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml restart api >/dev/null
for attempt in $(seq 1 90); do
  if curl --fail --silent "$BASE_URL/readyz" >/dev/null; then break; fi
  sleep 1
done
test "$(request)" = 200
cmp "$scratch/retry-1.body" "$scratch/response"
test "$saved" = "$(state)"
curl --fail --silent --show-error -b "$scratch/retry-1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -X PATCH -d '{"displayName":"Current sign-in profile","version":1}' "$BASE_URL/me" > "$scratch/profile"
test "$(request)" = 200
jq -e '.user.displayName=="Current sign-in profile" and .user.version==2' "$scratch/response" >/dev/null
test "$(admin "SELECT count(*) FROM sessions WHERE user_id='$user';")" = 1
curl --fail --silent --show-error -b "$scratch/retry-1.cookies" -H 'X-StrataAI-Request: 1' -X POST "$BASE_URL/auth/logout" >/dev/null
saved="$(state)"
test "$(request)" = 409
jq -e '.code=="idempotency_key_expired"' "$scratch/response" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
test "$saved" = "$(state)"
fresh_key="$(cat /proc/sys/kernel/random/uuid)"
test "$(request "$fresh_key")" = 200
test "$(admin "SELECT count(*) FROM sessions WHERE user_id='$user';")" = 2
saved="$(state)"
admin "INSERT INTO identity_login_replays(user_id,key_id,session_id,key_version,fingerprint,created_at,expires_at)
  SELECT user_id,gen_random_uuid(),session_id,key_version,fingerprint,clock_timestamp()-interval '2 days',clock_timestamp()-interval '25 hours'
  FROM identity_login_replays CROSS JOIN generate_series(1,101) WHERE user_id='$user' AND key_id='$key';" >/dev/null
for attempt in $(seq 1 90); do
  if test "$(admin "SELECT count(*) FROM identity_login_replays WHERE user_id='$user' AND expires_at<=clock_timestamp();")" = 0; then break; fi
  sleep 1
done
test "$(admin "SELECT count(*) FROM identity_login_replays WHERE user_id='$user' AND expires_at<=clock_timestamp();")" = 0
test "$saved" = "$(state)"
fresh_session="$(admin "SELECT session_id FROM identity_login_replays WHERE user_id='$user' AND key_id='$fresh_key';")"
# Expiry during an observed session-row wait must prevent disclosure of the original cookie.
admin "BEGIN; SELECT id FROM sessions WHERE id='$fresh_session' FOR UPDATE;
  SELECT pg_sleep(8) /* login-session-expiry-gate */;
  UPDATE sessions SET expires_at=clock_timestamp()-interval '1 second' WHERE id='$fresh_session'; COMMIT;" > "$scratch/gate" &
gate=$!; pids+=($gate)
for attempt in $(seq 1 100); do
  if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%login-session-expiry-gate%' AND wait_event='PgSleep';")" = 1; then break; fi
  sleep 0.1
done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%login-session-expiry-gate%' AND wait_event='PgSleep';")" = 1
request "$fresh_key" > "$scratch/wait-status" &
pending=$!; pids+=($pending)
for attempt in $(seq 1 100); do
  if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock';")" -ge 1; then break; fi
  sleep 0.1
done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock';")" -ge 1
wait "$gate"; wait "$pending"; pids=()
test "$(cat "$scratch/wait-status")" = 409
jq -e '.code=="idempotency_key_expired"' "$scratch/response" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
test "$(admin "SELECT count(*) FROM sessions WHERE user_id='$user';")" = 2
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='SESSION_CREATED';")" = 2
echo 'Release sign-in retries: atomic audit/receipt rollback, concurrent original-session acknowledgment, fresh password checks, restart, current profile and revoked-session denial passed.'
