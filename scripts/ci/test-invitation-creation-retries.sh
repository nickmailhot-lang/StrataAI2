#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable invitation retry fixtures may run only in CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"; scratch="$(mktemp -d)"; gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'GRANT INSERT ON audit_events,invitation_creation_replays TO strataai_api_runtime;' >/dev/null || true
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Invitation creation retry check failed at line $LINENO" >&2' ERR
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
for actor in owner other; do
  jq -nc --arg email "invite-retry-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"invitation-retry-correct-horse",displayName:"Invitation retry fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner="$(jq -r '.user.id' "$scratch/owner.user")"
org="$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Retry-safe invitation creation"}' "$base/organizations" | jq -r '.organization.id')"
foreign="$(curl --fail --silent --show-error -b "$scratch/other.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Other invitation scope"}' "$base/organizations" | jq -r '.organization.id')"
[[ "$org" =~ ^[0-9a-f-]{36}$ ]]; [[ "$owner" =~ ^[0-9a-f-]{36}$ ]]
[[ "$foreign" =~ ^[0-9a-f-]{36}$ ]]
key="$(cat /proc/sys/kernel/random/uuid)"
printf '%s' '{"email":"retry-invited@example.test","surface":"INTERNAL","targetRole":"ADMIN"}' > "$scratch/input.json"
create() { curl --max-time 60 --silent --show-error -b "$scratch/${4:-owner}.cookies" -H 'X-StrataAI-Request: 1' -H "Idempotency-Key: $1" -H 'Content-Type: application/json' -d "$(cat "$scratch/${3:-input}.json")" -o "$scratch/$2.json" -w '%{http_code}' "$base/organizations/$org/invitations"; }
state() { admin "SELECT json_build_array((SELECT count(*) FROM invitations WHERE tenant_id='$org'),(SELECT count(*) FROM invitation_routes WHERE tenant_id='$org'),(SELECT count(*) FROM invitation_creation_replays WHERE tenant_id='$org'),(SELECT count(*) FROM audit_events WHERE tenant_id='$org'));"; }
publication_state() {
  admin "SELECT jsonb_build_object(
   'proofs',(SELECT jsonb_agg(to_jsonb(p) ORDER BY invitation_id) FROM organization_invitation_creations p WHERE tenant_id='$org'),
   'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY sequence) FROM organization_metadata_events e WHERE tenant_id='$org'),
   'stream',(SELECT to_jsonb(s) FROM organization_metadata_event_streams s WHERE tenant_id='$org'),
   'recipientProofs',(SELECT jsonb_agg(to_jsonb(p) ORDER BY invitation_id,entity_version) FROM invitation_recipient_proofs p WHERE tenant_id='$org'),
   'recipientEvents',(SELECT jsonb_agg(to_jsonb(e) ORDER BY email_normalized,sequence) FROM invitation_recipient_events e WHERE tenant_id='$org'),
   'recipientStreams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY email_normalized) FROM invitation_recipient_streams s
     WHERE email_normalized IN (SELECT email_normalized FROM invitation_recipient_proofs WHERE tenant_id='$org')),
   'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id='$org'))::text;"
}
# Any receipt or audit failure rolls back the invitation and routing projection.
reviewed_state="$(state)"; reviewed_publication="$(publication_state)"
for reviewed in 00000000-0000-4000-8000-000000000001 00000000-0000-0000-0000-000000000000; do
  test "$(curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' \
    -H "Idempotency-Key: $key" -H 'Content-Type: application/json' -d "$(cat "$scratch/input.json")" \
    -o "$scratch/reviewed-create.json" -w '%{http_code}' "$base/organizations/$org/invitations?expectedActorId=$reviewed")" = 401
  jq -e '.code=="session_unavailable"' "$scratch/reviewed-create.json" >/dev/null
  scripts/ci/assert-file-excludes.sh "$org|retry-invited@example.test" "$scratch/reviewed-create.json"
  test "$(state)" = "$reviewed_state"
  test "$(publication_state)" = "$reviewed_publication"
done
# The same unconsumed key is used by the positive concurrent/retry case below.
for table in audit_events invitation_creation_replays; do
  before="$(state)"; failed_key="$(cat /proc/sys/kernel/random/uuid)"
  publication_before="$(publication_state)"
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(create "$failed_key" denied)" = 503
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
  test "$(state)" = "$before"
  test "$(publication_state)" = "$publication_before"
  test "$(create "$failed_key" repaired)" = 201
