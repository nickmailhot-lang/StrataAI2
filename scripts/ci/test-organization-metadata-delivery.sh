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
curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -d "$(jq -nc --arg id "$organization" '{organizationId:$id,name:"Typed routing isolation Board"}')" "$BASE_URL/boards" >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X PATCH \
 -d '{"name":"Updated automatic metadata Organization","version":1}' "$BASE_URL/organizations/$organization" | jq -e '.version==2' >/dev/null
test "$(admin "SELECT count(*)=2 AND bool_and(ready_at IS NULL) FROM organization_metadata_events WHERE tenant_id='$organization';")" = t
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
echo 'Automatic metadata delivery: ordinary creation/edit, exact release Worker, no explicit scopes, ready source events/acknowledged jobs and unchanged unrelated Work queue passed.'
