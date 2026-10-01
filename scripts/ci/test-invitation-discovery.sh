#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable invitation fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"; pids=()
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Invitation discovery check failed at line $LINENO" >&2' ERR
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
for actor in owner recipient wrong; do
  body="$(jq -nc --arg email "invite-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"invite-correct-horse-battery",displayName:"Invitation fixture"}')"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
done
user="$(jq -r '.user.id' "$scratch/recipient.user")"; email="$(jq -r '.user.email' "$scratch/recipient.user")"
post() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X POST -d "$3" -o "$scratch/response" -w '%{http_code}' "$BASE_URL$2"; }
test "$(post owner /organizations '{"name":"Release invitation council"}')" = 201
org="$(jq -r '.organization.id' "$scratch/response")"
state() { admin "SELECT jsonb_build_object('invite',(SELECT to_jsonb(i) FROM invitations i WHERE id='$id'),
  'route',(SELECT to_jsonb(r) FROM invitation_routes r WHERE invitation_id='$id'),
  'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY id) FROM organization_members m WHERE tenant_id='$org' AND user_id='$user'),
  'portal',(SELECT jsonb_agg(to_jsonb(p) ORDER BY id) FROM portal_access p WHERE tenant_id='$org' AND user_id='$user'),
  'audits',(SELECT count(*) FROM audit_events WHERE event_type='INVITATION_ACCEPTED' AND entity_id='$id'))::text;"; }
for surface in INTERNAL PORTAL; do
  role=MEMBER; if test "$surface" = PORTAL; then role=OWNER; fi
  test "$(post owner "/organizations/$org/invitations" "$(jq -nc --arg email "$email" --arg surface "$surface" --arg role "$role" '{email:$email,surface:$surface,targetRole:$role}')")" = 201
  id="$(jq -r '.id' "$scratch/response")"
  curl --fail --silent -b "$scratch/recipient.cookies" "$BASE_URL/me/invitations" > "$scratch/page"
  jq -e --arg id "$id" --arg surface "$surface" '.items|any(.id==$id and .surface==$surface and .organizationName=="Release invitation council")' "$scratch/page" >/dev/null
  scripts/ci/assert-file-excludes.sh 'tokenHash|invitationToken|acceptedByUserId' "$scratch/page"
  test "$(post wrong "/me/invitations/$id/accept" '{}')" = 400
  before="$(state)"
  admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
  test "$(post recipient "/me/invitations/$id/accept" '{}')" = 503
  test "$before" = "$(state)"
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
  for n in 1 2 3; do
    curl --max-time 60 --silent --show-error -b "$scratch/recipient.cookies" -H 'X-StrataAI-Request: 1' -X POST -o "$scratch/ack-$n" -w '%{http_code}' "$BASE_URL/me/invitations/$id/accept" > "$scratch/status-$n" &
    pids+=($!)
  done
  for pid in "${pids[@]}"; do wait "$pid"; done; pids=()
  for n in 1 2 3; do test "$(cat "$scratch/status-$n")" = 200; cmp "$scratch/ack-1" "$scratch/ack-$n"; done
  test "$(admin "SELECT count(*) FROM audit_events WHERE event_type='INVITATION_ACCEPTED' AND entity_id='$id';")" = 1
  test "$(admin "SELECT accepted_by_user_id FROM invitations WHERE id='$id';")" = "$user"
  wrong_user="$(jq -r '.user.id' "$scratch/wrong.user")"
  if admin "UPDATE invitations SET accepted_by_user_id='$wrong_user' WHERE id='$id';"; then echo 'Completed acceptance attribution was rewritten'; exit 1; fi
  if test "$surface" = INTERNAL; then
    test "$(admin "SELECT count(*) FROM organization_members WHERE tenant_id='$org' AND user_id='$user' AND version=1 AND role='MEMBER' AND status='ACTIVE';")" = 1
    test "$(admin "SELECT count(*) FROM portal_access WHERE tenant_id='$org' AND user_id='$user';")" = 0
  else
    test "$(admin "SELECT count(*) FROM portal_access WHERE tenant_id='$org' AND user_id='$user' AND relationship_type='OWNER' AND version=1;")" = 1
    test "$(admin "SELECT version FROM organization_members WHERE tenant_id='$org' AND user_id='$user';")" = 1
  fi
  saved="$(state)"
  docker compose -f compose.release.yml restart api >/dev/null
  for attempt in $(seq 1 90); do if curl --fail --silent "$BASE_URL/readyz" >/dev/null; then break; fi; sleep 1; done
  test "$(post recipient "/me/invitations/$id/accept" '{}')" = 200
  test "$saved" = "$(state)"