done
before="$(state)"
create "$key" first > "$scratch/first.status" & one=$!
create "$key" second > "$scratch/second.status" & two=$!
wait "$one"; wait "$two"
test "$(cat "$scratch/first.status")" = 201; test "$(cat "$scratch/second.status")" = 201
if ! cmp "$scratch/first.json" "$scratch/second.json"; then
  # Public fixture IDs and expiry are sufficient to diagnose precision drift;
  # do not emit credentials, recipient addresses or bearer-token material.
  jq '{id,expiresAt}' "$scratch/first.json" "$scratch/second.json" >&2
  exit 1
fi
id="$(jq -r '.id' "$scratch/first.json")"; [[ "$id" =~ ^[0-9a-f-]{36}$ ]]
jq -e --arg org "$org" '.organizationId==$org and .invitationToken==null and .targetRole=="ADMIN"' "$scratch/first.json" >/dev/null
test "$(admin "SELECT count(*) FROM invitation_creation_replays WHERE tenant_id='$org' AND actor_id='$owner' AND key_id='$key' AND invitation_id='$id';")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND entity_id='$id' AND event_type='ORGANIZATION_MEMBER_INVITED';")" = 1
test "$(admin "SELECT count(*)=1 AND bool_and(e.actor_id='$owner' AND e.entity_type='Invitation' AND e.entity_version=i.version
 AND i.version=1 AND i.updated_at=i.created_at AND e.created_at=i.created_at AND e.metadata='{}'::jsonb)
 FROM organization_metadata_events e JOIN invitations i ON i.tenant_id=e.tenant_id AND i.id=e.entity_id
 JOIN audit_events a ON a.id=e.event_id AND a.entity_id=i.id AND a.event_type='ORGANIZATION_MEMBER_INVITED'
 WHERE e.tenant_id='$org' AND e.entity_id='$id';")" = t
publication_after="$(publication_state)"
test "$(admin "SELECT count(*)=1 AND bool_and(e.event_type='INVITATION_CREATED' AND e.source_event_type='ORGANIZATION_MEMBER_INVITED'
 AND e.actor_id='$owner' AND e.entity_version=1 AND e.metadata='{}'::jsonb AND e.created_at=i.created_at AND e.email_normalized=i.email_normalized)
 FROM invitation_recipient_events e JOIN invitations i ON i.id=e.entity_id AND i.tenant_id=e.tenant_id
 JOIN audit_events a ON a.id=e.event_id AND a.entity_id=i.id AND a.event_type=e.source_event_type
 WHERE e.tenant_id='$org' AND e.entity_id='$id';")" = t
test "$(create "$key" replay)" = 201; cmp "$scratch/first.json" "$scratch/replay.json"
test "$(publication_state)" = "$publication_after"
after="$(state)"; test "$(jq -c 'map(.+1)' <<< "$before")" = "$(jq -c '.' <<< "$after")"
printf '%s' '{"email":"other-intent@example.test","surface":"INTERNAL","targetRole":"ADMIN"}' > "$scratch/changed.json"
test "$(create "$key" conflict changed)" = 409; jq -e '.code=="idempotency_key_reused"' "$scratch/conflict.json" >/dev/null
test "$(create "$key" outsider input other)" = 404
test "$(state)" = "$after"
test "$(publication_state)" = "$publication_after"
# Completed acknowledgments do not restore revoked grants.
reviewed_before="$(admin "SELECT jsonb_build_object('invitation',(SELECT to_jsonb(i) FROM invitations i WHERE tenant_id='$org' AND id='$id'),
 'route',(SELECT to_jsonb(r) FROM invitation_routes r WHERE tenant_id='$org' AND invitation_id='$id'))::text;")"
for reviewed in 00000000-0000-4000-8000-000000000001 00000000-0000-0000-0000-000000000000; do
  test "$(curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -X DELETE \
   -o "$scratch/reviewed-actor.json" -w '%{http_code}' "$base/organizations/$org/invitations/$id?expectedActorId=$reviewed")" = 401
  jq -e '.code=="session_unavailable"' "$scratch/reviewed-actor.json" >/dev/null
  scripts/ci/assert-file-excludes.sh "$org|$id|retry-invited@example.test" "$scratch/reviewed-actor.json"
  test "$reviewed_before" = "$(admin "SELECT jsonb_build_object('invitation',(SELECT to_jsonb(i) FROM invitations i WHERE tenant_id='$org' AND id='$id'),
   'route',(SELECT to_jsonb(r) FROM invitation_routes r WHERE tenant_id='$org' AND invitation_id='$id'))::text;")"
  test "$(state)" = "$after"
  test "$(publication_state)" = "$publication_after"
