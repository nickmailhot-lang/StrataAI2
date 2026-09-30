#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable event fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
restore() {
  admin 'GRANT INSERT ON work_events,background_jobs TO strataai_api_runtime; DROP TRIGGER IF EXISTS ci_event_expire_claim ON work_events; DROP FUNCTION IF EXISTS public.ci_event_expire_claim();' >/dev/null
  docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
  rm -rf "$scratch"
}
trap restore EXIT
trap 'echo "Work event check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
email="events-${RANDOM}-${RANDOM}@example.test"
body="$(jq -nc --arg email "$email" '{email:$email,password:"events-correct-horse-battery",displayName:"Event fixture"}')"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/register" > "$scratch/account.json"
curl --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$body" "$BASE_URL/auth/login" >/dev/null
request() { curl --max-time 30 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -X "$1" -d "$3" "$BASE_URL$2"; }
organization="$(request POST /organizations '{"name":"Event Organization"}' "$(uuid)" | jq -r '.organization.id')"
other_org="$(request POST /organizations '{"name":"Other Event Organization"}' "$(uuid)" | jq -r '.organization.id')"
actor="$(jq -r '.user.id' "$scratch/account.json")"
board="$(request POST /boards "$(jq -nc --arg org "$organization" '{organizationId:$org,name:"Private event board"}')" "$(uuid)" | jq -r '.id')"
list="$(request POST "/boards/$board/lists" '{"name":"Event list"}' "$(uuid)" | jq -r '.id')"
key="$(uuid)"
card="$(request POST "/lists/$list/cards" '{"title":"Private event content"}' "$key" | jq -r '.id')"
test "$(request POST "/lists/$list/cards" '{"title":"Private event content"}' "$key" | jq -r '.id')" = "$card"
for id in "$organization" "$other_org" "$actor" "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$organization';")" = 3
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$organization' AND job_type='WORK_EVENT_READY';")" = 3
test "$(admin "SELECT bool_and(metadata='{}'::jsonb) FROM work_events WHERE tenant_id='$organization';")" = t
test "$(admin "SELECT count(*) FROM background_jobs j JOIN work_events e ON e.tenant_id=j.tenant_id AND e.event_id=(j.safe_metadata->>'eventId')::uuid WHERE j.tenant_id='$organization' AND j.actor_id=e.actor_id AND j.safe_metadata->>'boardId'=e.board_id::text AND j.service_identity='work-event-delivery' AND j.idempotency_key='work-event/'||replace(e.event_id::text,'-','');")" = 3
# Both event-row and queue-insert failures must undo the domain, audit, sequence and replay claim.
edit_key="$(uuid)"
for denied in work_events background_jobs; do
  admin "REVOKE INSERT ON $denied FROM strataai_api_runtime;" >/dev/null
  status="$(curl --max-time 30 --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $edit_key" -X PATCH -d '{"title":"Atomic event edit","version":1}' -o "$scratch/failure.json" -w '%{http_code}' "$BASE_URL/cards/$card")"
  test "$status" = 503
  jq -e '.code=="work_storage_unavailable"' "$scratch/failure.json" >/dev/null
  test "$(admin "SELECT version FROM cards WHERE tenant_id='$organization' AND id='$card';")" = 1
  test "$(admin "SELECT last_sequence FROM work_event_streams WHERE tenant_id='$organization' AND board_id='$board';")" = 3
  test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$organization';")" = 3
  test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$organization' AND event_type='CARD_UPDATED';")" = 0
  test "$(admin "SELECT count(*) FROM work_command_replays WHERE tenant_id='$organization' AND key_id='$edit_key';")" = 0
  admin "GRANT INSERT ON $denied TO strataai_api_runtime;" >/dev/null
done
request PATCH "/cards/$card" '{"title":"Atomic event edit","version":1}' "$edit_key" >/dev/null
request PATCH "/cards/$card" '{"title":"Atomic event edit","version":1}' "$edit_key" >/dev/null
# Concurrent distinct commands allocate a contiguous per-board stream.
request POST "/lists/$list/cards" '{"title":"Concurrent first"}' "$(uuid)" > "$scratch/first.json" & first=$!
request POST "/lists/$list/cards" '{"title":"Concurrent second"}' "$(uuid)" > "$scratch/second.json" & second=$!
wait "$first"; wait "$second"
test "$(admin "SELECT count(*)=6 AND min(sequence)=1 AND max(sequence)=6 FROM work_events WHERE tenant_id='$organization' AND board_id='$board';")" = t
test "$(admin "SET ROLE strataai_api_runtime; SET app.tenant_id='$other_org'; SELECT count(*) FROM work_events WHERE tenant_id='$organization';" | tail -1)" = 0
if admin "SET ROLE strataai_api_runtime; SET app.tenant_id='$other_org'; INSERT INTO work_event_streams(tenant_id,board_id) VALUES ('$organization','$board');" >/dev/null 2>&1; then echo 'Cross-tenant stream write escaped RLS'; exit 1; fi
test "$(admin "SELECT has_column_privilege('strataai_worker_runtime','work_events','event_type','SELECT');")" = f
test "$(admin "SELECT has_table_privilege('strataai_api_runtime','work_events','UPDATE');")" = f
test "$(admin "SELECT has_table_privilege('strataai_worker_runtime','work_events','DELETE');")" = f
export STRATAAI_TEST_EVENT_ORGANIZATION_ID="$organization"
docker compose -f compose.release.yml -f scripts/ci/compose.work-event-test.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
for attempt in $(seq 1 60); do
  completed="$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$organization' AND job_type='WORK_EVENT_READY' AND state='SUCCEEDED';")"
  if test "$completed" = 6; then break; fi
  sleep 1
