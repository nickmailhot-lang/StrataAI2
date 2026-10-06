#!/usr/bin/env bash
set -euo pipefail
# PRD-03-TC-05/08, AC-WS-03-02: rollback, post-wait authorization and owner continuity.
test "${CI:-}" = true || { echo 'Disposable organization fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
gate_pid=''
request_pid=''
metadata_session=''
metadata_session_expiry=''
departure_session=''
departure_session_expiry=''
removal_session=''
removal_session_expiry=''
creation_session=''
creation_session_expiry=''
deletion_session=''
deletion_session_expiry=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
  admin 'GRANT INSERT ON organization_deletion_replays TO strataai_api_runtime;' >/dev/null
  admin 'GRANT INSERT ON organization_creation_replays TO strataai_api_runtime;' >/dev/null
  admin 'GRANT INSERT ON organization_metadata_replays TO strataai_api_runtime;' >/dev/null
  admin 'GRANT INSERT ON organization_removal_replays TO strataai_api_runtime;' >/dev/null
  admin 'GRANT INSERT ON organization_departure_replays TO strataai_api_runtime;' >/dev/null
  admin 'DROP TRIGGER IF EXISTS ci_organization_metadata_wait ON organization_metadata_replays; DROP FUNCTION IF EXISTS public.ci_organization_metadata_wait();' >/dev/null
  admin 'DROP TRIGGER IF EXISTS ci_organization_departure_wait ON organization_departure_replays; DROP FUNCTION IF EXISTS public.ci_organization_departure_wait();' >/dev/null
  admin 'DROP TRIGGER IF EXISTS ci_organization_removal_wait ON organization_removal_replays; DROP FUNCTION IF EXISTS public.ci_organization_removal_wait();' >/dev/null
  admin 'DROP TRIGGER IF EXISTS ci_organization_creation_wait ON organization_creation_replays; DROP FUNCTION IF EXISTS public.ci_organization_creation_wait();' >/dev/null
  admin 'DROP TRIGGER IF EXISTS ci_organization_deletion_wait ON organization_deletion_replays; DROP FUNCTION IF EXISTS public.ci_organization_deletion_wait();' >/dev/null
  if test -n "$deletion_session" && test -n "$deletion_session_expiry"; then
    admin "UPDATE sessions SET expires_at='$deletion_session_expiry'::timestamptz WHERE id='$deletion_session';" >/dev/null
  fi
  if test -n "$creation_session" && test -n "$creation_session_expiry"; then
    admin "UPDATE sessions SET expires_at='$creation_session_expiry'::timestamptz WHERE id='$creation_session';" >/dev/null
  fi
  if test -n "$removal_session" && test -n "$removal_session_expiry"; then
    admin "UPDATE sessions SET expires_at='$removal_session_expiry'::timestamptz WHERE id='$removal_session';" >/dev/null
  fi
  if test -n "$departure_session" && test -n "$departure_session_expiry"; then
    admin "UPDATE sessions SET expires_at='$departure_session_expiry'::timestamptz WHERE id='$departure_session';" >/dev/null
  fi
  if test -n "$metadata_session" && test -n "$metadata_session_expiry"; then
    admin "UPDATE sessions SET expires_at='$metadata_session_expiry'::timestamptz WHERE id='$metadata_session';" >/dev/null
  fi
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
# PRD-03-TC-03/04: URL validation never mutates metadata/audit and does not
# reveal its validation rule to an account without internal membership.
for logo in 'javascript:alert(1)' 'http://example.test/logo.png' 'https://user:password@example.test/logo.png' 'not-a-url' "https://example.test/$(printf '%02050d' 0)"; do
  body="$(jq -nc --arg logo "$logo" '{name:"Invalid metadata",logoUrl:$logo,version:1}')"
  test "$(request PATCH "/organizations/$organization" "$body" portal)" = 404
  test "$(request PATCH "/organizations/$organization" "$body")" = 400
  jq -e '.code=="invalid_organization_logo_url" and .status==400' "$scratch/response.json" >/dev/null
  test "$before" = "$(state)"
done
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

# PRD-03-TC-06/07: durable metadata acknowledgment is independent of later edits.
test "$(request POST /organizations '{"name":"Metadata retry fixture"}')" = 201
retry_org="$(jq -r '.organization.id' "$scratch/response.json")"
[[ "$retry_org" =~ ^[0-9a-fA-F-]{36}$ ]]
retry_key="$(cat /proc/sys/kernel/random/uuid)"
retry_metadata() {
  curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $retry_key" -X PATCH -d "$1" \
    -D "$scratch/${2:-metadata-reply}.headers" -o "$scratch/${2:-metadata-reply}.json" -w '%{http_code}' "$BASE_URL/organizations/$retry_org"
}
metadata_state() {
  admin "SELECT jsonb_build_object('organization',(SELECT to_jsonb(o) FROM organizations o WHERE id='$retry_org'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$retry_org'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM organization_metadata_replays r WHERE tenant_id='$retry_org'))::text;"
}
retry_body='{"name":"First metadata edit","description":"Original acknowledgment","version":1}'
metadata_before="$(metadata_state)"
admin 'REVOKE INSERT ON organization_metadata_replays FROM strataai_api_runtime;' >/dev/null
test "$(retry_metadata "$retry_body")" = 503
jq -e '.code=="organization_storage_unavailable"' "$scratch/metadata-reply.json" >/dev/null
test "$metadata_before" = "$(metadata_state)"
admin 'GRANT INSERT ON organization_metadata_replays TO strataai_api_runtime;' >/dev/null
# Observe actual receipt publication while the original cookie session expires.
metadata_hash="$(owner_hash)"
[[ "$metadata_hash" =~ ^[0-9a-f]{64}$ ]]
metadata_session="$(admin "SELECT id FROM sessions WHERE token_hash='$metadata_hash' AND user_id='$owner' AND revoked_at IS NULL;")"
[[ "$metadata_session" =~ ^[0-9a-fA-F-]{36}$ ]]
metadata_session_expiry="$(admin "SELECT expires_at FROM sessions WHERE id='$metadata_session';")"
test -n "$metadata_session_expiry"
admin "CREATE FUNCTION public.ci_organization_metadata_wait() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER AS \$\$
BEGIN
  IF NEW.tenant_id='$retry_org'::uuid THEN PERFORM pg_sleep(12); END IF;
  RETURN NEW;
END;
\$\$;
CREATE TRIGGER ci_organization_metadata_wait AFTER INSERT ON organization_metadata_replays
  FOR EACH ROW EXECUTE FUNCTION public.ci_organization_metadata_wait();" >/dev/null
admin "UPDATE sessions SET expires_at=clock_timestamp()+interval '10 seconds' WHERE id='$metadata_session';" >/dev/null
metadata_publication_before="$(metadata_state)"
metadata_identity_before="$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$owner'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$owner'))::text;")"
retry_metadata "$retry_body" metadata-expiry > "$scratch/metadata-expiry.status" & request_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_metadata_replays%';")" = 1; then break; fi
  sleep 0.1
