#!/usr/bin/env bash
set -euo pipefail
# PRD-02/03-TC-05/07/08: active-owner continuity across global account lifecycle.
test "${CI:-}" = true || { echo 'Disposable owner fixtures may run only in CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"
scratch=$(mktemp -d)
gate_pid=''
pids=()
changed_topology=0
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'GRANT INSERT ON audit_events,identity_events,identity_revocation_replays TO strataai_api_runtime;' >/dev/null
  if test "$changed_topology" = 1; then docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null; fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Account owner continuity failed at line $LINENO" >&2' ERR
account() {
  local label="$1" body
  body="$(jq -nc --arg email "owner-continuity-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"continuity-correct-horse-battery",displayName:"Continuity fixture"}')"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$base/auth/register" > "$scratch/$label.user"
  curl --fail --silent --show-error -c "$scratch/$label.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$base/auth/login" >/dev/null
}
request() {
  local label="$1" path="$2" output="$3" key="${4:-}" retry=()
  if test -n "$key"; then retry=(-H "Idempotency-Key: $key"); fi
  curl --max-time 60 --silent --show-error -b "$scratch/$label.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' "${retry[@]}" \
    -X POST -d '{}' -D "$scratch/$output.headers" -o "$scratch/$output.body" -w '%{http_code}' "$base$path"
}
organization() {
  curl --fail --silent --show-error -b "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -d '{"name":"Owner continuity fixture"}' "$base/organizations" | jq -r '.organization.id'
}
state() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$actor'),
    'sessions',(SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM sessions s WHERE user_id='$actor'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id='$actor'),
    'stream',(SELECT to_jsonb(s) FROM identity_event_streams s WHERE user_id='$actor'),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id='$actor'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_revocation_replays r WHERE user_id='$actor'))::text;"
}
unchanged_history() {
  admin "SELECT jsonb_build_object('user',(SELECT to_jsonb(u) FROM users u WHERE id='$actor'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE actor_id='$actor'),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM identity_events e WHERE user_id='$actor'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY key_id) FROM identity_revocation_replays r WHERE user_id='$actor'))::text;"
}
deny_owner() {
  test "$(request owner /me/deactivate denied "${1:-}")" = 409
  jq -e '.code=="organization_owner_required"' "$scratch/denied.body" >/dev/null
  scripts/ci/assert-file-excludes.sh "$org|$actor|Owner continuity fixture|Npgsql|SELECT" "$scratch/denied.body"
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/denied.headers"
}
hold() {
  rm -f "$scratch/gate.in" "$scratch/gate.log"
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 &
  gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\n%s\n\\echo continuity_locked\n' "$1" >&3
  for ((n=0;n<100;n++)); do if grep -q '^continuity_locked$' "$scratch/gate.log"; then return; fi; kill -0 "$gate_pid" || return 1; sleep 0.05; done
  return 1
}
blocked() {
  local pattern="$1" expected="${2:-1}" count
  for ((n=0;n<100;n++)); do
    count="$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '$pattern';")"
    if test "$count" -ge "$expected"; then return; fi
    sleep 0.05
  done
  return 1
}
release() { printf '%s\nCOMMIT;\n\\q\n' "$1" >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; }
account owner; account other
actor="$(jq -r '.user.id' "$scratch/owner.user")"
other="$(jq -r '.user.id' "$scratch/other.user")"
org="$(organization owner)"
key="$(cat /proc/sys/kernel/random/uuid)"
before="$(state)"
deny_owner
deny_owner "$key"
test "$before" = "$(state)"
test "$(curl --silent -b "$scratch/owner.cookies" -o /dev/null -w '%{http_code}' "$base/me")" = 200
# An active Admin is not an alternative Owner. Neither is an inactive historical Owner.
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$other','ADMIN','ACTIVE');" >/dev/null
deny_owner "$key"
admin "UPDATE organization_members SET role='OWNER' WHERE tenant_id='$org' AND user_id='$other';" >/dev/null
for status in SUSPENDED DEACTIVATED; do
  admin "UPDATE users SET status='$status' WHERE id='$other';" >/dev/null
  deny_owner "$key"
  test "$(request owner "/organizations/$org/leave" leave)" = 409
  jq -e '.code=="sole_owner"' "$scratch/leave.body" >/dev/null
