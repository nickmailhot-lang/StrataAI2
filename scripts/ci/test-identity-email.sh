#!/usr/bin/env bash
set -euo pipefail
base=http://localhost:8080
fixture=http://localhost:19090
compose=(docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.identity-test.yml)
scratch="$(mktemp -d)"
gate_pid=''
request_pid=''
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  query 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
  query 'GRANT INSERT ON identity_events TO strataai_api_runtime;' >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Identity mail integration failed at line $LINENO" >&2' ERR
query() { docker compose -f compose.release.yml exec -T postgres psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" --quiet --tuples-only --no-align --command="$1"; }
post() {
  curl --max-time 60 --silent --show-error -o "$scratch/response" -w '%{http_code}' -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$2" "$base$1"
}
token_state() {
  query "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$identity_user'),
    'verification',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM email_verification_tokens t WHERE user_id='$identity_user'),
    'reset',(SELECT jsonb_agg(to_jsonb(t) ORDER BY id) FROM password_reset_tokens t WHERE user_id='$identity_user'),
    'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$identity_user'),
    'audits',(SELECT count(*) FROM audit_events WHERE actor_id='$identity_user'),
    'stream',(SELECT to_jsonb(s) FROM identity_event_streams s WHERE user_id='$identity_user'),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id='$identity_user'))::text;"
}
reject_token_event() {
  local before
  before="$(token_state)"
  query 'REVOKE INSERT ON identity_events FROM strataai_api_runtime;' >/dev/null
  test "$(post "$1" "$2")" = 503
  jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Npgsql|identity_events|permission denied|UPDATE users|INSERT INTO' "$scratch/response"
  test "$before" = "$(token_state)"
  query 'GRANT INSERT ON identity_events TO strataai_api_runtime;' >/dev/null
}
reject_token_audit() {
  local before
  before="$(token_state)"
  query 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
  test "$(post "$1" "$2")" = 503
  jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Npgsql|audit_events|permission denied|UPDATE users|INSERT INTO' "$scratch/response"
  test "$before" = "$(token_state)"
  query 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
}
reject_recovery_audit() {
  local endpoint="$1" before target
  before="$(registration_state)"
  query 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
  for target in "$email" "unknown-recovery-${RANDOM}@example.test"; do
    test "$(post "$endpoint" "$(jq -nc --arg email "$target" '{email:$email}')")" = 202
    jq -e '.accepted==true and (.resetToken // null)==null and (.verificationToken // null)==null' "$scratch/response" >/dev/null
    scripts/ci/assert-file-excludes.sh 'Npgsql|audit_events|permission denied|INSERT INTO' "$scratch/response"
    test "$before" = "$(registration_state)"
  done
  query 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
}
expire_token_during_wait() {
  local table="$1" id="$2" endpoint="$3" payload="$4" before count
  query "UPDATE $table SET expires_at=clock_timestamp()+interval '10 seconds' WHERE id='$id';" >/dev/null
  before="$(token_state)"
  rm -f "$scratch/token-gate.in" "$scratch/token-gate.log"
  mkfifo "$scratch/token-gate.in"
  docker compose -f compose.release.yml exec -T postgres psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" < "$scratch/token-gate.in" > "$scratch/token-gate.log" 2>&1 &
  gate_pid=$!
  exec 3> "$scratch/token-gate.in"
  printf 'BEGIN;\nSELECT id FROM %s WHERE id=%s FOR UPDATE;\n\\echo token_locked\n' "$table" "'$id'" >&3
  for ((attempt=0; attempt<100; attempt++)); do
    if grep -q '^token_locked$' "$scratch/token-gate.log"; then break; fi
    kill -0 "$gate_pid" || return 1
    sleep 0.05
  done
  grep -q '^token_locked$' "$scratch/token-gate.log"
  post "$endpoint" "$payload" > "$scratch/token-status" &
  request_pid=$!
  for ((attempt=0; attempt<100; attempt++)); do
    count="$(query "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%FROM $table%FOR UPDATE%';")"
    if test "$count" = 1; then break; fi
    sleep 0.05
  done
  test "$count" = 1
  query 'SELECT pg_sleep(11);' >/dev/null
  printf 'COMMIT;\n\\q\n' >&3
  exec 3>&-
  wait "$gate_pid"
  gate_pid=''
  wait "$request_pid"
  request_pid=''
  test "$(cat "$scratch/token-status")" = 400
  jq -e '.code=="invalid_or_expired_token"' "$scratch/response" >/dev/null
  test "$before" = "$(token_state)"
  # Restore only the disposable token's expiry for the existing success/single-use checks.
  query "UPDATE $table SET expires_at=clock_timestamp()+interval '30 minutes' WHERE id='$id';" >/dev/null
}
latest_job() { query "SELECT id FROM identity_delivery_jobs WHERE recipient_email='$email' AND purpose='$1' ORDER BY created_at DESC,id DESC LIMIT 1;"; }
wait_state() {
  local id="$1" state="$2"
  for attempt in $(seq 1 90); do
    if [ "$(query "SELECT state FROM identity_delivery_jobs WHERE id='$id';")" = "$state" ]; then return; fi
    sleep 1
  done
  echo "Delivery did not reach $state" >&2
  return 1
}
mail_token() {
  curl --fail --silent "$fixture/messages" | jq -r --arg key "strataai-identity/$1/$2" \
    '.[] | select(.key==$key) | .payload.text | capture("#token=(?<token>[A-Za-z0-9_-]+)").token'
}
# The release's disabled mail mode fails uniformly before identity lookup.
known="$(query 'SELECT email FROM users ORDER BY created_at,id LIMIT 1;')"
test -n "$known"
for target in missing@example.test "$known"; do
  test "$(post /auth/password/forgot "$(jq -nc --arg email "$target" '{email:$email}')")" = 503
  jq -e '.code=="identity_delivery_unavailable"' "$scratch/response" >/dev/null
