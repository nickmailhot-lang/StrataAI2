#!/usr/bin/env bash
set -euo pipefail
base=http://localhost:8080
fixture=http://localhost:19090
compose=(docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.identity-test.yml)
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Identity mail integration failed at line $LINENO" >&2' ERR
query() { docker compose -f compose.release.yml exec -T postgres psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB" --quiet --tuples-only --no-align --command="$1"; }
post() {
  curl --silent --show-error -o "$scratch/response" -w '%{http_code}' -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$2" "$base$1"
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
"${compose[@]}" up -d --wait --wait-timeout 180 api

# A publication failure cannot leave a user or verification token committed.
query "CREATE FUNCTION ci_reject_identity_publication() RETURNS trigger LANGUAGE plpgsql AS \$\$ BEGIN RAISE EXCEPTION 'fixture publication rejected'; END; \$\$; CREATE TRIGGER ci_reject_identity_publication BEFORE INSERT ON identity_delivery_jobs FOR EACH ROW EXECUTE FUNCTION ci_reject_identity_publication();" >/dev/null
email="rollback-${RANDOM}-${RANDOM}@example.test"
test "$(post /auth/register "$(jq -nc --arg email "$email" '{email:$email,password:"mail-correct-horse-battery",displayName:"Mail rollback"}')")" = 500
test "$(query "SELECT count(*) FROM users WHERE email='$email';")" = 0
query 'DROP TRIGGER ci_reject_identity_publication ON identity_delivery_jobs; DROP FUNCTION ci_reject_identity_publication();' >/dev/null

email="identity-${RANDOM}-${RANDOM}@example.test"
password=mail-correct-horse-battery
test "$(post /auth/register "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password,displayName:"Identity delivery"}')")" = 201
jq -e '.verificationToken==null and .user.emailVerified==false' "$scratch/response" >/dev/null
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
test "$(post /auth/verify-email "$(jq -nc --arg token "$verification" '{token:$token}')")" = 200
test "$(post /auth/verify-email "$(jq -nc --arg token "$verification" '{token:$token}')")" = 400
curl --fail --silent -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" --arg password "$password" '{email:$email,password:$password}')" "$base/auth/login" >/dev/null

test "$(post /auth/password/forgot '{"email":"unknown-identity@example.test"}')" = 202
jq -e '.accepted==true and .resetToken==null' "$scratch/response" >/dev/null
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
test "$(post /auth/password/reset "$(jq -nc --arg token "$reset" '{token:$token,newPassword:"replacement-correct-horse"}')")" = 200
test "$(post /auth/password/reset "$(jq -nc --arg token "$reset" '{token:$token,newPassword:"another-replacement-horse"}')")" = 400
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