done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_metadata_replays%';")" = 1
wait "$request_pid"; request_pid=''
test "$(cat "$scratch/metadata-expiry.status")" = 401
jq -e '.code=="session_unavailable"' "$scratch/metadata-expiry.json" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/metadata-expiry.headers"
scripts/ci/assert-file-excludes.sh "$retry_org|$owner|First metadata edit|Original acknowledgment" "$scratch/metadata-expiry.json"
test "$metadata_publication_before" = "$(metadata_state)"
test "$metadata_identity_before" = "$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$owner'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$owner'))::text;")"
admin 'DROP TRIGGER ci_organization_metadata_wait ON organization_metadata_replays; DROP FUNCTION public.ci_organization_metadata_wait();' >/dev/null
admin "UPDATE sessions SET expires_at='$metadata_session_expiry'::timestamptz WHERE id='$metadata_session';" >/dev/null
metadata_session=''; metadata_session_expiry=''
test "$metadata_before" = "$(metadata_state)"
hold "SELECT id FROM organizations WHERE id='$retry_org' FOR UPDATE;"
retry_metadata "$retry_body" metadata-first > "$scratch/metadata-first.status" & metadata_first_pid=$!
retry_metadata "$retry_body" metadata-second > "$scratch/metadata-second.status" & metadata_second_pid=$!
blocked '%SELECT id FROM organizations%FOR UPDATE%' 2
release ''
wait "$metadata_first_pid"; wait "$metadata_second_pid"
test "$(cat "$scratch/metadata-first.status")" = 200
test "$(cat "$scratch/metadata-second.status")" = 200
test "$(jq -Sc . "$scratch/metadata-first.json")" = "$(jq -Sc . "$scratch/metadata-second.json")"
test "$(admin "SELECT count(*)=1 FROM organization_metadata_replays WHERE tenant_id='$retry_org';")" = t
test "$(admin "SELECT count(*)=1 FROM audit_events WHERE tenant_id='$retry_org' AND event_type='ORGANIZATION_UPDATED';")" = t
cp "$scratch/metadata-first.json" "$scratch/original-metadata.json"
jq -e '.name=="First metadata edit" and .version==2' "$scratch/original-metadata.json" >/dev/null
test "$(request PATCH "/organizations/$retry_org" '{"name":"Later metadata edit","version":2}')" = 200
metadata_after="$(metadata_state)"
test "$(admin "BEGIN; SET LOCAL ROLE strataai_api_runtime; SELECT set_config('app.tenant_id','$organization',true) IS NOT NULL; SELECT count(*) FROM organization_metadata_replays WHERE tenant_id='$retry_org'; ROLLBACK;" | tail -n1)" = 0
test "$(admin "SELECT has_table_privilege('strataai_worker_runtime','organization_metadata_replays','SELECT') OR has_table_privilege('strataai_api_runtime','organization_metadata_replays','UPDATE') OR has_table_privilege('strataai_api_runtime','organization_metadata_replays','DELETE');")" = f
test "$(retry_metadata "$retry_body")" = 200
test "$(jq -Sc . "$scratch/metadata-reply.json")" = "$(jq -Sc . "$scratch/original-metadata.json")"
test "$metadata_after" = "$(metadata_state)"
test "$(retry_metadata '{"name":"Different metadata edit","version":1}')" = 409
jq -e '.code=="idempotency_conflict"' "$scratch/metadata-reply.json" >/dev/null
test "$metadata_after" = "$(metadata_state)"
# Force expiry relative to wall clock without deleting/reusing the reserved key.
admin "UPDATE organization_metadata_replays SET expires_at=clock_timestamp()-interval '1 second',created_at=clock_timestamp()-interval '2 seconds' WHERE tenant_id='$retry_org' AND key_id='$retry_key';" >/dev/null
metadata_expired="$(metadata_state)"
test "$(retry_metadata "$retry_body")" = 409
jq -e '.code=="idempotency_expired"' "$scratch/metadata-reply.json" >/dev/null
test "$metadata_expired" = "$(metadata_state)"
echo 'Metadata receipt failure rolls back edit/audit; durable replay preserves later edits and expired keys stay reserved.'

