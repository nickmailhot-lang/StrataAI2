#!/usr/bin/env bash
set -euo pipefail
# PRD-03 / ARCH-07: ordinary commands, exact release Worker, automatic routing
# with no explicit scopes. SQL only observes; it never manufactures readiness.
BASE_URL="${1:-http://127.0.0.1:8080}"
scratch="$(mktemp -d)"
worker_changed=false
cleanup() {
  local status=$?
  if test "$worker_changed" = true; then
    docker compose -f compose.release.yml up -d --no-deps --force-recreate --wait --wait-timeout 180 worker >/dev/null || status=1
  fi
  rm -rf "$scratch"
  exit "$status"
}
trap cleanup EXIT
admin() { docker compose -f compose.release.yml exec -T postgres psql -X -U strataai -d strataai -At -v ON_ERROR_STOP=1 -c "$1"; }
credentials="$(jq -nc --arg email "metadata-auto-$(date +%s%N)@example.test" '{email:$email,password:"metadata-auto-correct-horse",displayName:"Automatic metadata Owner"}')"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$BASE_URL/auth/register" >/dev/null
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -c "$scratch/cookies" -d "$credentials" "$BASE_URL/auth/login" >/dev/null
organization="$(curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -d '{"name":"Automatic metadata Organization"}' "$BASE_URL/organizations" | jq -r '.organization.id')"
[[ "$organization" =~ ^[0-9a-f-]{36}$ ]]
actor="$(curl --fail --silent --show-error -b "$scratch/cookies" "$BASE_URL/me" | jq -r '.id')"
[[ "$actor" =~ ^[0-9a-f-]{36}$ ]]
curl --fail --silent --show-error -b "$scratch/cookies" -D "$scratch/bootstrap.headers" \
 "$BASE_URL/organizations/$organization/metadata-events?expectedActorId=$actor" > "$scratch/bootstrap.json"
jq -e '.resetRequired==true and .events==[] and (.cursor|type)=="string"' "$scratch/bootstrap.json" >/dev/null
grep -Eiq '^Cache-Control:.*no-store' "$scratch/bootstrap.headers"
cursor="$(jq -r '.cursor' "$scratch/bootstrap.json")"
# Reset is a snapshot instruction. Read current metadata after receiving it.
curl --fail --silent --show-error -b "$scratch/cookies" "$BASE_URL/organizations/$organization" | jq -e '.organization.version==1' >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -d "$(jq -nc --arg id "$organization" '{organizationId:$id,name:"Typed routing isolation Board"}')" "$BASE_URL/boards" >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X PATCH \
 -d '{"name":"Updated automatic metadata Organization","version":1}' "$BASE_URL/organizations/$organization" | jq -e '.version==2' >/dev/null
test "$(admin "SELECT count(*)=2 AND bool_and(ready_at IS NULL) FROM organization_metadata_events WHERE tenant_id='$organization';")" = t
curl --fail --silent --show-error -b "$scratch/cookies" --get --data-urlencode "cursor=$cursor" --data-urlencode "expectedActorId=$actor" \
 "$BASE_URL/organizations/$organization/metadata-events" | jq -e '.pending==true and .resetRequired==false and .events==[]' >/dev/null
work_snapshot() {
  admin "SELECT jsonb_build_object('events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id='$organization'),
   'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id='$organization' AND job_type='WORK_EVENT_READY'))::text;"
}
work_before="$(work_snapshot)"
test "$(admin "SELECT count(*)=1 FROM background_jobs WHERE tenant_id='$organization' AND job_type='WORK_EVENT_READY' AND state='PENDING';")" = t
recipient_credentials="$(jq -nc --arg email "metadata-member-$(date +%s%N)@example.test" '{email:$email,password:"metadata-member-correct-horse",displayName:"Metadata recipient"}')"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$recipient_credentials" "$BASE_URL/auth/register" > "$scratch/recipient.json"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -c "$scratch/recipient.cookies" -d "$recipient_credentials" "$BASE_URL/auth/login" >/dev/null
recipient="$(jq -r '.user.id' "$scratch/recipient.json")"
invitation="$(curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -d "$(jq -nc --arg email "$(jq -r '.user.email' "$scratch/recipient.json")" '{email:$email,surface:"INTERNAL",targetRole:"MEMBER"}')" "$BASE_URL/organizations/$organization/invitations" | jq -r '.id')"
curl --fail --silent --show-error -b "$scratch/recipient.cookies" -H 'X-StrataAI-Request: 1' -X POST "$BASE_URL/me/invitations/$invitation/accept" >/dev/null
subject="$(admin "SELECT id FROM organization_members WHERE tenant_id='$organization' AND user_id='$recipient';")"
test "$(admin "SELECT count(*)=1 FROM organization_metadata_events e JOIN organization_members m ON m.tenant_id=e.tenant_id AND m.id=e.entity_id
 WHERE e.tenant_id='$organization' AND e.event_type='ORGANIZATION_MEMBER_ADDED' AND e.entity_type='OrganizationMembership'
 AND e.actor_id='$recipient' AND e.entity_version=m.version AND e.created_at=m.updated_at AND e.ready_at IS NULL;")" = t