done
# Expiry committed during the invitation-row wait denies membership and audit publication.
expiry_id="$(cat /proc/sys/kernel/random/uuid)"
admin "INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 SELECT '$expiry_id',tenant_id,invited_email,email_normalized,encode(sha256('$expiry_id'::bytea),'hex'),
 target_surface,target_role,created_by_user_id,clock_timestamp(),clock_timestamp()+interval '1 hour' FROM invitations WHERE id='$id';" >/dev/null
admin "BEGIN; SELECT id FROM invitations WHERE id='$expiry_id' FOR UPDATE; SELECT pg_sleep(10) /* invitation-expiry-gate */;
 UPDATE invitations SET expires_at=clock_timestamp()-interval '1 second' WHERE id='$expiry_id'; COMMIT;" > "$scratch/expiry-gate" &
gate=$!; pids+=($gate)
for attempt in $(seq 1 100); do if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%invitation-expiry-gate%' AND wait_event='PgSleep';")" = 1; then break; fi; sleep 0.1; done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%invitation-expiry-gate%' AND wait_event='PgSleep';")" = 1
post recipient "/me/invitations/$expiry_id/accept" '{}' > "$scratch/expiry-status" &
pending=$!; pids+=($pending)
for attempt in $(seq 1 100); do if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%FROM invitations%FOR UPDATE%';")" -ge 1; then break; fi; sleep 0.1; done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%FROM invitations%FOR UPDATE%';")" -ge 1
wait "$gate"; wait "$pending"; pids=()
test "$(cat "$scratch/expiry-status")" = 400
test "$(admin "SELECT count(*) FROM invitations WHERE id='$expiry_id' AND accepted_at IS NULL AND accepted_by_user_id IS NULL;")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE event_type='INVITATION_ACCEPTED' AND entity_id='$expiry_id';")" = 0
test "$(admin "SELECT version FROM portal_access WHERE tenant_id='$org' AND user_id='$user' AND relationship_type='OWNER';")" = 1
# Large pending set seeks a bounded UUID cursor, using the one-connection API.
admin "INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
  SELECT gen_random_uuid(),tenant_id,invited_email,email_normalized,encode(sha256(gen_random_uuid()::text::bytea),'hex'),
    target_surface,target_role,created_by_user_id,clock_timestamp(),clock_timestamp()+interval '1 day'
  FROM invitations CROSS JOIN generate_series(1,101) WHERE id='$id';" >/dev/null
cursor=''; : > "$scratch/ids"
for count in 50 50 1; do
  curl --fail --silent -b "$scratch/recipient.cookies" "$BASE_URL/me/invitations${cursor:+?after=$cursor}" > "$scratch/page"
  test "$(jq '.items|length' "$scratch/page")" = "$count"
  jq -r '.items[].id' "$scratch/page" >> "$scratch/ids"
  cursor="$(jq -r '.nextCursor // empty' "$scratch/page")"
done
test -z "$cursor"; test "$(sort -u "$scratch/ids" | wc -l)" = 101
# Observe a real account-lock wait, then revoke the original session before disclosure.
admin "BEGIN; SELECT id FROM users WHERE id='$user' FOR UPDATE; SELECT pg_sleep(10) /* invitation-read-gate */; COMMIT;" > "$scratch/gate" &
gate=$!; pids+=($gate)
for attempt in $(seq 1 100); do if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%invitation-read-gate%' AND wait_event='PgSleep';")" = 1; then break; fi; sleep 0.1; done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE query LIKE '%invitation-read-gate%' AND wait_event='PgSleep';")" = 1
curl --max-time 60 --silent --show-error -b "$scratch/recipient.cookies" -o "$scratch/denied" -w '%{http_code}' "$BASE_URL/me/invitations" > "$scratch/wait-status" &
pending=$!; pids+=($pending)
for attempt in $(seq 1 100); do if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock';")" -ge 1; then break; fi; sleep 0.1; done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock';")" -ge 1
admin "UPDATE sessions SET revoked_at=clock_timestamp() WHERE user_id='$user';" >/dev/null
wait "$gate"; wait "$pending"; pids=()
test "$(cat "$scratch/wait-status")" = 401
scripts/ci/assert-file-excludes.sh 'Release invitation council|targetRole|organizationId' "$scratch/denied"
echo 'Release verified-email invitation discovery: one-connection paging, atomic acceptance, duplicates, immutable attribution, restart, invitation expiry wait and post-wait revocation passed.'