# Same-account departure replay must remain safe after membership is restored.
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$retry_org','$other','MEMBER','ACTIVE');" >/dev/null
# An actual assigned Card makes cross-store rollback observable.
test "$(request POST /boards "$(jq -nc --arg org "$retry_org" '{organizationId:$org,name:"Assignment receipt rollback",visibility:"PRIVATE"}')")" = 201
receipt_board="$(jq -r '.id' "$scratch/response.json")"
test "$(request POST "/boards/$receipt_board/lists" '{"name":"Receipt rollback List"}')" = 201
receipt_list="$(jq -r '.id' "$scratch/response.json")"
test "$(request POST "/lists/$receipt_list/cards" '{"title":"Assigned receipt rollback Card"}')" = 201
receipt_card="$(jq -r '.id' "$scratch/response.json")"
for id in "$receipt_board" "$receipt_list" "$receipt_card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES(gen_random_uuid(),'$retry_org','$receipt_board','$other','MEMBER','ACTIVE',clock_timestamp(),clock_timestamp());" >/dev/null
assign_receipt_card() {
  local version
  version="$(admin "SELECT version FROM cards WHERE tenant_id='$retry_org' AND id='$receipt_card';")"
  test "$(request PUT "/cards/$receipt_card/members/$other?version=$version" '{}')" = 200
}
assign_receipt_card
receipt_card_version="$(admin "SELECT version FROM cards WHERE tenant_id='$retry_org' AND id='$receipt_card';")"
departure_key="$(cat /proc/sys/kernel/random/uuid)"
departure_request() {
  local body="${3:-}"
  if test -z "$body"; then body='{}'; fi
  curl --max-time 60 --silent --show-error -b "$scratch/${2:-other}.cookies" -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $departure_key" -X POST -d "$body" \
    -D "$scratch/$1.headers" -o "$scratch/$1.json" -w '%{http_code}' "$BASE_URL/organizations/$retry_org/leave"
}
departure_state() {
  admin "SELECT jsonb_build_object('members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM organization_members m WHERE tenant_id='$retry_org'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$retry_org'),
    'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$retry_org' AND id='$receipt_card'),
    'assignments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY user_id) FROM card_members a WHERE tenant_id='$retry_org' AND card_id='$receipt_card'),
    'events',(SELECT jsonb_agg(jsonb_build_object('id',e.event_id,'sequence',e.sequence,'type',e.event_type,'actor',e.actor_id,'entityType',e.entity_type,'entity',e.entity_id,'version',e.entity_version,'correlation',e.correlation_id,'metadata',e.metadata,'created',e.created_at) ORDER BY e.sequence) FROM work_events e WHERE tenant_id='$retry_org' AND board_id='$receipt_board'),
    'stream',(SELECT to_jsonb(w) FROM work_event_streams w WHERE tenant_id='$retry_org' AND board_id='$receipt_board'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM organization_departure_replays r WHERE tenant_id='$retry_org'))::text;"
}
departure_before="$(departure_state)"
# A cookie account switch cannot turn the reviewed actor's command into a new departure.
departure_actor_body="$(jq -nc --arg actor "$owner" '{expectedActorId:$actor}')"
test "$(departure_request departure-account-switch other "$departure_actor_body")" = 401
jq -e '.code=="session_unavailable"' "$scratch/departure-account-switch.json" >/dev/null
scripts/ci/assert-file-excludes.sh "$retry_org|$owner|$other" "$scratch/departure-account-switch.json"
test "$departure_before" = "$(departure_state)"
admin 'REVOKE INSERT ON organization_departure_replays FROM strataai_api_runtime;' >/dev/null
test "$(departure_request departure-denied)" = 503
jq -e '.code=="organization_storage_unavailable"' "$scratch/departure-denied.json" >/dev/null
test "$departure_before" = "$(departure_state)"
admin 'GRANT INSERT ON organization_departure_replays TO strataai_api_runtime;' >/dev/null
# Observe actual receipt publication while the original cookie session expires.
departure_hash="$(awk '$6=="strataai_session" {print $7}' "$scratch/other.cookies" | tr -d '\n' | sha256sum | cut -d ' ' -f 1)"
[[ "$departure_hash" =~ ^[0-9a-f]{64}$ ]]
departure_session="$(admin "SELECT id FROM sessions WHERE token_hash='$departure_hash' AND user_id='$other' AND revoked_at IS NULL;")"
[[ "$departure_session" =~ ^[0-9a-fA-F-]{36}$ ]]
departure_session_expiry="$(admin "SELECT expires_at FROM sessions WHERE id='$departure_session';")"
test -n "$departure_session_expiry"
admin "CREATE FUNCTION public.ci_organization_departure_wait() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER AS \$\$
BEGIN
  IF NEW.tenant_id='$retry_org'::uuid THEN PERFORM pg_sleep(12); END IF;
  RETURN NEW;
END;
\$\$;
CREATE TRIGGER ci_organization_departure_wait AFTER INSERT ON organization_departure_replays
  FOR EACH ROW EXECUTE FUNCTION public.ci_organization_departure_wait();" >/dev/null
admin "UPDATE sessions SET expires_at=clock_timestamp()+interval '10 seconds' WHERE id='$departure_session';" >/dev/null
departure_publication_before="$(departure_state)"
departure_identity_before="$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$other'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$other'))::text;")"
departure_request departure-expiry > "$scratch/departure-expiry.status" & request_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_departure_replays%';")" = 1; then break; fi
  sleep 0.1
