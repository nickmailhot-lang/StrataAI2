#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable registration fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
pids=()
original_version="${STRATAAI_AUTH_RETRY_CURRENT_KEY:?Runtime key version required}"
original_ring="${STRATAAI_AUTH_RETRY_KEYS:?Runtime key ring required}"
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'GRANT INSERT ON users,audit_events,identity_events,identity_registration_replays TO strataai_api_runtime;' >/dev/null
  STRATAAI_AUTH_RETRY_CURRENT_KEY="$original_version" STRATAAI_AUTH_RETRY_KEYS="$original_ring" \
    docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Registration retry check failed at line $LINENO" >&2' ERR
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
email="registration-retry-${RANDOM}-${RANDOM}@example.test"
normalized="${email^^}"
body="$(jq -nc --arg email "$email" '{email:$email,password:"register-retry-correct-horse",displayName:"Registration retry"}')"
key="$(cat /proc/sys/kernel/random/uuid)"
request() {
  curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: ${1:-$key}" -d "${2:-$body}" -D "$scratch/headers" -o "$scratch/response" -w '%{http_code}' "$BASE_URL/auth/register"
}
state() {
  admin "WITH subject AS (SELECT id FROM users WHERE email_normalized='$normalized') SELECT jsonb_build_object(
    'user',(SELECT to_jsonb(u) FROM users u JOIN subject s ON s.id=u.id),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a.id) FROM audit_events a JOIN subject s ON s.id=a.actor_id WHERE tenant_id IS NULL),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e JOIN subject s ON s.id=e.user_id),
    'stream',(SELECT last_sequence FROM identity_event_streams e JOIN subject s ON s.id=e.user_id),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_registration_replays r JOIN subject s ON s.id=r.user_id))::text;"
}
before="$(state)"
for table in users audit_events identity_events identity_registration_replays; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request)" = 503
  jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
  test "$before" = "$(state)"
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
for n in 1 2 3; do
  curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $key" -d "$body" -o "$scratch/retry-$n.body" -w '%{http_code}' "$BASE_URL/auth/register" > "$scratch/retry-$n.status" &
  pids+=($!)
done
for pid in "${pids[@]}"; do wait "$pid"; done
pids=()
for n in 1 2 3; do test "$(cat "$scratch/retry-$n.status")" = 201; cmp "$scratch/retry-1.body" "$scratch/retry-$n.body"; done
user="$(jq -r '.user.id' "$scratch/retry-1.body")"
test "$(admin "SELECT count(*) FROM users WHERE email_normalized='$normalized';")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='USER_REGISTERED';")" = 1
test "$(admin "SELECT count(*) FROM identity_events WHERE user_id='$user' AND event_type='USER_REGISTERED';")" = 1
test "$(admin "SELECT count(*) FROM identity_registration_replays WHERE user_id='$user';")" = 1
saved="$(state)"
test "$(request "$(cat /proc/sys/kernel/random/uuid)")" != 201
wrong="$(jq -c '.password="incorrect-private-password"' <<< "$body")"
test "$(request "$key" "$wrong")" != 201
scripts/ci/assert-file-excludes.sh "$user" "$scratch/response"
changed="$(jq -c '.displayName="Changed intent"' <<< "$body")"
test "$(request "$key" "$changed")" = 409
jq -e '.code=="idempotency_key_reused"' "$scratch/response" >/dev/null
test "$saved" = "$(state)"
new_secret="$(openssl rand -base64 32)"
echo "::add-mask::$new_secret"
retained_ring="$(jq -c --arg value "$new_secret" '.+{"ci-registration-v2":$value}' <<< "$original_ring")"
STRATAAI_AUTH_RETRY_CURRENT_KEY=ci-registration-v2 STRATAAI_AUTH_RETRY_KEYS="$retained_ring" \
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
test "$(request)" = 201
cmp "$scratch/retry-1.body" "$scratch/response"
test "$saved" = "$(state)"
retired_ring="$(jq -nc --arg value "$new_secret" '{"ci-registration-v2":$value}')"
STRATAAI_AUTH_RETRY_CURRENT_KEY=ci-registration-v2 STRATAAI_AUTH_RETRY_KEYS="$retired_ring" \
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
test "$(request)" = 503
jq -e '.code=="identity_retry_key_unavailable"' "$scratch/response" >/dev/null
test "$saved" = "$(state)"
STRATAAI_AUTH_RETRY_CURRENT_KEY="$original_version" STRATAAI_AUTH_RETRY_KEYS="$original_ring" \
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml restart api >/dev/null
for attempt in $(seq 1 90); do if curl --fail --silent "$BASE_URL/readyz" >/dev/null; then break; fi; sleep 1; done
test "$(request)" = 201
cmp "$scratch/retry-1.body" "$scratch/response"
test "$saved" = "$(state)"
for status in SUSPENDED DEACTIVATED; do
  admin "UPDATE users SET status='$status' WHERE id='$user';" >/dev/null
  unavailable="$(state)"
  test "$(request)" != 201
  scripts/ci/assert-file-excludes.sh "$user" "$scratch/response"
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  test "$unavailable" = "$(state)"
done
admin "UPDATE users SET status='ACTIVE' WHERE id='$user';" >/dev/null
test "$saved" = "$(state)"
admin "INSERT INTO identity_registration_replays(user_id,key_id,key_version,fingerprint,verification_source,created_at,expires_at)
  SELECT user_id,gen_random_uuid(),key_version,fingerprint,'NONE',clock_timestamp()-interval '2 days',clock_timestamp()-interval '25 hours'
  FROM identity_registration_replays CROSS JOIN generate_series(1,101) WHERE user_id='$user' AND key_id='$key';" >/dev/null
for attempt in $(seq 1 90); do
  if test "$(admin "SELECT count(*) FROM identity_registration_replays WHERE user_id='$user' AND expires_at<=clock_timestamp();")" = 0; then break; fi
  sleep 1
done
test "$(admin "SELECT count(*) FROM identity_registration_replays WHERE user_id='$user' AND expires_at<=clock_timestamp();")" = 0
test "$saved" = "$(state)"
echo 'Release registration retries: atomic user/audit/event/receipt rollback, concurrent first insert, credential proof, collision, one-connection restart and Worker retention passed.'
