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
owner="$(jq -r '.user.id' "$scratch/owner.json")"
other="$(jq -r '.user.id' "$scratch/other.json")"
test "$(request POST /organizations '{"name":"Atomic organization"}')" = 201
organization="$(jq -r '.organization.id' "$scratch/response.json")"
for id in "$owner" "$other" "$organization"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$organization','$other','OWNER','ACTIVE');" >/dev/null
state() {
  admin "SELECT jsonb_build_object('organization',(SELECT to_jsonb(o) FROM organizations o WHERE id='$organization'),
    'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM organization_members m WHERE tenant_id='$organization'),
    'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$organization'),
    'owned',(SELECT count(*) FROM organizations WHERE owner_user_id='$owner'))::text;"
}
before="$(state)"
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
rejected() {
  test "$(request "$1" "$2" "$3")" = 503
  jq -e '.code=="organization_storage_unavailable" and .status==503' "$scratch/response.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'audit_events|Npgsql|permission denied|INSERT INTO|Atomic organization' "$scratch/response.json"
  test "$before" = "$(state)"
}
# Creation must roll back both organization and owner membership when audit insert fails.
rejected POST /organizations '{"name":"Must roll back"}'
rejected PATCH "/organizations/$organization" '{"name":"Must roll back","version":1}'
rejected DELETE "/organizations/$organization/members/$other" '{}'
rejected POST "/organizations/$organization/leave" '{}'
rejected DELETE "/organizations/$organization?version=1" '{}'
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
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
admin "UPDATE organization_members SET status='ACTIVE' WHERE tenant_id='$organization' AND user_id='$owner';" >/dev/null
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