# Historical acceptance must still deliver after removal. Membership version is
# independent of the parent version, and current read authorization is separate.
test "$(curl --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -X DELETE -o "$scratch/removal" -w '%{http_code}' \
 "$BASE_URL/organizations/$organization/members/$recipient?expectedVersion=1")" = 204
test "$(curl --silent --show-error -b "$scratch/recipient.cookies" -o "$scratch/removed-replay" -w '%{http_code}' \
 "$BASE_URL/organizations/$organization/metadata-events")" = 404
restoration="$(curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -d "$(jq -nc --arg email "$(jq -r '.user.email' "$scratch/recipient.json")" '{email:$email,surface:"INTERNAL",targetRole:"MEMBER"}')" "$BASE_URL/organizations/$organization/invitations" | jq -r '.id')"
curl --fail --silent --show-error -b "$scratch/recipient.cookies" -H 'X-StrataAI-Request: 1' -X POST "$BASE_URL/me/invitations/$restoration/accept" >/dev/null
test "$(admin "SELECT count(*)=1 FROM organization_metadata_events e JOIN organization_members m ON m.tenant_id=e.tenant_id AND m.id=e.entity_id
 WHERE e.tenant_id='$organization' AND e.event_type='ORGANIZATION_MEMBER_ADDED' AND e.entity_id='$subject'
 AND e.entity_version=3 AND m.version=3 AND e.created_at=m.updated_at AND e.ready_at IS NULL;")" = t
test "$(curl --silent --show-error -b "$scratch/recipient.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -X POST -d '{}' -o "$scratch/departure" -w '%{http_code}' "$BASE_URL/organizations/$organization/leave")" = 204
test "$(admin "SELECT count(*)=2 AND bool_and(e.entity_id='$subject' AND e.created_at=r.removed_at AND e.metadata='{}'::jsonb
 AND ((e.entity_version=2 AND e.actor_id='$actor' AND a.event_type='ORGANIZATION_MEMBER_REMOVED')
 OR (e.entity_version=4 AND e.actor_id='$recipient' AND a.event_type='ORGANIZATION_MEMBER_LEFT')))
 FROM organization_metadata_events e JOIN audit_events a ON a.id=e.event_id
 JOIN organization_membership_removals r ON r.tenant_id=e.tenant_id AND r.membership_id=e.entity_id AND r.entity_version=e.entity_version
 WHERE e.tenant_id='$organization' AND e.event_type='ORGANIZATION_MEMBER_REMOVED';")" = t
worker_changed=true
STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=true STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED=false STRATAAI_WORKER_ORGANIZATION_IDS='' \
 docker compose -f compose.release.yml up -d --no-deps --force-recreate --wait --wait-timeout 180 worker >/dev/null
finished() {
  admin "SELECT (SELECT count(*)=10 AND bool_and(ready_at IS NOT NULL) FROM organization_metadata_events WHERE tenant_id='$organization')
   AND (SELECT count(*)=10 AND bool_and(j.state='SUCCEEDED') FROM background_jobs j JOIN organization_metadata_events e
    ON e.tenant_id=j.tenant_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id)
    WHERE j.tenant_id='$organization' AND j.job_type='ORGANIZATION_METADATA_EVENT_READY'
    AND j.actor_id=e.actor_id AND j.correlation_id=e.correlation_id AND j.service_identity='organization-metadata-delivery');"
}
for ((attempt=0; attempt<60; attempt++)); do
  if test "$(finished)" = t; then break; fi
  sleep 1
done
test "$(finished)" = t
test "$work_before" = "$(work_snapshot)"
curl --fail --silent --show-error -b "$scratch/cookies" --get --data-urlencode "cursor=$cursor" --data-urlencode "expectedActorId=$actor" \
 "$BASE_URL/organizations/$organization/metadata-events" > "$scratch/replay.json"
jq -e --arg tenant "$organization" --arg actor "$actor" '.resetRequired==false and .pending==false and (.events|length)==9
 and .events[0].eventType=="ORGANIZATION_UPDATED" and .events[0].version==2 and .events[0].actorId==$actor
 and .events[0].organizationId==$tenant and .events[0].entityId==$tenant and .events[0].entityType=="Organization"
 and .events[0].boardId==null and .events[0].metadata=={}' "$scratch/replay.json" >/dev/null
jq -e --arg tenant "$organization" --arg actor "$recipient" --arg subject "$subject" \
 '.events[3].eventType=="ORGANIZATION_MEMBER_ADDED" and .events[3].actorId==$actor and .events[3].organizationId==$tenant
 and .events[3].entityType=="OrganizationMembership" and .events[3].entityId==$subject and .events[3].version==1
 and .events[3].boardId==null and .events[3].metadata=={}' "$scratch/replay.json" >/dev/null