done
docker compose -f compose.release.yml exec -T postgres psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" < scripts/ci/grant-identity-worker.sql
"${compose[@]}" stop worker
# Registration and its token/delivery/audit must borrow one connection.
STRATAAI_TEST_COMMAND_TIMEOUT=30 "${compose[@]}" -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api

# PRD-02/60: audit rejection must roll back account, verification token and queued delivery.
email="audit-rollback-${RANDOM}-${RANDOM}@example.test"
registration="$(jq -nc --arg email "$email" '{email:$email,password:"mail-correct-horse-battery",displayName:"Registration audit rollback"}')"
registration_state() {
  query "SELECT jsonb_build_object('users',(SELECT count(*) FROM users),'verification',(SELECT count(*) FROM email_verification_tokens),'reset',(SELECT count(*) FROM password_reset_tokens),'delivery',(SELECT count(*) FROM identity_delivery_jobs),'audit',(SELECT count(*) FROM audit_events))::text;"
}
before_registration="$(registration_state)"
query 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(post /auth/register "$registration")" = 503
jq -e '.code=="identity_storage_unavailable" and .status==503' "$scratch/response" >/dev/null
scripts/ci/assert-file-excludes.sh 'Registration audit rollback|Npgsql|audit_events|permission denied|INSERT INTO' "$scratch/response"
test "$before_registration" = "$(registration_state)"
test "$(query "SELECT count(*) FROM users WHERE email='$email';")" = 0
query 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$(post /auth/register "$registration")" = 201
registration_user="$(jq -r '.user.id' "$scratch/response")"
test "$(query "SELECT count(*) FROM email_verification_tokens WHERE user_id='$registration_user';")" = 1
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$registration_user' AND purpose='VERIFY_EMAIL';")" = 1
test "$(query "SELECT count(*) FROM audit_events WHERE actor_id='$registration_user' AND event_type='USER_REGISTERED';")" = 1
before_registration="$(registration_state)"
test "$(post /auth/register "$(jq '.email |= ascii_upcase' <<< "$registration")")" = 409
jq -e '.code=="email_unavailable"' "$scratch/response" >/dev/null
test "$before_registration" = "$(registration_state)"

