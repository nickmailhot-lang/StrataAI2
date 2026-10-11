#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable lifecycle fixtures require CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
worker_changed=false
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  local result=$?
  if test "$worker_changed" = true; then
    docker compose -f compose.release.yml up -d --no-deps --force-recreate --wait --wait-timeout 180 worker > "$scratch/restore-private.log" 2>&1 || result=1
  fi
  rm -rf "$scratch"
  exit "$result"
}
trap cleanup EXIT
trap 'echo "Operational lifecycle delivery failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
worker_changed=true
docker compose -f compose.release.yml stop worker >/dev/null
credentials="$(jq -nc --arg email "operational-delivery-$(uuid)@example.test" '{email:$email,password:"operational-delivery-correct-horse",displayName:"Lifecycle delivery owner"}')"
curl --max-time 30 --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$BASE_URL/auth/register" > "$scratch/account.json"
actor="$(jq -r '.user.id' "$scratch/account.json")"
[[ "$actor" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
# The disposable release account follows the established notification fixture.
# Provider verification is tested independently; only this fresh account changes.
admin "UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE id='$actor';" > /dev/null
curl --max-time 30 --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$BASE_URL/auth/login" > /dev/null
request() { curl --max-time 30 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -X "$1" -d "$3" "$BASE_URL$2"; }
organizations=()
for state in SUSPENDED DEACTIVATED; do
  organization="$(request POST /organizations "$(jq -nc --arg name "Lifecycle delivery $state" '{name:$name}')" | jq -r '.organization.id')"
  [[ "$organization" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
  organizations+=("$organization")
  request PATCH "/organizations/$organization/operational-lifecycle" "$(jq -nc --arg state "$state" '{version:1,status:$state}')" > "$scratch/ack.json"
  jq -e --arg state "$state" '.version==2 and .status==$state' "$scratch/ack.json" > /dev/null
  curl --max-time 30 --fail --silent --show-error -b "$scratch/cookies" "$BASE_URL/organizations/$organization/operational-lifecycle/events?afterVersion=0" > "$scratch/page.json"
  jq -e '.pending==true and .events==[] and .version==2 and .afterVersion==0' "$scratch/page.json" > /dev/null
  status="$(curl --max-time 30 --silent --show-error -b "$scratch/cookies" -o "$scratch/denial.json" -w '%{http_code}' "$BASE_URL/organizations/$organization/surface-access?surface=INTERNAL")"
  test "$status" = 404
done
scope="tenant_id IN ('${organizations[0]}','${organizations[1]}')"
typed="job_type='ORGANIZATION_OPERATIONAL_LIFECYCLE_EVENT_READY'"
test "$(admin "SELECT count(*) FROM organization_operational_lifecycle_events WHERE $scope AND ready_at IS NULL;")" = 2
other_snapshot() { admin "SELECT md5(COALESCE(jsonb_agg(to_jsonb(j) ORDER BY id)::text,'')) FROM background_jobs j WHERE $scope AND NOT ($typed);"; }
source_snapshot() { admin "SELECT md5(jsonb_agg(to_jsonb(e)-'ready_at'-'updated_at' ORDER BY tenant_id,version)::text) FROM organization_operational_lifecycle_events e WHERE $scope;"; }
ready_snapshot() { admin "SELECT jsonb_agg(jsonb_build_array(tenant_id,version,ready_at) ORDER BY tenant_id,version)::text FROM organization_operational_lifecycle_events WHERE $scope;"; }
other_before="$(other_snapshot)"
source_before="$(source_snapshot)"
admin "UPDATE users SET status='DEACTIVATED',version=version+1,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE id='$actor';" >/dev/null
start_worker() { docker compose -f compose.release.yml -f scripts/ci/compose.operational-lifecycle-test.yml up -d --no-deps --force-recreate --wait --wait-timeout 180 worker > "$scratch/start-private.log" 2>&1; }
wait_completed() {
  local completed=0
  for attempt in $(seq 1 60); do
    completed="$(admin "SELECT count(*) FROM background_jobs WHERE $scope AND $typed AND state='SUCCEEDED';")"
    if test "$completed" = 2; then break; fi
    sleep 1
  done
  test "$completed" = 2
}
start_worker
wait_completed
test "$(admin "SELECT count(*) FROM organization_operational_lifecycle_events WHERE $scope AND ready_at IS NOT NULL;")" = 2
ready_before="$(ready_snapshot)"
test "$(other_snapshot)" = "$other_before"
docker compose -f compose.release.yml stop worker > /dev/null
# Simulate a lost acknowledgment with a real expired lease. The second host
# reclaims through its restricted typed queue and preserves the existing effect.
admin "UPDATE background_jobs SET state='RUNNING',attempt_count=1,worker_id=gen_random_uuid(),lease_id=gen_random_uuid(),lease_expires_at=clock_timestamp()-interval '1 second',available_at=clock_timestamp(),updated_at=clock_timestamp(),version=version+1 WHERE $scope AND $typed;" > /dev/null
start_worker
wait_completed
test "$(ready_snapshot)" = "$ready_before"
test "$(source_snapshot)" = "$source_before"
test "$(other_snapshot)" = "$other_before"
test "$(admin "SELECT count(*) FROM background_jobs WHERE $scope AND $typed AND state='SUCCEEDED' AND attempt_count=2;")" = 2
echo 'Hosted operational lifecycle discovery and expired-lease recovery passed; inactive parents/source actor, original clocks and unrelated jobs preserved.'