done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_departure_replays%';")" = 1
wait "$request_pid"; request_pid=''
test "$(cat "$scratch/departure-expiry.status")" = 401
jq -e '.code=="session_unavailable"' "$scratch/departure-expiry.json" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/departure-expiry.headers"
scripts/ci/assert-file-excludes.sh "$retry_org|$other" "$scratch/departure-expiry.json"
test "$departure_publication_before" = "$(departure_state)"
test "$departure_identity_before" = "$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$other'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$other'))::text;")"
admin 'DROP TRIGGER ci_organization_departure_wait ON organization_departure_replays; DROP FUNCTION public.ci_organization_departure_wait();' >/dev/null
admin "UPDATE sessions SET expires_at='$departure_session_expiry'::timestamptz WHERE id='$departure_session';" >/dev/null
departure_session=''; departure_session_expiry=''
test "$departure_before" = "$(departure_state)"
hold "SELECT id FROM organizations WHERE id='$retry_org' FOR UPDATE;"
departure_request departure-first > "$scratch/departure-first.status" & departure_first_pid=$!
departure_request departure-second > "$scratch/departure-second.status" & departure_second_pid=$!
blocked '%SELECT id FROM organizations%FOR UPDATE%' 2
release ''
wait "$departure_first_pid"; wait "$departure_second_pid"
test "$(cat "$scratch/departure-first.status")" = 204
test "$(cat "$scratch/departure-second.status")" = 204
test "$(admin "SELECT count(*)=1 FROM organization_departure_replays WHERE tenant_id='$retry_org' AND actor_id='$other';")" = t
test "$(admin "SELECT count(*)=1 FROM audit_events WHERE tenant_id='$retry_org' AND event_type='ORGANIZATION_MEMBER_LEFT' AND actor_id='$other';")" = t
test "$(admin "SELECT status='REMOVED' AND version=2 FROM organization_members WHERE tenant_id='$retry_org' AND user_id='$other';")" = t
test "$(admin "SELECT count(*)=0 FROM card_members WHERE tenant_id='$retry_org' AND card_id='$receipt_card' AND user_id='$other';")" = t
test "$(admin "SELECT version FROM cards WHERE tenant_id='$retry_org' AND id='$receipt_card';")" = "$((receipt_card_version+1))"
admin "UPDATE organization_members SET status='ACTIVE',version=version+1,updated_at=clock_timestamp() WHERE tenant_id='$retry_org' AND user_id='$other';" >/dev/null
assign_receipt_card
departure_rejoined="$(departure_state)"
test "$(departure_request departure-replay)" = 204
test "$departure_rejoined" = "$(departure_state)"
test "$(departure_request departure-other-actor owner)" = 409
jq -e '.code=="sole_owner"' "$scratch/departure-other-actor.json" >/dev/null
test "$departure_rejoined" = "$(departure_state)"
test "$(admin "BEGIN; SET LOCAL ROLE strataai_api_runtime; SELECT set_config('app.tenant_id','$organization',true) IS NOT NULL; SELECT count(*) FROM organization_departure_replays WHERE tenant_id='$retry_org'; ROLLBACK;" | tail -n1)" = 0
test "$(admin "SELECT has_table_privilege('strataai_worker_runtime','organization_departure_replays','SELECT') OR has_table_privilege('strataai_api_runtime','organization_departure_replays','UPDATE') OR has_table_privilege('strataai_api_runtime','organization_departure_replays','DELETE');")" = f
admin "UPDATE organization_departure_replays SET expires_at=clock_timestamp()-interval '1 second',created_at=clock_timestamp()-interval '2 seconds' WHERE tenant_id='$retry_org' AND key_id='$departure_key';" >/dev/null
departure_expired="$(departure_state)"
test "$(departure_request departure-expired)" = 409
jq -e '.code=="idempotency_expired"' "$scratch/departure-expired.json" >/dev/null
test "$departure_expired" = "$(departure_state)"
echo 'Departure receipts roll back atomically, serialize identical retries, preserve rejoined membership and keep expired keys reserved.'

# Member removal receipts bind actor, target and reviewed version.
removal_key="$(cat /proc/sys/kernel/random/uuid)"
removal_card_version="$(admin "SELECT version FROM cards WHERE tenant_id='$retry_org' AND id='$receipt_card';")"
removal_version="$(admin "SELECT version FROM organization_members WHERE tenant_id='$retry_org' AND user_id='$other';")"
removal_request() {
  curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' \
    -H "Idempotency-Key: $removal_key" -X DELETE -D "$scratch/$1.headers" -o "$scratch/$1.json" -w '%{http_code}' \
    "$BASE_URL/organizations/$retry_org/members/${3:-$other}?expectedVersion=${2:-$removal_version}&expectedActorId=${4:-$owner}"
}
removal_state() {
  admin "SELECT jsonb_build_object('members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM organization_members m WHERE tenant_id='$retry_org'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$retry_org'),
    'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$retry_org' AND id='$receipt_card'),
    'assignments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY user_id) FROM card_members a WHERE tenant_id='$retry_org' AND card_id='$receipt_card'),
    'events',(SELECT jsonb_agg(jsonb_build_object('id',e.event_id,'sequence',e.sequence,'type',e.event_type,'actor',e.actor_id,'entityType',e.entity_type,'entity',e.entity_id,'version',e.entity_version,'correlation',e.correlation_id,'metadata',e.metadata,'created',e.created_at) ORDER BY e.sequence) FROM work_events e WHERE tenant_id='$retry_org' AND board_id='$receipt_board'),
    'stream',(SELECT to_jsonb(w) FROM work_event_streams w WHERE tenant_id='$retry_org' AND board_id='$receipt_board'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM organization_removal_replays r WHERE tenant_id='$retry_org'))::text;"
}
removal_before="$(removal_state)"
test "$(removal_request removal-account-switch "$removal_version" "$other" "$other")" = 401
jq -e '.code=="session_unavailable"' "$scratch/removal-account-switch.json" >/dev/null
test "$removal_before" = "$(removal_state)"
admin 'REVOKE INSERT ON organization_removal_replays FROM strataai_api_runtime;' >/dev/null
test "$(removal_request removal-denied)" = 503
jq -e '.code=="organization_storage_unavailable"' "$scratch/removal-denied.json" >/dev/null
test "$removal_before" = "$(removal_state)"
admin 'GRANT INSERT ON organization_removal_replays TO strataai_api_runtime;' >/dev/null
# Observe actual receipt publication while the original cookie session expires.
removal_hash="$(owner_hash)"
[[ "$removal_hash" =~ ^[0-9a-f]{64}$ ]]
removal_session="$(admin "SELECT id FROM sessions WHERE token_hash='$removal_hash' AND user_id='$owner' AND revoked_at IS NULL;")"
[[ "$removal_session" =~ ^[0-9a-fA-F-]{36}$ ]]
removal_session_expiry="$(admin "SELECT expires_at FROM sessions WHERE id='$removal_session';")"
test -n "$removal_session_expiry"
admin "CREATE FUNCTION public.ci_organization_removal_wait() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER AS \$\$
BEGIN
  IF NEW.tenant_id='$retry_org'::uuid THEN PERFORM pg_sleep(12); END IF;
  RETURN NEW;
