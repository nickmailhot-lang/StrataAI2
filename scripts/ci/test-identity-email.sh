#!/usr/bin/env bash
set -euo pipefail
base=http://localhost:8080
fixture=http://localhost:19090
compose=(docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.identity-test.yml)
scratch="$(mktemp -d)"
gate_pid=''
request_pid=''
recovery_pids=()
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  for pid in "${recovery_pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  query 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
  query 'GRANT INSERT ON identity_events TO strataai_api_runtime;' >/dev/null
  query 'GRANT INSERT ON email_verification_tokens,identity_delivery_jobs,identity_registration_replays TO strataai_api_runtime;' >/dev/null
  query 'GRANT INSERT ON password_reset_tokens,identity_recovery_request_replays TO strataai_api_runtime;' >/dev/null
  query 'GRANT INSERT ON identity_token_consumption_replays TO strataai_api_runtime;' >/dev/null
  query 'GRANT INSERT ON invitations,work_events,invitation_mail_intents,background_jobs,invitation_creation_replays TO strataai_api_runtime;' >/dev/null
  if test "${signup_closed:-false}" = true; then "${compose[@]}" up -d --wait --wait-timeout 180 api worker >/dev/null || true; fi
  rm -rf "$scratch"
}
trap cleanup EXIT
mail_failure() {
  local status="$1" line="$2" diagnostic
  echo "Identity mail integration failed at line $line" >&2
  # Capture only constrained state and booleans before EXIT restores the Worker.
  # Never retain recipients, bearer proofs, provider receipts or job metadata.
  if [[ "${invitation_job:-}" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
    diagnostic="${RUNNER_TEMP:-$scratch}/identity-mail-failure.json"
    query "SELECT jsonb_build_object('state',j.state,'attemptCount',j.attempt_count,
      'maxAttempts',j.max_attempts,'errorCode',j.last_error_code,
      'due',j.available_at<=clock_timestamp(),'hasWorker',j.worker_id IS NOT NULL,
      'hasLease',j.lease_id IS NOT NULL,'leaseActive',j.lease_expires_at>clock_timestamp(),
      'deliveryState',(SELECT i.state FROM invitation_mail_intents i WHERE i.job_id=j.id AND i.tenant_id=j.tenant_id))::text
      FROM background_jobs j WHERE j.id='$invitation_job';" > "$diagnostic" || true
    cat "$diagnostic" >&2 || true
  fi
  return "$status"
}
trap 'mail_failure "$?" "$LINENO"' ERR
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
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id='$identity_user'),
    'consumption',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id,operation) FROM identity_token_consumption_replays r WHERE user_id='$identity_user'))::text;"
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
# Keyed registration must retain one verification token and one delivery, including lost responses.
"${compose[@]}" stop worker >/dev/null
email="keyed-registration-${RANDOM}-${RANDOM}@example.test"
registration="$(jq -nc --arg email "$email" '{email:$email,password:"mail-correct-horse-battery",displayName:"Keyed registration"}')"
registration_key="$(cat /proc/sys/kernel/random/uuid)"
register_retry() {
  curl --max-time 60 --silent --show-error -o "$scratch/response" -w '%{http_code}' -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $registration_key" -d "$registration" "$base/auth/register"
}
registration_retry_state() {
  query "SELECT jsonb_build_object('users',(SELECT count(*) FROM users),'tokens',(SELECT count(*) FROM email_verification_tokens),'jobs',(SELECT count(*) FROM identity_delivery_jobs),'audits',(SELECT count(*) FROM audit_events),
    'events',(SELECT count(*) FROM identity_events),'receipts',(SELECT count(*) FROM identity_registration_replays))::text;"
}
before_registration="$(registration_retry_state)"
for table in email_verification_tokens identity_delivery_jobs identity_registration_replays; do
  query "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(register_retry)" = 503
  jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
  test "$before_registration" = "$(registration_retry_state)"
  query "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