test "$(jq -r '.events[3].eventId' "$scratch/replay.json")" = "$(admin "SELECT event_id FROM organization_metadata_events WHERE tenant_id='$organization' AND event_type='ORGANIZATION_MEMBER_ADDED' AND entity_version=1;")"
jq -e --arg actor "$recipient" --arg subject "$subject" '.events[7].eventType=="ORGANIZATION_MEMBER_ADDED"
 and .events[7].entityType=="OrganizationMembership" and .events[7].entityId==$subject and .events[7].actorId==$actor
 and .events[7].version==3 and .events[7].metadata=={} and .events[7].boardId==null' "$scratch/replay.json" >/dev/null
test "$(jq -r '.events[7].eventId' "$scratch/replay.json")" = "$(admin "SELECT event_id FROM organization_metadata_events WHERE tenant_id='$organization' AND event_type='ORGANIZATION_MEMBER_ADDED' AND entity_version=3;")"
for pair in 4:2 8:4; do
  index="${pair%:*}"; revision="${pair#*:}"
  removal_actor="$actor"; if test "$revision" = 4; then removal_actor="$recipient"; fi
  jq -e --arg actor "$removal_actor" --arg subject "$subject" --argjson index "$index" --argjson revision "$revision" \
   '.events[$index].eventType=="ORGANIZATION_MEMBER_REMOVED" and .events[$index].entityType=="OrganizationMembership"
    and .events[$index].entityId==$subject and .events[$index].actorId==$actor and .events[$index].version==$revision
    and .events[$index].metadata=={} and .events[$index].boardId==null' "$scratch/replay.json" >/dev/null
  test "$(jq -r --argjson index "$index" '.events[$index].eventId' "$scratch/replay.json")" = "$(admin "SELECT event_id FROM organization_metadata_events WHERE tenant_id='$organization' AND event_type='ORGANIZATION_MEMBER_REMOVED' AND entity_version=$revision;")"
done
for pair in "1:$invitation" "5:$restoration"; do
  index="${pair%%:*}"; invited="${pair#*:}"
  jq -e --argjson index "$index" --arg invited "$invited" --arg actor "$actor" \
   '.events[$index].eventType=="ORGANIZATION_MEMBER_INVITED" and .events[$index].entityType=="Invitation"
    and .events[$index].entityId==$invited and .events[$index].actorId==$actor and .events[$index].version==1
    and .events[$index].boardId==null and .events[$index].metadata=={}' "$scratch/replay.json" >/dev/null
  test "$(jq -r --argjson index "$index" '.events[$index].eventId' "$scratch/replay.json")" = "$(admin "SELECT event_id FROM organization_metadata_events WHERE tenant_id='$organization' AND entity_type='Invitation' AND entity_id='$invited' AND event_type='ORGANIZATION_MEMBER_INVITED';")"
done
for pair in "2:$invitation" "6:$restoration"; do
  index="${pair%%:*}"; accepted="${pair#*:}"
  jq -e --argjson index "$index" --arg invitation "$accepted" --arg actor "$recipient" \
   '.events[$index].eventType=="INVITATION_ACCEPTED" and .events[$index].entityType=="Invitation"
    and .events[$index].entityId==$invitation and .events[$index].actorId==$actor and .events[$index].version==2
    and .events[$index].boardId==null and .events[$index].metadata=={}' "$scratch/replay.json" >/dev/null
  test "$(jq -r --argjson index "$index" '.events[$index].eventId' "$scratch/replay.json")" = "$(admin "SELECT e.event_id FROM organization_metadata_events e JOIN invitations i ON i.tenant_id=e.tenant_id AND i.id=e.entity_id
    JOIN audit_events a ON a.id=e.event_id WHERE e.tenant_id='$organization' AND e.entity_id='$accepted'
    AND e.event_type='INVITATION_ACCEPTED' AND e.entity_version=i.version AND e.created_at=i.updated_at AND e.actor_id=a.actor_id;")"
done

test "$(curl --silent --show-error -b "$scratch/recipient.cookies" -o "$scratch/removed-replay" -w '%{http_code}' \
 "$BASE_URL/organizations/$organization/metadata-events")" = 404
scripts/ci/assert-file-excludes.sh 'Automatic metadata Organization|Updated automatic metadata Organization|Typed routing isolation Board' "$scratch/replay.json"
test "$(curl --silent --show-error -b "$scratch/cookies" -o "$scratch/actor-refusal.json" -w '%{http_code}' \
 "$BASE_URL/organizations/$organization/metadata-events?expectedActorId=00000000-0000-4000-8000-000000000001")" = 401
jq -e '.code=="session_unavailable"' "$scratch/actor-refusal.json" >/dev/null
test "$(curl --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -X DELETE \
 -o "$scratch/deletion.json" -w '%{http_code}' "$BASE_URL/organizations/$organization?version=2")" = 202
test "$(curl --silent --show-error -b "$scratch/cookies" --get --data-urlencode "cursor=$cursor" \
 -o "$scratch/deleting-replay.json" -w '%{http_code}' "$BASE_URL/organizations/$organization/metadata-events")" = 404
scripts/ci/assert-file-excludes.sh "$organization|$actor|ORGANIZATION_UPDATED|Automatic metadata" "$scratch/deleting-replay.json"
echo 'Automatic metadata delivery: ordinary creation/edit, exact release Worker, no explicit scopes, ready source events/acknowledged jobs and unchanged unrelated Work queue passed.'