END;
\$\$;
CREATE TRIGGER ci_organization_removal_wait AFTER INSERT ON organization_removal_replays
  FOR EACH ROW EXECUTE FUNCTION public.ci_organization_removal_wait();" >/dev/null
admin "UPDATE sessions SET expires_at=clock_timestamp()+interval '10 seconds' WHERE id='$removal_session';" >/dev/null
removal_publication_before="$(removal_state)"
removal_identity_before="$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$owner'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$owner'))::text;")"
removal_request removal-expiry > "$scratch/removal-expiry.status" & request_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_removal_replays%';")" = 1; then break; fi
  sleep 0.1
done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_removal_replays%';")" = 1
wait "$request_pid"; request_pid=''
test "$(cat "$scratch/removal-expiry.status")" = 401
jq -e '.code=="session_unavailable"' "$scratch/removal-expiry.json" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/removal-expiry.headers"
scripts/ci/assert-file-excludes.sh "$retry_org|$owner|$other|Assigned receipt rollback Card" "$scratch/removal-expiry.json"
test "$removal_publication_before" = "$(removal_state)"
test "$removal_identity_before" = "$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$owner'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$owner'))::text;")"
admin 'DROP TRIGGER ci_organization_removal_wait ON organization_removal_replays; DROP FUNCTION public.ci_organization_removal_wait();' >/dev/null
admin "UPDATE sessions SET expires_at='$removal_session_expiry'::timestamptz WHERE id='$removal_session';" >/dev/null
removal_session=''; removal_session_expiry=''
test "$removal_before" = "$(removal_state)"
hold "SELECT id FROM organizations WHERE id='$retry_org' FOR UPDATE;"
removal_request removal-first > "$scratch/removal-first.status" & removal_first_pid=$!
removal_request removal-second > "$scratch/removal-second.status" & removal_second_pid=$!
blocked '%SELECT id FROM organizations%FOR UPDATE%' 2
release ''
wait "$removal_first_pid"; wait "$removal_second_pid"
test "$(cat "$scratch/removal-first.status")" = 204
test "$(cat "$scratch/removal-second.status")" = 204
test "$(admin "SELECT count(*)=1 FROM organization_removal_replays WHERE tenant_id='$retry_org' AND actor_id='$owner';")" = t
test "$(admin "SELECT count(*)=1 FROM audit_events WHERE tenant_id='$retry_org' AND event_type='ORGANIZATION_MEMBER_REMOVED' AND actor_id='$owner';")" = t
test "$(admin "SELECT count(*)=0 FROM card_members WHERE tenant_id='$retry_org' AND card_id='$receipt_card' AND user_id='$other';")" = t
test "$(admin "SELECT version FROM cards WHERE tenant_id='$retry_org' AND id='$receipt_card';")" = "$((removal_card_version+1))"
admin "UPDATE organization_members SET status='ACTIVE',version=version+1,updated_at=clock_timestamp() WHERE tenant_id='$retry_org' AND user_id='$other';" >/dev/null
assign_receipt_card
removal_rejoined="$(removal_state)"
test "$(removal_request removal-replay)" = 204
test "$removal_rejoined" = "$(removal_state)"
test "$(removal_request removal-conflict "$((removal_version+1))")" = 409
jq -e '.code=="idempotency_conflict"' "$scratch/removal-conflict.json" >/dev/null
test "$(removal_request removal-target-conflict 1 "$owner")" = 409
jq -e '.code=="idempotency_conflict"' "$scratch/removal-target-conflict.json" >/dev/null
test "$removal_rejoined" = "$(removal_state)"
test "$(admin "BEGIN; SET LOCAL ROLE strataai_api_runtime; SELECT set_config('app.tenant_id','$organization',true) IS NOT NULL; SELECT count(*) FROM organization_removal_replays WHERE tenant_id='$retry_org'; ROLLBACK;" | tail -n1)" = 0
test "$(admin "SELECT has_table_privilege('strataai_worker_runtime','organization_removal_replays','SELECT') OR has_table_privilege('strataai_api_runtime','organization_removal_replays','UPDATE') OR has_table_privilege('strataai_api_runtime','organization_removal_replays','DELETE');")" = f
admin "UPDATE organization_removal_replays SET expires_at=clock_timestamp()-interval '1 second',created_at=clock_timestamp()-interval '2 seconds' WHERE tenant_id='$retry_org' AND key_id='$removal_key';" >/dev/null
removal_expired="$(removal_state)"
test "$(removal_request removal-expired)" = 409
jq -e '.code=="idempotency_expired"' "$scratch/removal-expired.json" >/dev/null
test "$removal_expired" = "$(removal_state)"
echo 'Removal receipt failure rolls back membership/audit; identical retries serialize, preserve rejoined membership and reserve expired keys.'