done
admin "UPDATE invitations SET revoked_at=clock_timestamp() WHERE tenant_id='$org' AND id='$id';" >/dev/null
test "$(create "$key" revoked)" = 201; cmp "$scratch/first.json" "$scratch/revoked.json"
test "$(admin "SELECT revoked_at IS NOT NULL FROM invitations WHERE tenant_id='$org' AND id='$id';")" = t
test "$(admin "SELECT version=2 AND updated_at>=created_at FROM invitations WHERE tenant_id='$org' AND id='$id';")" = t
test "$(publication_state)" = "$publication_after"
# Expiry keeps the original key reserved, never creating a replacement.
admin "UPDATE invitation_creation_replays SET created_at=clock_timestamp()-interval '25 hours',expires_at=clock_timestamp()-interval '1 hour' WHERE tenant_id='$org' AND actor_id='$owner' AND key_id='$key';" >/dev/null
test "$(create "$key" expired)" = 409; jq -e '.code=="idempotency_key_expired"' "$scratch/expired.json" >/dev/null
test "$(state)" = "$after"
test "$(publication_state)" = "$publication_after"
hold() {
  rm -f "$scratch/gate.in" "$scratch/gate.log"
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 &
  gate_pid=$!; exec 3> "$scratch/gate.in"; printf 'BEGIN;\n%s\n\\echo invite_locked\n' "$1" >&3
  for ((attempt=0;attempt<100;attempt++)); do grep -q '^invite_locked$' "$scratch/gate.log" && return; sleep 0.05; done; return 1
}
hold "SELECT user_id FROM organization_members WHERE tenant_id='$org' AND user_id='$owner' FOR UPDATE;"
create "$key" waited > "$scratch/waited.status" & request_pid=$!
locked=false
for ((attempt=0;attempt<100;attempt++)); do
  count="$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT user_id FROM organization_members%FOR UPDATE%';")"
  [[ "$count" =~ ^[0-9]+$ ]]; if ((count>0)); then locked=true; break; fi; sleep 0.05
done
test "$locked" = true
printf "UPDATE organization_members SET role='MEMBER',version=version+1 WHERE tenant_id='%s' AND user_id='%s';\nCOMMIT;\n\\q\n" "$org" "$owner" >&3
exec 3>&-; wait "$gate_pid"; gate_pid=''; wait "$request_pid"; request_pid=''
test "$(cat "$scratch/waited.status")" = 404
test "$(state)" = "$after"
scripts/ci/assert-file-excludes.sh 'retry-invited@example.test|token_hash|fingerprint' "$scratch/waited.json"
# Expiry during an audit write wait rolls back the invitation, route and receipt.
admin "UPDATE organization_members SET role='OWNER',version=version+1 WHERE tenant_id='$org' AND user_id='$owner';
  UPDATE sessions SET expires_at=clock_timestamp()+interval '5 seconds' WHERE user_id='$owner' AND revoked_at IS NULL;" >/dev/null
hold 'LOCK TABLE audit_events IN SHARE MODE;'
create "$(cat /proc/sys/kernel/random/uuid)" elapsed > "$scratch/elapsed.status" & request_pid=$!
locked=false
for ((attempt=0;attempt<100;attempt++)); do
  count="$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%INSERT INTO audit_events%';")"
  [[ "$count" =~ ^[0-9]+$ ]]; if ((count>0)); then locked=true; break; fi; sleep 0.05
done
test "$locked" = true
sleep 6
printf 'COMMIT;\n\\q\n' >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; wait "$request_pid"; request_pid=''
test "$(cat "$scratch/elapsed.status")" = 401
test "$(state)" = "$after"
test "$(admin 'BEGIN; SET LOCAL ROLE strataai_api_runtime; SELECT count(*) FROM invitation_creation_replays; ROLLBACK;')" = 0
test "$(admin "BEGIN; SET LOCAL ROLE strataai_api_runtime; SELECT set_config('app.tenant_id','$foreign',true); SELECT count(*) FROM invitation_creation_replays WHERE tenant_id='$org'; ROLLBACK;" | tail -n1)" = 0
if admin "BEGIN; SET LOCAL ROLE strataai_api_runtime; SELECT set_config('app.tenant_id','$foreign',true);
  INSERT INTO invitation_creation_replays(tenant_id,actor_id,key_id,fingerprint,invitation_id)
  VALUES('$foreign','$owner',gen_random_uuid(),repeat('A',64),'$id'); COMMIT;" > "$scratch/cross-tenant.log" 2>&1; then
  echo 'Cross-tenant invitation receipt accepted'; exit 1
fi
grep -q 'invitation_creation_replay_invitation_scope' "$scratch/cross-tenant.log"
test "$(admin "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='invitation_creation_replays'::regclass;")" = t
echo 'Exact-image invitation creation: concurrent token-free acknowledgments, atomic audit/receipt rollback, post-write session expiry rollback, current administrative admission, expired-key reservation and acknowledgment-only revoked invitation replay passed.'