done
test "$completed" = 6
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$organization' AND ready_at IS NOT NULL;")" = 6
before="$(admin "SELECT string_agg(ready_at::text,',' ORDER BY sequence) FROM work_events WHERE tenant_id='$organization';")"
# Simulate a crash after the ready effect but before acknowledgement. Reclaim
# must preserve the existing effect rather than changing the readiness timestamp.
admin "UPDATE background_jobs SET state='PENDING',attempt_count=0,lease_id=NULL,worker_id=NULL,lease_expires_at=NULL,available_at=clock_timestamp() WHERE tenant_id='$organization';" >/dev/null
for attempt in $(seq 1 60); do
  completed="$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$organization' AND state='SUCCEEDED';")"
  if test "$completed" = 6; then break; fi
  sleep 1
done
test "$completed" = 6
test "$(admin "SELECT string_agg(ready_at::text,',' ORDER BY sequence) FROM work_events WHERE tenant_id='$organization';")" = "$before"
# References alone cannot redirect readiness to a different board or actor.
docker compose -f compose.release.yml stop worker >/dev/null
request POST "/lists/$list/cards" '{"title":"Held event"}' "$(uuid)" >/dev/null
held_event="$(admin "SELECT event_id FROM work_events WHERE tenant_id='$organization' AND sequence=7;")"
admin "UPDATE background_jobs SET available_at=clock_timestamp()+interval '1 day' WHERE tenant_id='$organization' AND safe_metadata->>'eventId'='$held_event';" >/dev/null
for mismatch in actor board tenant; do
  bad_actor="$actor"; bad_board="$board"; bad_event="$held_event"
  case "$mismatch" in
    actor) bad_actor="$(uuid)" ;;
    board) bad_board="$(uuid)" ;;
    tenant)
      other_board="$(request POST /boards "$(jq -nc --arg org "$other_org" '{organizationId:$org,name:"Other private board"}')" "$(uuid)" | jq -r '.id')"
      bad_board="$other_board"
      bad_event="$(admin "SELECT event_id FROM work_events WHERE tenant_id='$other_org' AND board_id='$other_board';")"
      ;;
  esac
  admin "INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata) VALUES (gen_random_uuid(),'$organization','WORK_EVENT_READY','ci-negative-$mismatch','$bad_actor','work-event-delivery','event-negative',jsonb_build_object('eventId','$bad_event','boardId','$bad_board'));" >/dev/null
done
docker compose -f compose.release.yml -f scripts/ci/compose.work-event-test.yml up -d --wait --wait-timeout 180 worker >/dev/null
for attempt in $(seq 1 30); do
  denied="$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$organization' AND idempotency_key LIKE 'ci-negative-%' AND last_error_code='job_handler_failed';")"
  if test "$denied" = 3; then break; fi
  sleep 1
done
test "$denied" = 3
test "$(admin "SELECT ready_at IS NULL FROM work_events WHERE tenant_id='$organization' AND event_id='$held_event';")" = t
test "$(admin "SELECT ready_at IS NULL FROM work_events WHERE tenant_id='$other_org' AND event_id='$bad_event';")" = t
# Force lease expiry after the readiness write, before its final transaction fence.
# The event must remain pending even though the UPDATE itself succeeded.
docker compose -f compose.release.yml stop worker >/dev/null
admin "UPDATE background_jobs SET available_at=clock_timestamp()+interval '1 day' WHERE tenant_id='$organization' AND idempotency_key LIKE 'ci-negative-%'; CREATE FUNCTION public.ci_event_expire_claim() RETURNS trigger LANGUAGE plpgsql AS \$\$ BEGIN IF NEW.event_id='$held_event'::uuid THEN UPDATE background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second' WHERE tenant_id=NEW.tenant_id AND safe_metadata->>'eventId'=NEW.event_id::text AND state='RUNNING'; END IF; RETURN NEW; END; \$\$; CREATE TRIGGER ci_event_expire_claim AFTER UPDATE OF ready_at ON work_events FOR EACH ROW EXECUTE FUNCTION public.ci_event_expire_claim(); UPDATE background_jobs SET available_at=clock_timestamp() WHERE tenant_id='$organization' AND safe_metadata->>'eventId'='$held_event' AND idempotency_key NOT LIKE 'ci-negative-%';" >/dev/null
docker compose -f compose.release.yml -f scripts/ci/compose.work-event-test.yml up -d --wait --wait-timeout 180 worker >/dev/null
for attempt in $(seq 1 30); do
  fenced="$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$organization' AND safe_metadata->>'eventId'='$held_event' AND idempotency_key NOT LIKE 'ci-negative-%' AND last_error_code='job_handler_failed';")"
  if test "$fenced" = 1; then break; fi
  sleep 1
done
test "$fenced" = 1
test "$(admin "SELECT ready_at IS NULL FROM work_events WHERE tenant_id='$organization' AND event_id='$held_event';")" = t
admin "DROP TRIGGER ci_event_expire_claim ON work_events; DROP FUNCTION public.ci_event_expire_claim(); UPDATE background_jobs SET available_at=clock_timestamp() WHERE tenant_id='$organization' AND safe_metadata->>'eventId'='$held_event' AND idempotency_key NOT LIKE 'ci-negative-%';" >/dev/null
for attempt in $(seq 1 30); do
  recovered="$(admin "SELECT ready_at IS NOT NULL FROM work_events WHERE tenant_id='$organization' AND event_id='$held_event';")"
  if test "$recovered" = t; then break; fi
  sleep 1
done
test "$recovered" = t
echo 'Exact API/Worker images prove atomic content-free events, contiguous concurrent sequences, duplicate retry, tenant isolation and idempotent delivery readiness.'