# PRD-03-TC-06/07/08: absent-parent retries share one owning creation scope.
creation_key="$(cat /proc/sys/kernel/random/uuid)"
creation_org="$(python3 - "$owner" "$creation_key" <<'PY'
import hashlib,sys,uuid
print(uuid.UUID(bytes_le=hashlib.sha256(f"strataai:organization:create:v1:{sys.argv[1]}:{sys.argv[2]}".encode()).digest()[:16]))
PY
)"
[[ "$creation_org" =~ ^[0-9a-fA-F-]{36}$ ]]
creation_body='{"name":"Original receipt creation","description":"Reviewed creation"}'
creation_request() {
  curl --max-time 60 --silent --show-error -b "$scratch/${3:-owner}.cookies" -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $creation_key" -X POST \
    -D "$scratch/$1.headers" -o "$scratch/$1.json" -w '%{http_code}' -d "${2:-$creation_body}" \
    "$BASE_URL/organizations?expectedActorId=$owner"
}
creation_state() {
  admin "SELECT jsonb_build_object('organization',(SELECT to_jsonb(o) FROM organizations o WHERE id='$creation_org'),
    'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM organization_members m WHERE tenant_id='$creation_org'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$creation_org'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM organization_creation_replays r WHERE tenant_id='$creation_org'))::text;"
}
creation_before="$(creation_state)"
test "$(creation_request creation-account "$creation_body" other)" = 401
test "$creation_before" = "$(creation_state)"
admin 'REVOKE INSERT ON organization_creation_replays FROM strataai_api_runtime;' >/dev/null
test "$(creation_request creation-denied)" = 503
jq -e '.code=="organization_storage_unavailable"' "$scratch/creation-denied.json" >/dev/null
test "$creation_before" = "$(creation_state)"
admin 'GRANT INSERT ON organization_creation_replays TO strataai_api_runtime;' >/dev/null
# Observe actual receipt publication while the original cookie session expires.
creation_hash="$(owner_hash)"
[[ "$creation_hash" =~ ^[0-9a-f]{64}$ ]]
creation_session="$(admin "SELECT id FROM sessions WHERE token_hash='$creation_hash' AND user_id='$owner' AND revoked_at IS NULL;")"
[[ "$creation_session" =~ ^[0-9a-fA-F-]{36}$ ]]
creation_session_expiry="$(admin "SELECT expires_at FROM sessions WHERE id='$creation_session';")"
test -n "$creation_session_expiry"
admin "CREATE FUNCTION public.ci_organization_creation_wait() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER AS \$\$
BEGIN
  IF NEW.tenant_id='$creation_org'::uuid THEN PERFORM pg_sleep(12); END IF;
  RETURN NEW;
END;
\$\$;
CREATE TRIGGER ci_organization_creation_wait AFTER INSERT ON organization_creation_replays
  FOR EACH ROW EXECUTE FUNCTION public.ci_organization_creation_wait();" >/dev/null
admin "UPDATE sessions SET expires_at=clock_timestamp()+interval '10 seconds' WHERE id='$creation_session';" >/dev/null
creation_publication_before="$(creation_state)"
creation_identity_before="$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$owner'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$owner'))::text;")"
creation_request creation-expiry > "$scratch/creation-expiry.status" & request_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_creation_replays%';")" = 1; then break; fi
  sleep 0.1
done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_creation_replays%';")" = 1
wait "$request_pid"; request_pid=''
test "$(cat "$scratch/creation-expiry.status")" = 401
jq -e '.code=="session_unavailable"' "$scratch/creation-expiry.json" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/creation-expiry.headers"
scripts/ci/assert-file-excludes.sh "$creation_org|$owner|Original receipt creation" "$scratch/creation-expiry.json"
test "$creation_publication_before" = "$(creation_state)"
test "$creation_identity_before" = "$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$owner'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$owner'))::text;")"
admin 'DROP TRIGGER ci_organization_creation_wait ON organization_creation_replays; DROP FUNCTION public.ci_organization_creation_wait();' >/dev/null
admin "UPDATE sessions SET expires_at='$creation_session_expiry'::timestamptz WHERE id='$creation_session';" >/dev/null
creation_session=''; creation_session_expiry=''
test "$creation_before" = "$(creation_state)"
hold "SELECT pg_advisory_xact_lock(hashtextextended('strataai:organization:create:$creation_org',0));"
creation_request creation-first > "$scratch/creation-first.status" & creation_first_pid=$!
creation_request creation-second > "$scratch/creation-second.status" & creation_second_pid=$!
blocked '%strataai:organization:create:%' 2
release ''
wait "$creation_first_pid"; wait "$creation_second_pid"
test "$(cat "$scratch/creation-first.status")" = 201
test "$(cat "$scratch/creation-second.status")" = 201
cmp "$scratch/creation-first.json" "$scratch/creation-second.json"
jq -e --arg org "$creation_org" --arg actor "$owner" '.organization.id==$org and .organization.ownerUserId==$actor and .organization.version==1 and .role==0' "$scratch/creation-first.json" >/dev/null
test "$(admin "SELECT count(*)=1 FROM organization_creation_replays WHERE tenant_id='$creation_org' AND actor_id='$owner';")" = t
test "$(admin "SELECT count(*)=1 FROM organization_members WHERE tenant_id='$creation_org' AND user_id='$owner' AND role='OWNER' AND status='ACTIVE';")" = t
test "$(admin "SELECT count(*)=1 FROM audit_events WHERE tenant_id='$creation_org' AND event_type='ORGANIZATION_CREATED' AND actor_id='$owner';")" = t
test "$(request PATCH "/organizations/$creation_org" '{"name":"Later creation metadata","version":1}')" = 200
creation_later="$(creation_state)"
test "$(creation_request creation-replay)" = 201
cmp "$scratch/creation-first.json" "$scratch/creation-replay.json"
test "$creation_later" = "$(creation_state)"
test "$(creation_request creation-conflict '{"name":"Different creation","description":"Reviewed creation"}')" = 409
jq -e '.code=="idempotency_conflict"' "$scratch/creation-conflict.json" >/dev/null
test "$creation_later" = "$(creation_state)"
admin "UPDATE organization_members SET status='REMOVED',version=version+1 WHERE tenant_id='$creation_org' AND user_id='$owner';" >/dev/null
creation_withdrawn="$(creation_state)"
test "$(creation_request creation-withdrawn)" = 404
scripts/ci/assert-file-excludes.sh "$creation_org|$owner|Original receipt creation|Later creation metadata" "$scratch/creation-withdrawn.json"
test "$creation_withdrawn" = "$(creation_state)"
admin "UPDATE organization_members SET status='ACTIVE',version=version+1 WHERE tenant_id='$creation_org' AND user_id='$owner';" >/dev/null
test "$(admin "BEGIN; SET LOCAL ROLE strataai_api_runtime; SELECT set_config('app.tenant_id','$organization',true) IS NOT NULL; SELECT count(*) FROM organization_creation_replays WHERE tenant_id='$creation_org'; ROLLBACK;" | tail -n1)" = 0
test "$(admin "SELECT has_table_privilege('strataai_worker_runtime','organization_creation_replays','SELECT') OR has_table_privilege('strataai_api_runtime','organization_creation_replays','UPDATE') OR has_table_privilege('strataai_api_runtime','organization_creation_replays','DELETE');")" = f
admin "UPDATE organization_creation_replays SET expires_at=clock_timestamp()-interval '1 second',created_at=clock_timestamp()-interval '2 seconds' WHERE tenant_id='$creation_org' AND key_id='$creation_key';" >/dev/null
creation_expired="$(creation_state)"
test "$(creation_request creation-expired)" = 409
jq -e '.code=="idempotency_expired"' "$scratch/creation-expired.json" >/dev/null
test "$creation_expired" = "$(creation_state)"
echo 'Creation receipts roll back the new parent/owner/audit, serialize absent-parent retries, preserve later edits and reserve expired keys.'

