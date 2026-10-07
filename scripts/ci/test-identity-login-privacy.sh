#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable authentication privacy fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
user=''
original_status=''
original_verified=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$original_status" && [[ "$original_verified" =~ ^(true|false)$ ]]; then
    admin "UPDATE users SET status='$original_status',email_verified=$original_verified WHERE id='$user';" >/dev/null
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Authentication privacy check failed at line $LINENO" >&2' ERR
uuid() { python3 -c 'import uuid; print(uuid.uuid4())'; }
email="login-privacy-$(uuid)@example.test"
unknown="unknown-login-privacy-$(uuid)@example.test"
body="$(jq -nc --arg email "$email" '{email:$email,password:"login-privacy-correct-horse",displayName:"Protected privacy subject"}')"
curl --max-time 60 --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$body" "$BASE_URL/auth/register" > "$scratch/user"
user="$(jq -er '.user.id' "$scratch/user")"
[[ "$user" =~ ^[0-9a-f-]{36}$ ]]
original_status="$(admin "SELECT status FROM users WHERE id='$user';")"
original_verified="$(admin "SELECT email_verified FROM users WHERE id='$user';")"
[[ "$original_status" =~ ^(ACTIVE|PENDING_VERIFICATION)$ ]]
[[ "$original_verified" =~ ^(t|f)$ ]]
if test "$original_verified" = t; then original_verified=true; else original_verified=false; fi
state() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$user'),
    'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$user'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id='$user' AND tenant_id IS NULL),
    'stream',(SELECT last_sequence FROM identity_event_streams WHERE user_id='$user'),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id='$user'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_login_replays r WHERE user_id='$user'))::text;"
}
refuse() {
  local target="$1" key="$2" before="$3" request status
  request="$(jq -nc --arg email "$target" '{email:($email|ascii_upcase),password:"incorrect-private-login-password"}')"
  status="$(curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $key" -d "$request" -D "$scratch/headers" -o "$scratch/response" -w '%{http_code}' "$BASE_URL/auth/login")"
  test "$status" = 401
  jq -e '.code=="invalid_credentials" and .status==401' "$scratch/response" >/dev/null
  jq -Sc '{status,title,type,code,detail}' "$scratch/response" > "$scratch/refusal.current"
  if test -f "$scratch/refusal.expected"; then cmp "$scratch/refusal.expected" "$scratch/refusal.current";
  else cp "$scratch/refusal.current" "$scratch/refusal.expected"; fi
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  scripts/ci/assert-file-excludes.sh "$user|[Pp]rotected privacy subject|[Ll][Oo][Gg][Ii][Nn]-[Pp][Rr][Ii][Vv][Aa][Cc][Yy]-|incorrect-private-login-password|email_verification_required|account_unavailable" "$scratch/response"
  test "$before" = "$(state)"
}
# AUTH-FR-009 / PRD-02-TC-03/04/07: lifecycle setup touches only this fixture
# account. Each pair must remain a generic refusal even with its original UUID.
for lifecycle in ACTIVE PENDING_VERIFICATION SUSPENDED DEACTIVATED; do
  verified=true; if test "$lifecycle" = PENDING_VERIFICATION; then verified=false; fi
  admin "UPDATE users SET status='$lifecycle',email_verified=$verified WHERE id='$user';" >/dev/null
  before="$(state)"; key="$(uuid)"
  refuse "$email" "$key" "$before"
  refuse "$email" "$key" "$before"
done
before="$(state)"; key="$(uuid)"
refuse "$unknown" "$key" "$before"
refuse "$unknown" "$key" "$before"
test "$(admin "SELECT count(*) FROM users WHERE id='00000000-0000-0000-0000-000000000000' OR email_normalized=upper('$unknown');")" = 0
test "$(admin "SELECT count(*) FROM sessions WHERE user_id='$user';")" = 0
test "$(admin "SELECT count(*) FROM identity_login_replays WHERE user_id='$user';")" = 0
echo 'Authentication privacy: ten lifecycle/unknown refusals preserve full account state.'