done
admin "UPDATE users SET status='ACTIVE' WHERE id='$other';" >/dev/null
# An Owner in every Organization is required, not just in the first route.
second_org="$(organization owner)"
before="$(state)"
deny_owner "$key"
test "$before" = "$(state)"
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$second_org','$other','OWNER','ACTIVE');" >/dev/null
# Ownership checks and account/session/audit/event/receipt mutations share one root.
before="$(state)"
for table in audit_events identity_events identity_revocation_replays; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request owner /me/deactivate rollback "$key")" = 503
  jq -e '.code=="identity_storage_unavailable"' "$scratch/rollback.body" >/dev/null
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/rollback.headers"
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
# Freeze a real parent, then revoke the original session before account admission.
history="$(unchanged_history)"
hold "SELECT id FROM organizations WHERE id='$org' FOR UPDATE;"
request owner /me/deactivate revoked "$key" > "$scratch/revoked.status" & pids+=($!)
blocked '%SELECT id FROM organizations%FOR UPDATE%'
release "UPDATE sessions SET revoked_at=clock_timestamp() WHERE user_id='$actor';"
for pid in "${pids[@]}"; do wait "$pid"; done; pids=()
test "$(cat "$scratch/revoked.status")" = 401
test "$history" = "$(unchanged_history)"
scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/revoked.headers"
# Separate fixture for an additional Owner grant committed during account admission.
account owner; account other
actor="$(jq -r '.user.id' "$scratch/owner.user")"; other="$(jq -r '.user.id' "$scratch/other.user")"
org="$(organization owner)"; new_org="$(organization other)"
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$other','OWNER','ACTIVE');" >/dev/null
before="$(state)"; key="$(cat /proc/sys/kernel/random/uuid)"
hold "SELECT id FROM users WHERE id='$actor' FOR UPDATE;"
request owner /me/deactivate changed "$key" > "$scratch/changed.status" & pids+=($!)
blocked '%SELECT id FROM users%FOR UPDATE%'
release "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$new_org','$actor','OWNER','ACTIVE');"
for pid in "${pids[@]}"; do wait "$pid"; done; pids=()
test "$(cat "$scratch/changed.status")" = 409
jq -e '.code=="ownership_changed"' "$scratch/changed.body" >/dev/null
test "$before" = "$(state)"
# A status change committed during an alternative Owner account lock wait must win.
hold "SELECT id FROM users WHERE id='$other' FOR UPDATE;"
request owner /me/deactivate inactive "$key" > "$scratch/inactive.status" & pids+=($!)
blocked '%SELECT id,status,email_verified FROM users%FOR SHARE%'
release "UPDATE users SET status='DEACTIVATED' WHERE id='$other';"
for pid in "${pids[@]}"; do wait "$pid"; done; pids=()
test "$(cat "$scratch/inactive.status")" = 409
jq -e '.code=="organization_owner_required"' "$scratch/inactive.body" >/dev/null
test "$before" = "$(state)"
admin "UPDATE users SET status='ACTIVE' WHERE id='$other';" >/dev/null
# Complete under a one-connection pool and verified-email policy; no nested scope.
changed_topology=1
admin "UPDATE users SET email_verified=(id='$actor') WHERE id IN ('$actor','$other');" >/dev/null
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml -f scripts/ci/compose.owner-continuity-test.yml up -d --wait --wait-timeout 180 api >/dev/null
before="$(state)"
deny_owner "$key"
test "$before" = "$(state)"
admin "UPDATE users SET email_verified=true WHERE id='$other';" >/dev/null
test "$(request owner /me/deactivate confirmed "$key")" = 204
test "$(admin "SELECT status FROM users WHERE id='$actor';")" = DEACTIVATED
test "$(admin "SELECT count(*) FROM sessions WHERE user_id='$actor' AND revoked_at IS NULL;")" = 0
test "$(admin "SELECT count(*) FROM identity_events WHERE user_id='$actor' AND event_type='USER_DEACTIVATED';")" = 1
test "$(admin "SELECT count(*) FROM organization_members WHERE user_id='$actor' AND role='OWNER' AND status='ACTIVE';")" = 2
saved="$(state)"
# Even if external fixture data loses continuity later, a completed original receipt
# is only an empty acknowledgment, never a repeated lifecycle decision or regrant.
admin "UPDATE users SET status='DEACTIVATED' WHERE id='$other';" >/dev/null
test "$(request owner /me/deactivate replay "$key")" = 204
test ! -s "$scratch/replay.body"
test "$saved" = "$(state)"
test "$(curl --silent -b "$scratch/owner.cookies" -o /dev/null -w '%{http_code}' "$base/me")" = 401
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
changed_topology=0
# Force simultaneous departure/lifecycle decisions to wait at the parent gate.
for action in deactivate leave; do
  account owner; account other
  actor="$(jq -r '.user.id' "$scratch/owner.user")"; other="$(jq -r '.user.id' "$scratch/other.user")"
  org="$(organization owner)"
  admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$other','OWNER','ACTIVE');" >/dev/null
  path=/me/deactivate; if test "$action" = leave; then path="/organizations/$org/leave"; fi
  hold "SELECT id FROM organizations WHERE id='$org' FOR UPDATE;"
  first_key=''; if test "$action" = deactivate; then first_key="$(cat /proc/sys/kernel/random/uuid)"; fi
  request owner "$path" first "$first_key" > "$scratch/first.status" & pids+=($!)
  request other /me/deactivate second "$(cat /proc/sys/kernel/random/uuid)" > "$scratch/second.status" & pids+=($!)
  blocked '%SELECT id FROM organizations%FOR UPDATE%' 2
  release ''
  for pid in "${pids[@]}"; do wait "$pid"; done; pids=()
  first="$(cat "$scratch/first.status")"; second="$(cat "$scratch/second.status")"
  if test "$first" = 204; then test "$second" = 409; else test "$first" = 409; test "$second" = 204; fi
  test "$(admin "SELECT count(*) FROM organization_members m JOIN users u ON u.id=m.user_id WHERE m.tenant_id='$org' AND m.role='OWNER' AND m.status='ACTIVE' AND u.status='ACTIVE';")" = 1
done
echo 'Exact-image account ownership: all-Organization continuity, inactive/administrative denial, atomic rollback, post-wait session/status/plan validation, one-connection verified policy, acknowledgment-only replay and concurrent deactivation/departure passed.'