# PRD-03-TC-05/06/07/08: original deletion request acknowledgment survives DELETING.
delete_card_version="$(admin "SELECT version FROM cards WHERE tenant_id='$retry_org' AND id='$receipt_card';")"
test "$(request PATCH "/cards/$receipt_card/dates" "$(jq -nc --argjson version "$delete_card_version" '{dueAt:"2099-01-01T12:00:00Z",dueTimezone:"UTC",dueHasTime:true,dueComplete:false,version:$version}')")" = 200
delete_card_version="$(admin "SELECT version FROM cards WHERE tenant_id='$retry_org' AND id='$receipt_card';")"
test "$(request POST "/cards/$receipt_card/reminders" "$(jq -nc --argjson version "$delete_card_version" '{intervalCode:"AT_DUE",enabled:true,cardVersion:$version,version:0}')")" = 200
delete_reminder_version="$(admin "SELECT version FROM card_reminders WHERE tenant_id='$retry_org' AND card_id='$receipt_card' AND user_id='$owner';")"
delete_reminder_generation="$(admin "SELECT generation FROM card_reminders WHERE tenant_id='$retry_org' AND card_id='$receipt_card' AND user_id='$owner';")"
delete_cancel_events="$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$retry_org' AND board_id='$receipt_board' AND event_type='REMINDER_CANCELLED';")"
deletion_key="$(cat /proc/sys/kernel/random/uuid)"
deletion_version="$(admin "SELECT version FROM organizations WHERE id='$retry_org';")"
deletion_request() {
  curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' \
    -H "Idempotency-Key: $deletion_key" -X DELETE -D "$scratch/$1.headers" -o "$scratch/$1.json" -w '%{http_code}' \
    "$BASE_URL/organizations/$retry_org?version=${2:-$deletion_version}&expectedActorId=${3:-$owner}"
}
deletion_state() {
  admin "SELECT jsonb_build_object('organization',(SELECT to_jsonb(o) FROM organizations o WHERE id='$retry_org'),
    'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM organization_members m WHERE tenant_id='$retry_org'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$retry_org'),
    'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$retry_org' AND id='$receipt_card'),
    'assignments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY user_id) FROM card_members a WHERE tenant_id='$retry_org' AND card_id='$receipt_card'),
    'reminders',(SELECT jsonb_agg(to_jsonb(r) ORDER BY id) FROM card_reminders r WHERE tenant_id='$retry_org'),
    'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id='$retry_org' AND job_type='CARD_REMINDER'),
    'events',(SELECT jsonb_agg(jsonb_build_object('id',e.event_id,'sequence',e.sequence,'type',e.event_type,'actor',e.actor_id,'entityType',e.entity_type,'entity',e.entity_id,'version',e.entity_version,'correlation',e.correlation_id,'metadata',e.metadata,'created',e.created_at) ORDER BY e.sequence) FROM work_events e WHERE tenant_id='$retry_org' AND board_id='$receipt_board'),
    'stream',(SELECT to_jsonb(w) FROM work_event_streams w WHERE tenant_id='$retry_org' AND board_id='$receipt_board'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM organization_deletion_replays r WHERE tenant_id='$retry_org'))::text;"
}
deletion_before="$(deletion_state)"
test "$(deletion_request deletion-account "$deletion_version" "$other")" = 401
test "$deletion_before" = "$(deletion_state)"
admin 'REVOKE INSERT ON organization_deletion_replays FROM strataai_api_runtime;' >/dev/null
test "$(deletion_request deletion-denied)" = 503
jq -e '.code=="organization_storage_unavailable"' "$scratch/deletion-denied.json" >/dev/null
test "$deletion_before" = "$(deletion_state)"
admin 'GRANT INSERT ON organization_deletion_replays TO strataai_api_runtime;' >/dev/null
# Observe actual receipt publication while the original cookie session expires.
deletion_hash="$(owner_hash)"
[[ "$deletion_hash" =~ ^[0-9a-f]{64}$ ]]
deletion_session="$(admin "SELECT id FROM sessions WHERE token_hash='$deletion_hash' AND user_id='$owner' AND revoked_at IS NULL;")"
[[ "$deletion_session" =~ ^[0-9a-fA-F-]{36}$ ]]
deletion_session_expiry="$(admin "SELECT expires_at FROM sessions WHERE id='$deletion_session';")"
test -n "$deletion_session_expiry"
admin "CREATE FUNCTION public.ci_organization_deletion_wait() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER AS \$\$
BEGIN
  IF NEW.tenant_id='$retry_org'::uuid THEN PERFORM pg_sleep(12); END IF;
  RETURN NEW;
