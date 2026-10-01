#!/usr/bin/env bash
set -euo pipefail
# PRD-03-TC-05/08, AC-WS-03-02: rollback, post-wait authorization and owner continuity.
test "${CI:-}" = true || { echo 'Disposable organization fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
gate_pid=''
request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Organization command check failed at line $LINENO" >&2' ERR
account() {
  local label="$1" body
  body="$(jq -nc --arg email "org-commands-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"organization-correct-horse-battery",displayName:"Organization fixture"}')"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/$label.json"
  curl --fail --silent --show-error -c "$scratch/$label.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
}
request() {
  curl --max-time 60 --silent --show-error -b "$scratch/${4:-owner}.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X "$1" -d "$3" -o "$scratch/${5:-response}.json" -w '%{http_code}' "$BASE_URL$2"
}
account owner
account other
account guest
account portal
owner="$(jq -r '.user.id' "$scratch/owner.json")"
other="$(jq -r '.user.id' "$scratch/other.json")"
guest="$(jq -r '.user.id' "$scratch/guest.json")"
portal_user="$(jq -r '.user.id' "$scratch/portal.json")"
test "$(request POST /organizations '{"name":"Atomic organization"}')" = 201
organization="$(jq -r '.organization.id' "$scratch/response.json")"
for id in "$owner" "$other" "$guest" "$portal_user" "$organization"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$organization','$other','OWNER','ACTIVE');" >/dev/null
admin "UPDATE users SET email_verified=true,status='ACTIVE' WHERE id IN ('$owner','$other','$guest','$portal_user');" >/dev/null
fixture_invite() {
  local actor="$1" surface="$2" role="$3" token hash email
  token="$(openssl rand -hex 32)"
  hash="$(printf '%s' "$token" | sha256sum | cut -d ' ' -f 1)"
  email="$(jq -r '.user.email' "$scratch/$actor.json")"
  admin "INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
    VALUES(gen_random_uuid(),'$organization','$email',upper('$email'),'$hash','$surface','$role','$owner',now(),now()+interval '1 day');" >/dev/null
  printf '%s' "$token"
}
internal_token="$(fixture_invite guest INTERNAL MEMBER)"
portal_token="$(fixture_invite portal PORTAL OWNER)"
revoke_id="$(admin "SELECT id FROM invitations WHERE tenant_id='$organization' AND target_surface='INTERNAL';")"
state() {
  admin "SELECT jsonb_build_object('organization',(SELECT to_jsonb(o) FROM organizations o WHERE id='$organization'),
    'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM organization_members m WHERE tenant_id='$organization'),
    'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$organization'),
    'invitations',(SELECT jsonb_agg(to_jsonb(i) ORDER BY id) FROM invitations i WHERE tenant_id='$organization'),
    'portal',(SELECT count(*) FROM portal_access WHERE tenant_id='$organization'),
    'owned',(SELECT count(*) FROM organizations WHERE owner_user_id='$owner'))::text;"
}
before="$(state)"
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
rejected() {
  test "$(request "$1" "$2" "$3" "${4:-owner}")" = 503
  jq -e --arg code "${5:-organization_storage_unavailable}" '.code==$code and .status==503' "$scratch/response.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'audit_events|Npgsql|permission denied|INSERT INTO|Atomic organization' "$scratch/response.json"
  test "$before" = "$(state)"
}
# Creation must roll back both organization and owner membership when audit insert fails.
rejected POST /organizations '{"name":"Must roll back"}'
rejected PATCH "/organizations/$organization" '{"name":"Must roll back","version":1}'
rejected DELETE "/organizations/$organization/members/$other" '{}'
rejected POST "/organizations/$organization/leave" '{}'
rejected DELETE "/organizations/$organization?version=1" '{}'
rejected POST "/organizations/$organization/invitations" '{"email":"rollback@example.test","surface":"INTERNAL","targetRole":"MEMBER"}' owner invitation_storage_unavailable
rejected DELETE "/organizations/$organization/invitations/$revoke_id" '{}' owner invitation_storage_unavailable
rejected POST "/invitations/$internal_token/accept" '{}' guest invitation_storage_unavailable
rejected POST "/invitations/$portal_token/accept" '{}' portal invitation_storage_unavailable
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$(request POST "/invitations/$internal_token/accept" '{}' guest)" = 200
test "$(admin "SELECT role FROM organization_members WHERE tenant_id='$organization' AND user_id='$guest' AND status='ACTIVE';")" = MEMBER
test "$(request POST "/invitations/$internal_token/accept" '{}' guest)" = 400
test "$(request POST "/invitations/$portal_token/accept" '{}' portal)" = 200
test "$(admin "SELECT count(*) FROM portal_access WHERE tenant_id='$organization' AND user_id='$portal_user' AND status='ACTIVE';")" = 1
test "$(admin "SELECT count(*) FROM organization_members WHERE tenant_id='$organization' AND user_id='$portal_user';")" = 0
owner_downgrade="$(fixture_invite owner INTERNAL MEMBER)"
test "$(request POST "/invitations/$owner_downgrade/accept" '{}')" = 409
jq -e '.code=="ownership_change_requires_confirmation"' "$scratch/response.json" >/dev/null
test "$(admin "SELECT role FROM organization_members WHERE tenant_id='$organization' AND user_id='$owner';")" = OWNER
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
# Acceptance must recheck issuer role after its membership lock wait, leave the
# token unconsumed and avoid rewriting an existing recipient membership.
waiting_token="$(fixture_invite guest INTERNAL MEMBER)"
waiting_hash="$(printf '%s' "$waiting_token" | sha256sum | cut -d ' ' -f 1)"
guest_version="$(admin "SELECT version FROM organization_members WHERE tenant_id='$organization' AND user_id='$guest';")"
waiting_audits="$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization';")"
hold "SELECT user_id FROM organization_members WHERE tenant_id='$organization' AND user_id='$owner' FOR UPDATE;"
request POST "/invitations/$waiting_token/accept" '{}' guest > "$scratch/status" &
request_pid=$!
blocked '%SELECT user_id FROM organization_members%FOR UPDATE%'
release "UPDATE organization_members SET status='SUSPENDED' WHERE tenant_id='$organization' AND user_id='$owner';"
wait "$request_pid"
request_pid=''
test "$(cat "$scratch/status")" = 400
jq -e '.code=="invalid_or_expired_invitation"' "$scratch/response.json" >/dev/null
test "$(admin "SELECT accepted_at IS NULL FROM invitations WHERE token_hash='$waiting_hash';")" = t
test "$(admin "SELECT version FROM organization_members WHERE tenant_id='$organization' AND user_id='$guest';")" = "$guest_version"
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization';")" = "$waiting_audits"
admin "UPDATE organization_members SET status='ACTIVE' WHERE tenant_id='$organization' AND user_id='$owner';" >/dev/null
# The verified recipient's current account state is locked and checked too.
hold "SELECT id FROM users WHERE id='$guest' FOR UPDATE;"
request POST "/invitations/$waiting_token/accept" '{}' guest > "$scratch/status" &
request_pid=$!
blocked '%FROM users WHERE id =%FOR SHARE%'
release "UPDATE users SET status='DEACTIVATED' WHERE id='$guest';"
wait "$request_pid"
request_pid=''
test "$(cat "$scratch/status")" = 401
jq -e '.code=="session_unavailable"' "$scratch/response.json" >/dev/null
test "$(admin "SELECT accepted_at IS NULL FROM invitations WHERE token_hash='$waiting_hash';")" = t
test "$(admin "SELECT version FROM organization_members WHERE tenant_id='$organization' AND user_id='$guest';")" = "$guest_version"
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization';")" = "$waiting_audits"
admin "UPDATE users SET status='ACTIVE' WHERE id='$guest';" >/dev/null
# Membership may remain active for historical attribution after account deactivation.
# Such an issuer cannot authorize acceptance, including deactivation during its read wait.
hold "SELECT id FROM users WHERE id='$owner' FOR UPDATE;"
request POST "/invitations/$waiting_token/accept" '{}' guest > "$scratch/status" &
request_pid=$!
blocked '%FROM users WHERE id =%FOR SHARE%'
release "UPDATE users SET status='DEACTIVATED' WHERE id='$owner';"
wait "$request_pid"
request_pid=''
test "$(cat "$scratch/status")" = 400
jq -e '.code=="invalid_or_expired_invitation"' "$scratch/response.json" >/dev/null
test "$(admin "SELECT accepted_at IS NULL FROM invitations WHERE token_hash='$waiting_hash';")" = t
test "$(admin "SELECT version FROM organization_members WHERE tenant_id='$organization' AND user_id='$guest';")" = "$guest_version"
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization';")" = "$waiting_audits"
admin "UPDATE users SET status='ACTIVE' WHERE id='$owner';" >/dev/null
# A membership revocation committed during the lock wait must invalidate the actor.
hold "SELECT user_id FROM organization_members WHERE tenant_id='$organization' AND user_id='$owner' FOR UPDATE;"
request PATCH "/organizations/$organization" '{"name":"Revoked edit","version":1}' > "$scratch/status" &
request_pid=$!
blocked '%SELECT user_id FROM organization_members%FOR UPDATE%'
release "UPDATE organization_members SET status='SUSPENDED',version=version+1 WHERE tenant_id='$organization' AND user_id='$owner';"
wait "$request_pid"
request_pid=''
test "$(cat "$scratch/status")" = 404
jq -e '.code=="organization_not_found"' "$scratch/response.json" >/dev/null
test "$(admin "SELECT name||':'||version FROM organizations WHERE id='$organization';")" = 'Atomic organization:1'
# The old creator's invitation cannot grant access after that creator loses permission.
test "$(request POST "/invitations/$owner_downgrade/accept" '{}')" = 400
jq -e '.code=="invalid_or_expired_invitation"' "$scratch/response.json" >/dev/null
admin "UPDATE organization_members SET status='ACTIVE' WHERE tenant_id='$organization' AND user_id='$owner';" >/dev/null
owner_hash() {
  awk '$6=="strataai_session" {print $7}' "$scratch/owner.cookies" | tr -d '\n' | sha256sum | cut -d ' ' -f 1
}
login_owner() {
  local body
  body="$(jq -nc --arg email "$(jq -r '.user.email' "$scratch/owner.json")" '{email:$email,password:"organization-correct-horse-battery"}')"
  curl --fail --silent --show-error -c "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
}
logout_during_wait() {
  local lock="$1" query="$2" method="$3" route="$4" body="$5" before hash
  before="$(state)"
  hash="$(owner_hash)"
  hold "$lock"
  request "$method" "$route" "$body" > "$scratch/status" &
  request_pid=$!
  blocked "$query"
  release "UPDATE sessions SET revoked_at=clock_timestamp() WHERE token_hash='$hash';"
  wait "$request_pid"
  request_pid=''
  test "$(cat "$scratch/status")" = 401
  jq -e '.code=="session_unavailable"' "$scratch/response.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Atomic organization|Npgsql|SELECT|token_hash|strataai_session' "$scratch/response.json"
  test "$before" = "$(state)"
  login_owner
}
logout_during_wait "SELECT id FROM users WHERE id='$owner' FOR UPDATE;" '%FROM users WHERE id =%FOR SHARE%' POST /organizations '{"name":"Logged out creation"}'
logout_during_wait "SELECT id FROM organizations WHERE id='$organization' FOR UPDATE;" '%SELECT id FROM organizations%FOR UPDATE%' PATCH "/organizations/$organization" '{"name":"Logged out edit","version":1}'
logout_during_wait "SELECT id FROM organizations WHERE id='$organization' FOR UPDATE;" '%SELECT id FROM organizations%FOR UPDATE%' POST "/organizations/$organization/invitations" '{"email":"logged-out@example.test","surface":"INTERNAL","targetRole":"MEMBER"}'
# Force both departure requests to wait on the same organization gate. After release,
# exactly one commits; the second reads the committed owner count and rejects departure.
audit_before="$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization';")"
[[ "$audit_before" =~ ^[0-9]+$ ]]
hold "SELECT id FROM organizations WHERE id='$organization' FOR UPDATE;"
request POST "/organizations/$organization/leave" '{}' owner first > "$scratch/first.status" &
first_pid=$!
request POST "/organizations/$organization/leave" '{}' other second > "$scratch/second.status" &
second_pid=$!
blocked '%SELECT id FROM organizations%FOR UPDATE%' 2
release ''
wait "$first_pid"
wait "$second_pid"
# curl writes no newline, so inspect the two statuses explicitly.
first_status="$(cat "$scratch/first.status")"
second_status="$(cat "$scratch/second.status")"
if test "$first_status" = 204; then
  test "$second_status" = 409
  jq -e '.code=="sole_owner"' "$scratch/second.json" >/dev/null
else
  test "$first_status" = 409
  test "$second_status" = 204
  jq -e '.code=="sole_owner"' "$scratch/first.json" >/dev/null
fi
test "$(admin "SELECT count(*) FROM organization_members WHERE tenant_id='$organization' AND role='OWNER' AND status='ACTIVE';")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization';")" = "$((audit_before + 1))"
echo 'Organization mutations/audits roll back together, revoked actors fail after waits, and concurrent departures preserve one owner.'
