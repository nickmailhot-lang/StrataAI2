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
worker_changed=true
STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=true STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED=false STRATAAI_WORKER_ORGANIZATION_IDS='' \
 docker compose -f compose.release.yml up -d --no-deps --force-recreate --wait --wait-timeout 180 worker >/dev/null
finished() {
  admin "SELECT (SELECT count(*)=2 AND bool_and(ready_at IS NOT NULL) FROM organization_metadata_events WHERE tenant_id='$organization')
   AND (SELECT count(*)=2 AND bool_and(j.state='SUCCEEDED') FROM background_jobs j JOIN organization_metadata_events e
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
jq -e --arg tenant "$organization" --arg actor "$actor" '.resetRequired==false and .pending==false and (.events|length)==1
 and .events[0].eventType=="ORGANIZATION_UPDATED" and .events[0].version==2 and .events[0].actorId==$actor
 and .events[0].organizationId==$tenant and .events[0].entityId==$tenant and .events[0].entityType=="Organization"
 and .events[0].boardId==null and .events[0].metadata=={}' "$scratch/replay.json" >/dev/null
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