END;
\$\$;
CREATE TRIGGER ci_organization_deletion_wait AFTER INSERT ON organization_deletion_replays
  FOR EACH ROW EXECUTE FUNCTION public.ci_organization_deletion_wait();" >/dev/null
admin "UPDATE sessions SET expires_at=clock_timestamp()+interval '10 seconds' WHERE id='$deletion_session';" >/dev/null
deletion_publication_before="$(deletion_state)"
deletion_identity_before="$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$owner'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$owner'))::text;")"
deletion_request deletion-expiry > "$scratch/deletion-expiry.status" & request_pid=$!
for ((attempt=0; attempt<100; attempt++)); do
  if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_deletion_replays%';")" = 1; then break; fi
  sleep 0.1
done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_deletion_replays%';")" = 1
wait "$request_pid"; request_pid=''
test "$(cat "$scratch/deletion-expiry.status")" = 401
jq -e '.code=="session_unavailable"' "$scratch/deletion-expiry.json" >/dev/null
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/deletion-expiry.headers"
scripts/ci/assert-file-excludes.sh "$retry_org|$owner|$other|Assigned receipt rollback Card" "$scratch/deletion-expiry.json"
test "$deletion_publication_before" = "$(deletion_state)"
test "$deletion_identity_before" = "$(admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$owner'),
  'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$owner'))::text;")"
admin 'DROP TRIGGER ci_organization_deletion_wait ON organization_deletion_replays; DROP FUNCTION public.ci_organization_deletion_wait();' >/dev/null
admin "UPDATE sessions SET expires_at='$deletion_session_expiry'::timestamptz WHERE id='$deletion_session';" >/dev/null
deletion_session=''; deletion_session_expiry=''
test "$deletion_before" = "$(deletion_state)"
hold "SELECT id FROM organizations WHERE id='$retry_org' FOR UPDATE;"
deletion_request deletion-first > "$scratch/deletion-first.status" & deletion_first_pid=$!
deletion_request deletion-second > "$scratch/deletion-second.status" & deletion_second_pid=$!
blocked '%SELECT id FROM organizations%FOR UPDATE%' 2
release ''
wait "$deletion_first_pid"; wait "$deletion_second_pid"
test "$(cat "$scratch/deletion-first.status")" = 202
test "$(cat "$scratch/deletion-second.status")" = 202
test "$(admin "SELECT status='DELETING' AND version=$((deletion_version+1)) FROM organizations WHERE id='$retry_org';")" = t
test "$(admin "SELECT count(*)=1 FROM organization_deletion_replays WHERE tenant_id='$retry_org' AND actor_id='$owner';")" = t
test "$(admin "SELECT count(*)=1 FROM audit_events WHERE tenant_id='$retry_org' AND event_type='ORGANIZATION_DELETION_REQUESTED' AND actor_id='$owner';")" = t
test "$(admin "SELECT status='SUSPENDED' AND trigger_at IS NULL AND version=$((delete_reminder_version+1)) AND generation=$((delete_reminder_generation+1)) FROM card_reminders WHERE tenant_id='$retry_org' AND card_id='$receipt_card' AND user_id='$owner';")" = t
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$retry_org' AND board_id='$receipt_board' AND event_type='REMINDER_CANCELLED';")" = "$((delete_cancel_events+1))"
test "$(admin "SELECT version FROM cards WHERE tenant_id='$retry_org' AND id='$receipt_card';")" = "$delete_card_version"
test "$(request GET "/organizations/$retry_org" '')" = 404
deletion_committed="$(deletion_state)"
test "$(deletion_request deletion-replay)" = 202
test "$deletion_committed" = "$(deletion_state)"
deletion_original_key="$deletion_key"; deletion_key="$(cat /proc/sys/kernel/random/uuid)"
test "$(deletion_request deletion-fresh)" = 404
test "$deletion_committed" = "$(deletion_state)"
deletion_key="$deletion_original_key"
test "$(deletion_request deletion-conflict "$((deletion_version+1))")" = 409
jq -e '.code=="idempotency_conflict"' "$scratch/deletion-conflict.json" >/dev/null
test "$deletion_committed" = "$(deletion_state)"
admin "UPDATE organization_members SET role='ADMIN',version=version+1 WHERE tenant_id='$retry_org' AND user_id='$owner';" >/dev/null
deletion_demoted="$(deletion_state)"
test "$(deletion_request deletion-demoted)" = 404
test "$deletion_demoted" = "$(deletion_state)"
admin "UPDATE organization_members SET role='OWNER',version=version+1 WHERE tenant_id='$retry_org' AND user_id='$owner';" >/dev/null
test "$(admin "BEGIN; SET LOCAL ROLE strataai_api_runtime; SELECT set_config('app.tenant_id','$organization',true) IS NOT NULL; SELECT count(*) FROM organization_deletion_replays WHERE tenant_id='$retry_org'; ROLLBACK;" | tail -n1)" = 0
test "$(admin "SELECT has_table_privilege('strataai_worker_runtime','organization_deletion_replays','SELECT') OR has_table_privilege('strataai_api_runtime','organization_deletion_replays','UPDATE') OR has_table_privilege('strataai_api_runtime','organization_deletion_replays','DELETE');")" = f
admin "UPDATE organization_deletion_replays SET expires_at=clock_timestamp()-interval '1 second',created_at=clock_timestamp()-interval '2 seconds' WHERE tenant_id='$retry_org' AND key_id='$deletion_key';" >/dev/null
deletion_expired="$(deletion_state)"
test "$(deletion_request deletion-expired)" = 409
jq -e '.code=="idempotency_expired"' "$scratch/deletion-expired.json" >/dev/null
test "$deletion_expired" = "$(deletion_state)"
echo 'Deletion request receipts roll back parent/reminders/events, serialize retries, acknowledge after access withdrawal and retain current Owner/session checks.'
