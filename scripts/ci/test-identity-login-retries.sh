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
hash_fixture_user=''
hash_fixture_original=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'DROP TRIGGER IF EXISTS ci_login_receipt_expiry ON identity_login_replays; DROP FUNCTION IF EXISTS public.ci_login_receipt_expiry(); GRANT INSERT ON audit_events,identity_login_replays TO strataai_api_runtime;' >/dev/null
  if test -n "$hash_fixture_user"; then
    admin "UPDATE users SET password_hash='$hash_fixture_original' WHERE id='$hash_fixture_user';" >/dev/null
  fi
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
    'stream',(SELECT last_sequence FROM identity_event_streams WHERE user_id='$user'),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id='$user'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_login_replays r WHERE user_id='$user'))::text;"
}
before="$(state)"
# Corrupt only this disposable account's persisted hash; no credential refusal
# may create sessions/audits/receipts or disclose storage encoding failures.
hash_fixture_original="$(admin "SELECT password_hash FROM users WHERE id='$user';")"
[[ "$hash_fixture_original" =~ ^[A-Za-z0-9+/=]+$ ]]
hash_fixture_user="$user"
for malformed in 'not-base64!' '' 'AQ==' 'Ag=='; do
  admin "UPDATE users SET password_hash='$malformed' WHERE id='$user';" >/dev/null
  corrupt_before="$(state)"
  test "$(request)" = 401
  jq -e '.code=="invalid_credentials"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  scripts/ci/assert-file-excludes.sh "$user|Sign-in retry|FormatException|Base64|password_hash" "$scratch/response"
  test "$corrupt_before" = "$(state)"
done
admin "UPDATE users SET password_hash='$hash_fixture_original' WHERE id='$user';" >/dev/null
hash_fixture_user=''
test "$before" = "$(state)"
wrong_credentials="$(jq -c '.password="incorrect-private-password"' <<< "$body")"
unknown_email="unknown-signin-$(cat /proc/sys/kernel/random/uuid)@example.test"
unknown_credentials="$(jq -c --arg email "$unknown_email" '.email=$email' <<< "$wrong_credentials")"
for refused_body in "$wrong_credentials" "$unknown_credentials"; do
  test "$(request "$key" "$refused_body")" = 401
  jq -e '.code=="invalid_credentials" and .status==401' "$scratch/response" >/dev/null
  jq -Sc '{status,title,type,code,detail}' "$scratch/response" > "$scratch/refusal.current"
  if test -f "$scratch/refusal.expected"; then cmp "$scratch/refusal.expected" "$scratch/refusal.current";
  else cp "$scratch/refusal.current" "$scratch/refusal.expected"; fi
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  scripts/ci/assert-file-excludes.sh "$user|Sign-in retry|$unknown_email" "$scratch/response"
  test "$before" = "$(state)"
done
test "$(admin "SELECT count(*) FROM users WHERE id='00000000-0000-0000-0000-000000000000' OR email_normalized=upper('$unknown_email');")" = 0
for table in audit_events identity_login_replays; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request)" = 503
  jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
# Expire only this fixture's actual session after the receipt has been inserted.
# The invoker trigger runs under the restricted API role, within the same command;
# it is ephemeral CI fault injection and is removed before ordinary success tests.
admin "CREATE FUNCTION public.ci_login_receipt_expiry() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER AS \$\$
BEGIN
  IF NEW.user_id='$user'::uuid THEN
    UPDATE public.sessions SET expires_at=clock_timestamp()-interval '1 second'
      WHERE id=NEW.session_id AND user_id=NEW.user_id;
    IF NOT FOUND THEN RAISE EXCEPTION 'CI session expiry did not reach its row'; END IF;
  END IF;
  RETURN NEW;
END;
\$\$;
CREATE TRIGGER ci_login_receipt_expiry AFTER INSERT ON identity_login_replays
  FOR EACH ROW EXECUTE FUNCTION public.ci_login_receipt_expiry();" >/dev/null
test "$(request)" = 401
jq -e '.code=="session_unavailable"' "$scratch/response" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
scripts/ci/assert-file-excludes.sh "$user" "$scratch/response"
test "$before" = "$(state)"
admin 'DROP TRIGGER ci_login_receipt_expiry ON identity_login_replays; DROP FUNCTION public.ci_login_receipt_expiry();' >/dev/null
# The exact same intent key must now create one session and immutable receipt.
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