# A publication failure cannot leave a user or verification token committed.
query "CREATE FUNCTION ci_reject_identity_publication() RETURNS trigger LANGUAGE plpgsql AS \$\$ BEGIN RAISE EXCEPTION 'fixture publication rejected'; END; \$\$; CREATE TRIGGER ci_reject_identity_publication BEFORE INSERT ON identity_delivery_jobs FOR EACH ROW EXECUTE FUNCTION ci_reject_identity_publication();" >/dev/null
email="rollback-${RANDOM}-${RANDOM}@example.test"
test "$(post /auth/register "$(jq -nc --arg email "$email" '{email:$email,password:"mail-correct-horse-battery",displayName:"Mail rollback"}')")" = 503
jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
test "$(query "SELECT count(*) FROM users WHERE email='$email';")" = 0
query 'DROP TRIGGER ci_reject_identity_publication ON identity_delivery_jobs; DROP FUNCTION ci_reject_identity_publication();' >/dev/null

email="identity-${RANDOM}-${RANDOM}@example.test"
password=mail-correct-horse-battery
test "$(post /auth/register "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password,displayName:"Identity delivery"}')")" = 201
jq -e '.verificationToken==null and .user.emailVerified==false' "$scratch/response" >/dev/null
identity_user="$(jq -r '.user.id' "$scratch/response")"
verification_id="$(latest_job VERIFY_EMAIL)"
test -n "$verification_id"
test "$(query "SELECT state FROM identity_delivery_jobs WHERE id='$verification_id';")" = PENDING
test "$(post /auth/login "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')")" = 403
jq -e '.code=="email_verification_required"' "$scratch/response" >/dev/null
# Restart with a rotated current key while retaining the queued job's old key.
STRATAAI_TEST_CURRENT_KEY=k2 "${compose[@]}" up -d --wait --wait-timeout 180 worker
wait_state "$verification_id" SENT
verification="$(mail_token verify "${verification_id//-/}")"
test -n "$verification"
curl --fail --silent "$fixture/messages" | jq -e --arg key "strataai-identity/verify/${verification_id//-/}" \
  '[.[]|select(.key==$key)] | length==1 and .[0].attempts==2' >/dev/null
test "$(query "SELECT attempt_count FROM identity_delivery_jobs WHERE id='$verification_id';")" = 2
if query "SELECT row_to_json(j)::text FROM identity_delivery_jobs j WHERE id='$verification_id';" | grep -Fq "$verification"; then
  echo 'Plaintext verification token persisted in queue' >&2; exit 1
fi
test "$(post /auth/password/reset "$(jq -nc --arg token "$verification" '{token:$token,newPassword:"replacement-correct-horse"}')")" = 400
reject_token_audit /auth/verify-email "$(jq -nc --arg token "$verification" '{token:$token}')"
reject_token_event /auth/verify-email "$(jq -nc --arg token "$verification" '{token:$token}')"
expire_token_during_wait email_verification_tokens "$verification_id" /auth/verify-email "$(jq -nc --arg token "$verification" '{token:$token}')"
test "$(post /auth/verify-email "$(jq -nc --arg token "$verification" '{token:$token}')")" = 200
test "$(post /auth/verify-email "$(jq -nc --arg token "$verification" '{token:$token}')")" = 400
curl --fail --silent -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')" "$base/auth/login" >/dev/null

test "$(post /auth/password/forgot '{"email":"unknown-identity@example.test"}')" = 202
jq -e '.accepted==true and .resetToken==null' "$scratch/response" >/dev/null
reject_recovery_audit /auth/password/forgot
test "$(post /auth/password/forgot "$(jq -nc --arg email "$email" '{email:$email}')")" = 202
jq -e '.accepted==true and .resetToken==null' "$scratch/response" >/dev/null
reset_id="$(latest_job RESET_PASSWORD)"
wait_state "$reset_id" SENT
reset="$(mail_token reset "${reset_id//-/}")"
test -n "$reset"
if query "SELECT row_to_json(j)::text FROM identity_delivery_jobs j WHERE id='$reset_id';" | grep -Fq "$reset"; then
  echo 'Plaintext reset token persisted in queue' >&2; exit 1
fi
test "$(post /auth/verify-email "$(jq -nc --arg token "$reset" '{token:$token}')")" = 400
reject_token_audit /auth/password/reset "$(jq -nc --arg token "$reset" '{token:$token,newPassword:"replacement-correct-horse"}')"
reject_token_event /auth/password/reset "$(jq -nc --arg token "$reset" '{token:$token,newPassword:"replacement-correct-horse"}')"
expire_token_during_wait password_reset_tokens "$reset_id" /auth/password/reset "$(jq -nc --arg token "$reset" '{token:$token,newPassword:"replacement-correct-horse"}')"
test "$(curl --silent -o /dev/null -w '%{http_code}' -b "$scratch/cookies" "$base/me")" = 200
test "$(post /auth/password/reset "$(jq -nc --arg token "$reset" '{token:$token,newPassword:"replacement-correct-horse"}')")" = 200
reset_version="$(jq -r '.version' "$scratch/response")"
test "$(query "SELECT count(*) FROM identity_events WHERE user_id='$identity_user' AND event_type='SESSION_REVOKED' AND entity_version=$reset_version AND actor_id=user_id AND entity_id=user_id AND entity_type='User' AND organization_id IS NULL AND board_id IS NULL AND metadata='{}'::jsonb;")" = 1
reset_state="$(token_state)"
test "$(post /auth/password/reset "$(jq -nc --arg token "$reset" '{token:$token,newPassword:"another-replacement-horse"}')")" = 400
test "$reset_state" = "$(token_state)"
test "$(curl --silent -o /dev/null -w '%{http_code}' -b "$scratch/cookies" "$base/me")" = 401
test "$(post /auth/login "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')")" = 401
curl --fail --silent -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" '{email:$email,password:"replacement-correct-horse"}')" "$base/auth/login" >/dev/null

# Permanent rejection becomes failed, not an endless resend loop.
curl --fail --silent -H 'Content-Type: application/json' -d '{"reject_next":true}' "$fixture/control" >/dev/null
test "$(post /auth/password/forgot "$(jq -nc --arg email "$email" '{email:$email}')")" = 202
rejected="$(latest_job RESET_PASSWORD)"
wait_state "$rejected" FAILED
test "$(query "SELECT last_error_code FROM identity_delivery_jobs WHERE id='$rejected';")" = identity_provider_rejected

# Deactivation after publication prevents a queued send.
"${compose[@]}" stop worker
test "$(post /auth/password/forgot "$(jq -nc --arg email "$email" '{email:$email}')")" = 202
cancelled="$(latest_job RESET_PASSWORD)"
curl --fail --silent -X POST -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' "$base/me/deactivate" >/dev/null
STRATAAI_TEST_CURRENT_KEY=k2 "${compose[@]}" up -d --wait --wait-timeout 180 worker
wait_state "$cancelled" CANCELLED
curl --fail --silent "$fixture/messages" | jq -e --arg key "strataai-identity/reset/${cancelled//-/}" '[.[]|select(.key==$key)]|length==0' >/dev/null

# An expired verification token can be replaced without revealing registration.
"${compose[@]}" stop worker
email="resend-${RANDOM}-${RANDOM}@example.test"
test "$(post /auth/register "$(jq -nc --arg email "$email" '{email:$email,password:"mail-correct-horse-battery",displayName:"Resend verification"}')")" = 201
expired="$(latest_job VERIFY_EMAIL)"
reject_recovery_audit /auth/verification/resend
query "UPDATE email_verification_tokens SET expires_at=clock_timestamp()-interval '1 second' WHERE id='$expired';" >/dev/null
test "$(post /auth/verification/resend '{"email":"unknown-verification@example.test"}')" = 202
jq -e '.accepted==true and .verificationToken==null' "$scratch/response" >/dev/null
test "$(post /auth/verification/resend "$(jq -nc --arg email "$email" '{email:$email}')")" = 202
jq -e '.accepted==true and .verificationToken==null' "$scratch/response" >/dev/null
replacement="$(latest_job VERIFY_EMAIL)"
test "$replacement" != "$expired"
STRATAAI_TEST_CURRENT_KEY=k2 "${compose[@]}" up -d --wait --wait-timeout 180 worker
wait_state "$expired" CANCELLED
wait_state "$replacement" SENT
token="$(mail_token verify "${replacement//-/}")"
test "$(post /auth/verify-email "$(jq -nc --arg token "$token" '{token:$token}')")" = 200
echo 'Exact-image identity email, atomic publication, restart/rotation, provider retry/idempotency, verification/reset/replay/revocation, cancellation and resend checks passed.'