test "$(register_retry)" = 201
cp "$scratch/response" "$scratch/registration-original"
jq -e '.verificationToken==null and .user.emailVerified==false' "$scratch/response" >/dev/null
registration_user="$(jq -r '.user.id' "$scratch/response")"
for attempt in 1 2 3; do test "$(register_retry)" = 201; cmp "$scratch/registration-original" "$scratch/response"; done
test "$(query "SELECT count(*) FROM email_verification_tokens WHERE user_id='$registration_user';")" = 1
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$registration_user';")" = 1
test "$(query "SELECT count(*) FROM identity_events WHERE user_id='$registration_user' AND event_type='USER_REGISTERED';")" = 1
verification_job="$(latest_job VERIFY_EMAIL)"
"${compose[@]}" up -d --wait --wait-timeout 180 worker >/dev/null
wait_state "$verification_job" SENT
token="$(mail_token verify "${verification_job//-/}")"
test "$(post /auth/verify-email "$(jq -nc --arg token "$token" '{token:$token}')")" = 200
test "$(register_retry)" = 201
jq -e '.verificationToken==null and .user.emailVerified==true' "$scratch/response" >/dev/null
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$registration_user';")" = 1
echo 'Keyed production registration: token/delivery/receipt rollback, duplicate proof, one verification email and post-verification acknowledgment passed.'

# Recovery acknowledgments remain neutral while durable intents prevent duplicate publication.
"${compose[@]}" stop worker >/dev/null
recovery_request() {
  curl --max-time 60 --silent --show-error -o "$scratch/response" -w '%{http_code}' -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $recovery_key" -d "$recovery_body" "$base$recovery_route"
}
recovery_state() {
  query "SELECT jsonb_build_object('tokens',(SELECT count(*) FROM $recovery_table WHERE user_id='$recovery_user'),
    'jobs',(SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$recovery_user'),
    'audits',(SELECT count(*) FROM audit_events WHERE actor_id='$recovery_user'),
    'receipts',(SELECT count(*) FROM identity_recovery_request_replays WHERE user_id='$recovery_user'))::text;"
}
consume_request() {
  curl --max-time 60 --silent --show-error -o "$scratch/response" -w '%{http_code}' -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: ${1:-$consumption_key}" -d "$consumption_body" "$base$consumption_route"
}
expire_consumption_during_wait() {
  query "UPDATE $recovery_table SET expires_at=clock_timestamp()+interval '10 seconds' WHERE id='$recovery_job';" >/dev/null
  local before count
  before="$(token_state)"
  rm -f "$scratch/consume-gate.in" "$scratch/consume-gate.log"
  mkfifo "$scratch/consume-gate.in"
  docker compose -f compose.release.yml exec -T postgres psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" < "$scratch/consume-gate.in" > "$scratch/consume-gate.log" 2>&1 &
  gate_pid=$!
  exec 3> "$scratch/consume-gate.in"
  printf 'BEGIN;\nSELECT id FROM %s WHERE id=%s FOR UPDATE;\n\\echo token_locked\n' "$recovery_table" "'$recovery_job'" >&3
  for ((attempt=0; attempt<100; attempt++)); do
    if grep -q '^token_locked$' "$scratch/consume-gate.log"; then break; fi
    kill -0 "$gate_pid" || return 1
    sleep 0.05
  done
  grep -q '^token_locked$' "$scratch/consume-gate.log"
  consume_request > "$scratch/consume-status" &
  request_pid=$!
  for ((attempt=0; attempt<100; attempt++)); do
    count="$(query "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%FROM $recovery_table%FOR SHARE%';")"
    if test "$count" = 1; then break; fi
    sleep 0.05
  done
  test "$count" = 1
  sleep 11
  printf 'COMMIT;\n\\q\n' >&3
  exec 3>&-
  wait "$gate_pid"; gate_pid=''
  wait "$request_pid"; request_pid=''
  test "$(cat "$scratch/consume-status")" = 400
  jq -e '.code=="invalid_or_expired_token"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh "$recovery_user" "$scratch/response"
  test "$before" = "$(token_state)"
  query "UPDATE $recovery_table SET expires_at=clock_timestamp()+interval '30 minutes' WHERE id='$recovery_job';" >/dev/null
}
for purpose in RESET_PASSWORD VERIFY_EMAIL; do
  email="keyed-recovery-${purpose}-${RANDOM}-${RANDOM}@example.test"
  test "$(post /auth/register "$(jq -nc --arg email "$email" '{email:$email,password:"mail-correct-horse-battery",displayName:"Recovery retry"}')")" = 201
  recovery_user="$(jq -r '.user.id' "$scratch/response")"
  recovery_key="$(cat /proc/sys/kernel/random/uuid)"
  recovery_body="$(jq -nc --arg email "$email" '{email:$email}')"
  if test "$purpose" = RESET_PASSWORD; then recovery_route=/auth/password/forgot; recovery_table=password_reset_tokens; else recovery_route=/auth/verification/resend; recovery_table=email_verification_tokens; fi
  before="$(recovery_state)"
  for table in "$recovery_table" identity_delivery_jobs audit_events identity_recovery_request_replays; do
    query "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
    test "$(recovery_request)" = 202
    jq -e '.accepted==true and (.resetToken // null)==null and (.verificationToken // null)==null' "$scratch/response" >/dev/null
    test "$before" = "$(recovery_state)"
    query "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
  done
  for n in 1 2 3; do
    curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
      -H "Idempotency-Key: $recovery_key" -d "$recovery_body" -o "$scratch/recovery-$n.body" -w '%{http_code}' "$base$recovery_route" > "$scratch/recovery-$n.status" &
    recovery_pids+=($!)
  done
  for pid in "${recovery_pids[@]}"; do wait "$pid"; done
  recovery_pids=()
  for n in 1 2 3; do test "$(cat "$scratch/recovery-$n.status")" = 202; cmp "$scratch/recovery-1.body" "$scratch/recovery-$n.body"; done
  test "$(query "SELECT count(*) FROM identity_recovery_request_replays WHERE user_id='$recovery_user' AND operation='$purpose';")" = 1
  test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$recovery_user';")" = 2
  saved="$(recovery_state)"
  "${compose[@]}" restart api >/dev/null
  for attempt in $(seq 1 90); do if curl --fail --silent "$base/readyz" >/dev/null; then break; fi; sleep 1; done
  test "$(recovery_request)" = 202
  cmp "$scratch/recovery-1.body" "$scratch/response"
  test "$saved" = "$(recovery_state)"
  recovery_job="$(latest_job "$purpose")"
  "${compose[@]}" up -d --wait --wait-timeout 180 worker >/dev/null
  wait_state "$recovery_job" SENT
  if test "$purpose" = RESET_PASSWORD; then
    token="$(mail_token reset "${recovery_job//-/}")"
    original_verification="$(latest_job VERIFY_EMAIL)"
    wait_state "$original_verification" SENT
    original_token="$(mail_token verify "${original_verification//-/}")"
    test "$(post /auth/verify-email "$(jq -nc --arg token "$original_token" '{token:$token}')")" = 200
    curl --fail --silent -c "$scratch/consume-cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
      -d "$(jq -nc --arg email "$email" '{email:$email,password:"mail-correct-horse-battery"}')" "$base/auth/login" >/dev/null
    consumption_route=/auth/password/reset
    consumption_body="$(jq -nc --arg token "$token" '{token:$token,newPassword:"replacement-recovery-horse"}')"
    login_password=replacement-recovery-horse
  else
    token="$(mail_token verify "${recovery_job//-/}")"
    consumption_route=/auth/verify-email
    consumption_body="$(jq -nc --arg token "$token" '{token:$token}')"
    login_password=mail-correct-horse-battery
  fi
  identity_user="$recovery_user"
  consumption_key="$(cat /proc/sys/kernel/random/uuid)"
  before_consumption="$(token_state)"
  for table in audit_events identity_events identity_token_consumption_replays; do
    query "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
    test "$(consume_request)" = 503
    jq -e '.code=="identity_storage_unavailable"' "$scratch/response" >/dev/null
    test "$before_consumption" = "$(token_state)"
    query "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
  done
  for n in 1 2 3; do
    curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
      -H "Idempotency-Key: $consumption_key" -d "$consumption_body" -o "$scratch/consume-$n.body" -w '%{http_code}' "$base$consumption_route" > "$scratch/consume-$n.status" &
    recovery_pids+=($!)
  done
  for pid in "${recovery_pids[@]}"; do wait "$pid"; done
  recovery_pids=()
  for n in 1 2 3; do test "$(cat "$scratch/consume-$n.status")" = 200; cmp "$scratch/consume-1.body" "$scratch/consume-$n.body"; done
  test "$(query "SELECT count(*) FROM identity_token_consumption_replays WHERE user_id='$recovery_user';")" = 1
  if test "$purpose" = RESET_PASSWORD; then test "$(curl --silent -o /dev/null -w '%{http_code}' -b "$scratch/consume-cookies" "$base/me")" = 401; fi
  consumed_state="$(token_state)"
  test "$(consume_request "$(cat /proc/sys/kernel/random/uuid)")" = 400
  test "$consumed_state" = "$(token_state)"
  "${compose[@]}" restart api >/dev/null
  for attempt in $(seq 1 90); do if curl --fail --silent "$base/readyz" >/dev/null; then break; fi; sleep 1; done
  test "$(consume_request)" = 200
  cmp "$scratch/consume-1.body" "$scratch/response"
  test "$consumed_state" = "$(token_state)"
  curl --fail --silent -c "$scratch/new-consume-cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg email "$email" --arg password "$login_password" '{email:$email,password:$password}')" "$base/auth/login" >/dev/null
  consumed_state="$(token_state)"
  test "$(consume_request)" = 200
  test "$consumed_state" = "$(token_state)"
  test "$(curl --silent -o /dev/null -w '%{http_code}' -b "$scratch/new-consume-cookies" "$base/me")" = 200
  expire_consumption_during_wait
  for status in SUSPENDED DEACTIVATED; do
    query "UPDATE users SET status='$status' WHERE id='$recovery_user';" >/dev/null
    inactive_state="$(token_state)"
    test "$(consume_request)" = 400
    scripts/ci/assert-file-excludes.sh "$recovery_user" "$scratch/response"
    test "$inactive_state" = "$(token_state)"
  done
  query "UPDATE users SET status='ACTIVE' WHERE id='$recovery_user';" >/dev/null
  consumed_state="$(token_state)"
  saved="$(recovery_state)"
  test "$(recovery_request)" = 202
  cmp "$scratch/recovery-1.body" "$scratch/response"
  test "$saved" = "$(recovery_state)"
  query "INSERT INTO identity_recovery_request_replays(user_id,key_id,operation,key_version,fingerprint,password_reset_token_id,verification_token_id,token_source,token_key_version,created_at,expires_at)
    SELECT user_id,gen_random_uuid(),operation,key_version,fingerprint,password_reset_token_id,verification_token_id,token_source,token_key_version,
      clock_timestamp()-interval '2 days',clock_timestamp()-interval '25 hours' FROM identity_recovery_request_replays CROSS JOIN generate_series(1,101)
      WHERE user_id='$recovery_user' AND key_id='$recovery_key' AND operation='$purpose';" >/dev/null
  query "INSERT INTO identity_token_consumption_replays(user_id,key_id,operation,key_version,fingerprint,password_reset_token_id,verification_token_id,consumed_at,created_at,expires_at)
    SELECT user_id,gen_random_uuid(),operation,key_version,fingerprint,password_reset_token_id,verification_token_id,
      clock_timestamp()-interval '3 days',clock_timestamp()-interval '2 days',clock_timestamp()-interval '25 hours'
      FROM identity_token_consumption_replays CROSS JOIN generate_series(1,101) WHERE user_id='$recovery_user' AND key_id='$consumption_key' AND operation='$purpose';" >/dev/null
  for attempt in $(seq 1 90); do
    if test "$(query "SELECT count(*) FROM identity_recovery_request_replays WHERE user_id='$recovery_user' AND expires_at<=clock_timestamp();")" = 0; then break; fi
    sleep 1
  done
  test "$(query "SELECT count(*) FROM identity_recovery_request_replays WHERE user_id='$recovery_user';")" = 1
  test "$saved" = "$(recovery_state)"
  for attempt in $(seq 1 90); do
    if test "$(query "SELECT count(*) FROM identity_token_consumption_replays WHERE user_id='$recovery_user' AND expires_at<=clock_timestamp();")" = 0; then break; fi
    sleep 1
  done
  test "$consumed_state" = "$(token_state)"
  "${compose[@]}" stop worker >/dev/null
done
"${compose[@]}" up -d --wait --wait-timeout 180 worker >/dev/null
# Invitation signup with the default verified-email policy and actual separate Worker.
# First create and verify the issuer through the same mail transport.
email="signup-mail-owner-${RANDOM}-${RANDOM}@example.test"
mail_owner_body="$(jq -nc --arg email "$email" '{email:$email,password:"signup-mail-correct-horse",displayName:"Signup mail owner"}')"
test "$(post /auth/register "$mail_owner_body")" = 201
mail_owner_id="$(jq -r '.user.id' "$scratch/response")"; mail_owner_job="$(latest_job VERIFY_EMAIL)"
wait_state "$mail_owner_job" SENT
mail_proof="$(mail_token verify "${mail_owner_job//-/}")"; test -n "$mail_proof"
test "$(post /auth/verify-email "$(jq -nc --arg token "$mail_proof" '{token:$token}')")" = 200
curl --fail --silent --show-error -c "$scratch/signup-owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$mail_owner_body" "$base/auth/login" >/dev/null
signup_owner_post() { curl --max-time 60 --silent --show-error -b "$scratch/signup-owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$2" -o "$scratch/response" -w '%{http_code}' "$base$1"; }
test "$(signup_owner_post /organizations '{"name":"Verified invitation signup"}')" = 201
signup_org="$(jq -r '.organization.id' "$scratch/response")"
signup_closed=true
export STRATAAI_TEST_INVITATION_ORGANIZATION_ID="$signup_org"
"${compose[@]}" -f scripts/ci/compose.invitation-signup-test.yml -f scripts/ci/compose.invitation-mail-test.yml up -d --wait --wait-timeout 180 api worker >/dev/null
# The new producer must roll back every sibling write if any publication step fails.
invitation_atomic_state() {
  query "SELECT jsonb_build_object('invitations',(SELECT count(*) FROM invitations WHERE tenant_id='$signup_org'),
    'mail',(SELECT count(*) FROM invitation_mail_intents WHERE tenant_id='$signup_org'),
    'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$signup_org'),
    'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$signup_org'),
    'receipts',(SELECT count(*) FROM invitation_creation_replays WHERE tenant_id='$signup_org'),
    'events',(SELECT count(*) FROM work_events WHERE tenant_id='$signup_org'),
    'stream',(SELECT jsonb_agg(to_jsonb(w) ORDER BY board_id) FROM work_event_streams w WHERE tenant_id='$signup_org'))::text;"
}
invitation_key="$(cat /proc/sys/kernel/random/uuid)"
invitation_payload='{"email":"atomic-mail-recipient@example.test","surface":"INTERNAL","targetRole":"MEMBER"}'
invitation_create() {
  curl --max-time 60 --silent --show-error -b "$scratch/signup-owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $invitation_key" -d "$invitation_payload" -o "$scratch/response" -w '%{http_code}' "$base${invitation_path:-/organizations/$signup_org/invitations}"
}
invitation_before="$(invitation_atomic_state)"
for table in invitation_mail_intents background_jobs audit_events invitation_creation_replays; do
  query "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(invitation_create)" = 503
  jq -e '.code=="invitation_storage_unavailable"' "$scratch/response" >/dev/null
  test "$invitation_before" = "$(invitation_atomic_state)"
  query "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
 done
test "$(signup_owner_post /boards "$(jq -nc --arg org "$signup_org" '{organizationId:$org,name:"Delivered Board invitation",visibility:"PRIVATE"}')")" = 201
mail_board="$(jq -r '.id' "$scratch/response")"
for target in INTERNAL PORTAL BOARD_ADMIN BOARD_MEMBER; do
  surface="$target"; role=MEMBER; board_role=''
  invitation_path="/organizations/$signup_org/invitations"
  if test "$target" = PORTAL; then role=OWNER; fi
  if [[ "$target" = BOARD_* ]]; then surface=INTERNAL; board_role="${target#BOARD_}"; invitation_path="/boards/$mail_board/invitations"; fi
  email="signup-mail-recipient-${target,,}-${RANDOM}-${RANDOM}@example.test"
  invitation_key="$(cat /proc/sys/kernel/random/uuid)"
  invitation_payload="$(jq -nc --arg email "$email" --arg surface "$surface" --arg role "$role" '{email:$email,surface:$surface,targetRole:$role}')"
  if test -n "$board_role"; then
    invitation_payload="$(jq -nc --arg email "$email" --arg role "$board_role" '{email:$email,role:$role}')"
    invitation_before="$(invitation_atomic_state)"
    for table in invitations invitation_mail_intents background_jobs audit_events work_events invitation_creation_replays; do
      query "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
      test "$(invitation_create)" = 503
      jq -e '.code=="invitation_storage_unavailable"' "$scratch/response" >/dev/null
      test "$invitation_before" = "$(invitation_atomic_state)"
      query "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
    done
  fi
  test "$(invitation_create)" = 201
  cp "$scratch/response" "$scratch/invitation-ack"
  test "$(invitation_create)" = 201
  cmp "$scratch/invitation-ack" "$scratch/response"
  signup_invitation="$(jq -r '.id' "$scratch/response")"
  scripts/ci/assert-file-excludes.sh 'invitationToken":"[A-Za-z0-9_-]+|tokenHash' "$scratch/response"
  invitation_job="$(query "SELECT job_id FROM invitation_mail_intents WHERE invitation_id='$signup_invitation' AND tenant_id='$signup_org';")"
  test -n "$invitation_job"
  if test -n "$board_role"; then
    jq -e --arg board "$mail_board" --arg role "$board_role" '.boardTarget.boardId==$board and .boardTarget.role==$role and .invitationToken==null' "$scratch/response" >/dev/null
    test "$(query "SELECT count(*) FROM invitation_mail_intents WHERE job_id='$invitation_job' AND target_board_id='$mail_board' AND target_board_role='$board_role';")" = 1
  fi
  for attempt in $(seq 1 90); do
    if test "$(query "SELECT state FROM invitation_mail_intents WHERE job_id='$invitation_job';")" = SENT; then break; fi
    sleep 1
  done
  test "$(query "SELECT state FROM invitation_mail_intents WHERE job_id='$invitation_job';")" = SENT
  invitation_provider_key="strataai-invitation/${signup_org//-/}/${signup_invitation//-/}"
  curl --fail --silent "$fixture/messages" > "$scratch/invitation-messages"
  signup_token="$(jq -r --arg key "$invitation_provider_key" '.[]|select(.key==$key)|.payload.text|capture("/invitation#token=(?<token>[A-Za-z0-9_-]+)").token' "$scratch/invitation-messages")"
  test -n "$signup_token"
  jq -e --arg key "$invitation_provider_key" --arg email "$email" '[.[]|select(.key==$key)]|length==1 and .[0].attempts>=2 and .[0].payload.to==[$email]' "$scratch/invitation-messages" >/dev/null
  if test -n "$board_role"; then
    jq --arg key "$invitation_provider_key" '.[]|select(.key==$key)|.payload' "$scratch/invitation-messages" > "$scratch/board-mail-body"
    scripts/ci/assert-file-excludes.sh "Delivered Board invitation|$mail_board" "$scratch/board-mail-body"
  fi
  signup_hash="$(printf '%s' "$signup_token" | sha256sum | cut -d ' ' -f 1)"
  test "$(query "SELECT count(*) FROM invitations WHERE id='$signup_invitation' AND token_hash='$signup_hash';")" = 1
  test "$(query "SELECT count(*) FROM background_jobs WHERE id='$invitation_job' AND job_type='INVITATION_EMAIL' AND service_identity='invitation-email-delivery' AND actor_id='$mail_owner_id' AND safe_metadata=jsonb_build_object('invitationId','$signup_invitation'::uuid);")" = 1
  delivery_attempts="$(jq -r --arg key "$invitation_provider_key" '.[]|select(.key==$key)|.attempts' "$scratch/invitation-messages")"
  # Recover the gap between persisted SENT and generic job completion without a send.
  # Freeze execution before setting the crash boundary. Otherwise the live
  # Worker can claim PENDING just before restart and leave a new two-minute
  # lease, making this ninety-second recovery check depend on scheduling.
  "${compose[@]}" -f scripts/ci/compose.invitation-signup-test.yml -f scripts/ci/compose.invitation-mail-test.yml stop worker >/dev/null
  query "UPDATE background_jobs SET state='RUNNING',attempt_count=1,available_at=clock_timestamp(),worker_id=gen_random_uuid(),lease_id=gen_random_uuid(),lease_expires_at=clock_timestamp()-interval '1 second' WHERE id='$invitation_job';" >/dev/null
  test "$(query "SELECT count(*) FROM background_jobs j JOIN invitation_mail_intents i ON i.job_id=j.id AND i.tenant_id=j.tenant_id WHERE j.id='$invitation_job' AND j.state='RUNNING' AND j.lease_expires_at<clock_timestamp() AND i.state='SENT';")" = 1
  "${compose[@]}" -f scripts/ci/compose.invitation-signup-test.yml -f scripts/ci/compose.invitation-mail-test.yml up -d --wait --wait-timeout 180 worker >/dev/null
  for attempt in $(seq 1 90); do
    if test "$(query "SELECT state FROM background_jobs WHERE id='$invitation_job';")" = SUCCEEDED; then break; fi
    sleep 1
  done
  test "$(query "SELECT state FROM background_jobs WHERE id='$invitation_job';")" = SUCCEEDED
  test "$(query "SELECT count(*) FROM background_jobs WHERE id='$invitation_job' AND attempt_count=2 AND worker_id IS NULL AND lease_id IS NULL AND lease_expires_at IS NULL;")" = 1
  test "$(curl --fail --silent "$fixture/messages" | jq -r --arg key "$invitation_provider_key" '.[]|select(.key==$key)|.attempts')" = "$delivery_attempts"
  query "SELECT to_jsonb(m)::text FROM invitation_mail_intents m WHERE job_id='$invitation_job';" > "$scratch/invitation-ledger"
  scripts/ci/assert-file-excludes.sh "$signup_token" "$scratch/invitation-ledger"
  curl --fail --silent --show-error -b "$scratch/signup-owner.cookies" "$base/organizations/$signup_org/invitations" > "$scratch/invitation-history"
  if test -n "$board_role"; then
    curl --fail --silent --show-error -b "$scratch/signup-owner.cookies" "$base/boards/$mail_board/invitations" > "$scratch/board-history"
    jq -e --arg id "$signup_invitation" --arg board "$mail_board" --arg role "$board_role" --arg email "$email" '.items|map(select(.id==$id))|length==1 and .[0].email==$email and .[0].deliveryState=="SENT" and .[0].boardTarget.boardId==$board and .[0].boardTarget.role==$role' "$scratch/board-history" >/dev/null
    scripts/ci/assert-file-excludes.sh "$signup_token|tokenHash|providerReceipt|safeMetadata|providerAccount" "$scratch/board-history"
    # Organization history currently excludes Board invitations; no false history completion claim.
    jq -e --arg id "$signup_invitation" '.items|map(select(.id==$id))|length==0' "$scratch/invitation-history" >/dev/null
  else
  jq -e --arg id "$signup_invitation" --arg email "$email" '.items|map(select(.id==$id))|length==1 and .[0].email==$email and .[0].deliveryState=="SENT"' "$scratch/invitation-history" >/dev/null
  fi
  scripts/ci/assert-file-excludes.sh "$signup_token|tokenHash|providerReceipt|safeMetadata|providerAccount" "$scratch/invitation-history"
  signup_body="$(jq -nc --arg email "$email" --arg token "$signup_token" '{email:$email,password:"signup-mail-correct-horse",displayName:"Invited mail recipient",invitationToken:$token}')"
  test "$(post /auth/register "$(jq 'del(.invitationToken)' <<< "$signup_body")")" = 403
  signup_key="$(cat /proc/sys/kernel/random/uuid)"
  signup_register() { curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $signup_key" -d "$signup_body" -o "$scratch/response" -w '%{http_code}' "$base/auth/register"; }
  test "$(signup_register)" = 201
  jq -e '.verificationToken==null and .user.emailVerified==false' "$scratch/response" >/dev/null
  signup_user="$(jq -r '.user.id' "$scratch/response")"; cp "$scratch/response" "$scratch/signup-original"
  test "$(signup_register)" = 201; cmp "$scratch/signup-original" "$scratch/response"
  test "$(query "SELECT status FROM users WHERE id='$signup_user';")" = PENDING_VERIFICATION
  test "$(query "SELECT count(*) FROM organization_members WHERE tenant_id='$signup_org' AND user_id='$signup_user';")" = 0
  test "$(query "SELECT count(*) FROM portal_access WHERE tenant_id='$signup_org' AND user_id='$signup_user';")" = 0
  test "$(query "SELECT count(*) FROM invitations WHERE id='$signup_invitation' AND accepted_at IS NULL;")" = 1
  if test -n "$board_role"; then
    test "$(query "SELECT count(*) FROM board_members WHERE board_id='$mail_board' AND user_id='$signup_user';")" = 0
  fi
  test "$(post /auth/login "$signup_body")" = 403
  signup_job="$(latest_job VERIFY_EMAIL)"; test -n "$signup_job"; wait_state "$signup_job" SENT
  mail_proof="$(mail_token verify "${signup_job//-/}")"; test -n "$mail_proof"
  test "$(post /auth/verify-email "$(jq -nc --arg token "$mail_proof" '{token:$token}')")" = 200
  test "$(query "SELECT count(*) FROM organization_members WHERE tenant_id='$signup_org' AND user_id='$signup_user';")" = 0
  curl --fail --silent --show-error -c "$scratch/signup-recipient.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$signup_body" "$base/auth/login" >/dev/null
  if test -n "$board_role"; then
    test "$(query "SELECT count(*) FROM board_members WHERE board_id='$mail_board' AND user_id='$signup_user';")" = 0
    test "$(curl --max-time 60 --silent --show-error -b "$scratch/signup-recipient.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg token "$signup_token" '{token:$token}')" -o "$scratch/response" -w '%{http_code}' "$base/invitations/review")" = 200
    jq -e --arg board "$mail_board" --arg role "$board_role" '.boardTarget.boardId==$board and .boardTarget.role==$role and .boardName=="Delivered Board invitation"' "$scratch/response" >/dev/null
    curl --fail --silent --show-error -b "$scratch/signup-recipient.cookies" "$base/me/invitations" > "$scratch/board-discovery"
    jq -e --arg id "$signup_invitation" --arg board "$mail_board" '.items|any(.id==$id and .boardTarget.boardId==$board)' "$scratch/board-discovery" >/dev/null
    scripts/ci/assert-file-excludes.sh "$signup_token|tokenHash|acceptedByUserId" "$scratch/board-discovery"
  fi
  test "$(curl --max-time 60 --silent --show-error -b "$scratch/signup-recipient.cookies" -H 'X-StrataAI-Request: 1' -X POST -o "$scratch/response" -w '%{http_code}' "$base/me/invitations/$signup_invitation/accept")" = 200
  jq -e --arg id "$signup_invitation" --arg surface "$surface" '.invitationId==$id and .surface==$surface' "$scratch/response" >/dev/null
  count=0; if test "$surface" = INTERNAL; then count=1; fi
  test "$(query "SELECT count(*) FROM organization_members WHERE tenant_id='$signup_org' AND user_id='$signup_user' AND role='MEMBER' AND status='ACTIVE';")" = "$count"
  if test -n "$board_role"; then
    jq -e --arg board "$mail_board" --arg role "$board_role" '.boardTarget.boardId==$board and .boardTarget.role==$role' "$scratch/response" >/dev/null
    test "$(query "SELECT count(*) FROM board_members WHERE board_id='$mail_board' AND user_id='$signup_user' AND role='$board_role' AND status='ACTIVE';")" = 1
  fi
  scripts/ci/assert-file-excludes.sh "$signup_token|$mail_proof|invitationToken|tokenHash" "$scratch/response"
  test "$(curl --max-time 60 --silent --show-error -b "$scratch/signup-recipient.cookies" -o "$scratch/invitation-history-denied" -w '%{http_code}' "$base/organizations/$signup_org/invitations")" = 404
  scripts/ci/assert-file-excludes.sh "$email|$signup_invitation|$signup_token|deliveryState" "$scratch/invitation-history-denied"
done
unset signup_token signup_hash mail_proof signup_body
"${compose[@]}" up -d --wait --wait-timeout 180 api worker >/dev/null; signup_closed=false
echo 'Exact-image Board invitation mail: both roles, public signed issuance, sibling publication rollback, actual scoped Worker/provider delivery, verified proof review/discovery and explicit grants passed.'
echo 'Exact-image invitation mail: atomic invitation/job/snapshot/audit/receipt rollback, keyed single publication, reference-only metadata, separate scoped Worker delivery, provider lost-acknowledgment idempotency, SENT completion recovery and no stored bearer passed.'
echo 'Exact-image closed invitation signup: pending verification, retry acknowledgment, separate Worker verification and explicit Internal/Portal acceptance without premature grants passed.'
echo 'Keyed production recovery: neutral atomic rollback, concurrent one-token/one-delivery publication, restart, single-use proof and actual Worker receipt retention passed.'
echo 'Keyed production token consumption: atomic user/token/session/audit/event/receipt rollback, concurrent acknowledgment, restart, newer-session preservation, post-wait expiry, lifecycle denial and Worker cleanup passed.'
echo 'Exact-image identity email, atomic publication, restart/rotation, provider retry/idempotency, verification/reset/replay/revocation, cancellation and resend checks passed.'
