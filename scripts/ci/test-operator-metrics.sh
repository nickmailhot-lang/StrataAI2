#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true && test "${GITHUB_ACTIONS:-}" = true || { echo 'Disposable operator metrics fixture requires GitHub Actions.' >&2; exit 1; }
scratch=$(mktemp -d)
worker_changed=false
postgres_stopped=false
backlog_pending=''
backlog_running=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
restore() {
  local failed=0
  if $postgres_stopped; then
    docker compose -f compose.release.yml up -d --wait --wait-timeout 180 postgres >/dev/null || failed=1
  fi
  if $worker_changed; then
    docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null || failed=1
  fi
  if [[ -n "$backlog_pending" && -n "$backlog_running" ]]; then
    admin "DELETE FROM background_jobs WHERE id IN ('$backlog_pending','$backlog_running');" >/dev/null || failed=1
  fi
  rm -rf "$scratch"
  return "$failed"
}
trap restore EXIT
base=http://localhost:8088
uuid() { cat /proc/sys/kernel/random/uuid; }
post() { curl --max-time 30 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$2" "$base$1"; }
get() { curl --max-time 30 --fail --silent --show-error -b "$scratch/cookies" "$base$1"; }
patch() { curl --max-time 30 --fail --silent --show-error -b "$scratch/cookies" -X PATCH -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$2" "$base$1"; }
credentials=$(jq -nc --arg email "metrics-$(uuid)@example.test" '{email:$email,password:"private-metric-fixture-password",displayName:"private-metric-fixture"}')
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/register" >/dev/null
bad_credentials=$(jq -c '.password="private-wrong-password"' <<< "$credentials")
test "$(curl --max-time 30 --silent --show-error -o /dev/null -w '%{http_code}' -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$bad_credentials" "$base/auth/login")" = 401
test "$(curl --max-time 30 --silent --show-error -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login")" = 403
curl --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null
org=$(post /organizations '{"name":"private-metric-fixture"}' | jq -r '.organization.id')
[[ "$org" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
# Two tiny, non-claimable fixture rows exercise nonzero pending/running gauges.
# Neither is runnable during this bounded check; no business command is retried.
backlog_pending=$(uuid)
backlog_running=$(uuid)
admin "INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,state,available_at,lease_id,worker_id,lease_expires_at)
VALUES ('$backlog_pending','$org','BACKLOG_FIXTURE','$backlog_pending',gen_random_uuid(),'fixture','backlog-fixture','PENDING',now()+interval '1 day',NULL,NULL,NULL),
('$backlog_running','$org','BACKLOG_FIXTURE','$backlog_running',gen_random_uuid(),'fixture','backlog-fixture','RUNNING',now(),gen_random_uuid(),gen_random_uuid(),now()+interval '1 day');" >/dev/null
export STRATAAI_TEST_BACKLOG_ORGANIZATION_ID="$org"
worker_changed=true
docker compose -f compose.release.yml -f scripts/ci/compose.backlog-test.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
test "$(curl --max-time 30 --silent --show-error -o /dev/null -w '%{http_code}' "$base/organizations/$org")" = 401
board=$(post /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"private-metric-fixture",visibility:"PRIVATE"}')" | jq -r '.id')
list=$(post "/boards/$board/lists" '{"name":"private-metric-fixture"}' | jq -r '.id')
card=$(post "/lists/$list/cards" '{"title":"private-metric-fixture"}' | jq -r '.id')
card_version=$(post "/cards/$card/checklists" '{"title":"private-metric-fixture","cardVersion":1}' | jq -r '.cardVersion')
[[ "$card_version" =~ ^[1-9][0-9]*$ ]]
curl --fail --silent --show-error -b "$scratch/cookies" "$base/cards/$card/checklists" >/dev/null
post /me/checklist-client-events '{"events":[{"action":"disclosure","kind":"open","count":1},{"action":"create","kind":"use","count":1},{"action":"create","kind":"success","count":1,"durationMs":125}]}' >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" "$base/cards/$card/activity" >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" "$base/boards/$board/activity" >/dev/null
post /me/activity-client-events '{"events":[{"action":"card_disclosure","kind":"open","count":1},{"action":"board_disclosure","kind":"open","count":1},{"action":"card_read","kind":"retry","count":1},{"action":"card_read","kind":"success","count":1,"durationMs":125}]}' >/dev/null
post "/cards/$card/comments" "$(jq -nc --argjson version "$card_version" '{content:"private-metric-fixture",cardVersion:$version}')" >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" "$base/cards/$card/comments" >/dev/null
post /me/activity-client-events '{"events":[{"action":"comment_disclosure","kind":"open","count":1},{"action":"comment_create","kind":"use","count":1},{"action":"comment_create","kind":"success","count":1,"durationMs":125},{"action":"mention_selection","kind":"use","count":1}]}' >/dev/null
test "$(curl --silent --show-error -o /dev/null -w '%{http_code}' -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"events":[{"action":"card_read","kind":"use","count":1}]}' "$base/me/activity-client-events")" = 401
test "$(curl --silent --show-error -o /dev/null -w '%{http_code}' -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"events":[{"action":"card_read","kind":"use","count":1,"cursor":"private-metric-fixture"}]}' "$base/me/activity-client-events")" = 400
test "$(curl --silent --show-error -o /dev/null -w '%{http_code}' -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"events":[{"action":"create","kind":"use","count":1}]}' "$base/me/checklist-client-events")" = 401
test "$(curl --silent --show-error -o /dev/null -w '%{http_code}' -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"events":[{"action":"create","kind":"use","count":1,"private":"private-metric-fixture"}]}' "$base/me/checklist-client-events")" = 400
test "$(curl --max-time 30 --silent --show-error -o /dev/null -w '%{http_code}' -b "$scratch/cookies" -H 'Content-Type: application/json' -d '{"events":[{"action":"create","kind":"use","count":1}]}' "$base/me/checklist-client-events")" = 403
post /me/activity-client-events '{"events":[{"action":"organization_settings_disclosure","kind":"open","count":1},{"action":"organization_settings_read","kind":"retry","count":1},{"action":"organization_settings_update","kind":"use","count":1},{"action":"organization_settings_update","kind":"success","count":1,"durationMs":125}]}' >/dev/null
# Synthetic ingestion/export contract; real browser production is checked separately.
post /me/activity-client-events '{"events":[{"action":"organization_members_disclosure","kind":"open","count":1},{"action":"organization_members_read","kind":"retry","count":1},{"action":"organization_members_read","kind":"success","count":1,"durationMs":125},{"action":"organization_member_review","kind":"use","count":1},{"action":"organization_member_review","kind":"success","count":1,"durationMs":125},{"action":"organization_member_remove","kind":"use","count":1},{"action":"organization_member_remove","kind":"retry","count":1},{"action":"organization_member_remove","kind":"conflict","count":1},{"action":"organization_member_remove","kind":"failure","count":1,"durationMs":250},{"action":"organization_member_remove","kind":"success","count":1,"durationMs":125}]}' >/dev/null
post /me/activity-client-events '{"events":[{"action":"organization_departure_disclosure","kind":"open","count":1},{"action":"organization_departure_read","kind":"retry","count":1},{"action":"organization_departure_read","kind":"success","count":1,"durationMs":125},{"action":"organization_departure_change","kind":"use","count":1},{"action":"organization_departure_change","kind":"retry","count":1},{"action":"organization_departure_change","kind":"conflict","count":1},{"action":"organization_departure_change","kind":"failure","count":1,"durationMs":250},{"action":"organization_departure_change","kind":"success","count":1,"durationMs":125}]}' >/dev/null
post /me/activity-client-events '{"events":[{"action":"organization_deletion_disclosure","kind":"open","count":1},{"action":"organization_deletion_read","kind":"retry","count":1},{"action":"organization_deletion_read","kind":"success","count":1,"durationMs":125},{"action":"organization_deletion_change","kind":"use","count":1},{"action":"organization_deletion_change","kind":"retry","count":1},{"action":"organization_deletion_change","kind":"conflict","count":1},{"action":"organization_deletion_change","kind":"failure","count":1,"durationMs":250},{"action":"organization_deletion_change","kind":"success","count":1,"durationMs":125},{"action":"organization_deletion_status","kind":"use","count":1},{"action":"organization_deletion_status","kind":"retry","count":1},{"action":"organization_deletion_status","kind":"failure","count":1,"durationMs":250},{"action":"organization_deletion_status","kind":"success","count":1,"durationMs":125},{"action":"organization_deletion_completion","kind":"open","count":1}]}' >/dev/null
# PRD-27: actual authorized HTTP operations, not synthetic server observations.
configuration="/organizations/$org/configuration"
get "$configuration" >/dev/null
configuration_input='{"version":0,"configuration":{"legalName":"private-metric-fixture","jurisdiction":"CA-BC","timezone":"UTC"}}'
patch "$configuration" "$configuration_input" >/dev/null
for suffix in history activity events; do get "$configuration/$suffix" >/dev/null; done
test "$(curl --max-time 30 --silent --show-error -o /dev/null -w '%{http_code}' -b "$scratch/cookies" -X PATCH -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$configuration_input" "$base$configuration")" = 409
lifecycle="/organizations/$org/operational-lifecycle"
version=$(get "$lifecycle" | jq -er '.version')
[[ "$version" =~ ^[1-9][0-9]*$ ]]
get /organizations/operational-lifecycle-directory >/dev/null
get "$lifecycle/events" >/dev/null
patch "$lifecycle" "$(jq -nc --argjson version "$version" '{version:$version,status:"SUSPENDED"}')" >/dev/null
test "$(curl --max-time 30 --silent --show-error -o /dev/null -w '%{http_code}' -b "$scratch/cookies" "$base$configuration")" = 404
version=$(get "$lifecycle" | jq -er '.version')
[[ "$version" =~ ^[1-9][0-9]*$ ]]
patch "$lifecycle" "$(jq -nc --argjson version "$version" '{version:$version,status:"ACTIVE"}')" >/dev/null
post /me/activity-client-events '{"events":[{"action":"organization_configuration_disclosure","kind":"open","count":1},{"action":"organization_configuration_read","kind":"reconnect","count":1},{"action":"organization_configuration_change","kind":"retry","count":1},{"action":"organization_configuration_change","kind":"failure","count":1,"durationMs":125},{"action":"organization_configuration_change","kind":"success","count":1,"durationMs":125},{"action":"organization_configuration_history","kind":"success","count":1,"durationMs":125},{"action":"organization_configuration_activity","kind":"success","count":1,"durationMs":125}]}' >/dev/null
post /me/activity-client-events '{"events":[{"action":"organization_lifecycle_disclosure","kind":"open","count":1},{"action":"organization_lifecycle_read","kind":"reconnect","count":1},{"action":"organization_lifecycle_change","kind":"retry","count":1},{"action":"organization_lifecycle_change","kind":"conflict","count":1},{"action":"organization_lifecycle_change","kind":"failure","count":1,"durationMs":125},{"action":"organization_lifecycle_change","kind":"success","count":1,"durationMs":125}]}' >/dev/null
# Poll bounded export/batch intervals; raw scrapes never become retained artifacts.
post /me/activity-client-events '{"events":[{"action":"organization_creation_disclosure","kind":"open","count":1},{"action":"organization_creation","kind":"retry","count":1},{"action":"organization_creation","kind":"success","count":1,"durationMs":125}]}' >/dev/null
collect() {
  local target="$1"
  shift
  for attempt in $(seq 1 30); do
    if curl --max-time 3 --fail --silent http://127.0.0.1:9464/metrics > "$scratch/metrics" &&
      python3 scripts/ci/verify-operator-metrics.py "$scratch/metrics" "$GITHUB_SHA" "$STRATAAI_BUILD_VERSION" "$target" "$@"; then return 0; fi
    sleep 2
  done
  return 1
}
worker_probe() {
  local endpoint="$1" expected="$2" code
  code=$(curl --max-time 8 --silent --show-error -o "$scratch/worker-state" -w '%{http_code}' "http://127.0.0.1:8081/$endpoint")
  test "$code" = "$expected"
  if [[ "$endpoint" = healthz ]]; then
    jq -e 'keys==["service","status","timestamp"] and .status=="ok" and .service=="strataai-worker" and (.timestamp|type)=="string"' "$scratch/worker-state" >/dev/null
  elif [[ "$expected" = 503 ]]; then
    jq -e '.=={"status":"not-ready"}' "$scratch/worker-state" >/dev/null
  else
    jq -e '.=={"status":"ready","mode":"production"}' "$scratch/worker-state" >/dev/null
  fi
}
fingerprint() { admin "SELECT md5(string_agg(row_to_json(j)::text,'' ORDER BY id)) FROM background_jobs j WHERE id IN ('$backlog_pending','$backlog_running') HAVING count(*)=2;"; }
before_jobs=$(fingerprint)
[[ "$before_jobs" =~ ^[0-9a-f]{32}$ ]]
worker_probe healthz 200
worker_probe readyz 200
collect "$scratch/evidence.json"

# Actual dependency failure, not synthetic zero counts. Only this disposable
# CI project's database is stopped; its existing volume must survive recovery.
api_process() { docker inspect --format '{{.Id}} {{.State.StartedAt}}' "$(docker compose -f compose.release.yml ps -q api)"; }
before_api=$(api_process)
test -n "$before_api"
postgres_stopped=true
docker compose -f compose.release.yml stop postgres >/dev/null
worker_probe healthz 200
worker_probe readyz 503
collect "$scratch/backlog-unavailable.json" --backlog-unavailable
docker compose -f compose.release.yml up -d --wait --wait-timeout 180 postgres >/dev/null
postgres_stopped=false
collect "$scratch/backlog-recovered.json"
worker_probe healthz 200
worker_probe readyz 200
# Readiness uses an independent pool. The first business request must also
# recover in the same API process, without client-side mutation retries.
test "$(curl --max-time 8 --silent --show-error -o "$scratch/api-state" -w '%{http_code}' http://127.0.0.1:8080/readyz)" = 200
jq -e '.=={"status":"ready","mode":"production"}' "$scratch/api-state" >/dev/null
recovery_email="metrics-recovery-$(uuid)@example.test"
recovery_body=$(jq -nc --arg email "$recovery_email" '{email:$email,password:"private-metric-recovery-password",displayName:"private-metric-recovery"}')
test "$(curl --max-time 30 --silent --show-error -o "$scratch/recovered-account" -w '%{http_code}' -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$recovery_body" "$base/auth/register")" = 201
test "$(admin "SELECT (SELECT count(*) FROM users WHERE lower(email_normalized)=lower('$recovery_email'))::text || ':' || (SELECT count(*) FROM identity_events e JOIN users u ON u.id=e.user_id WHERE e.event_type='USER_REGISTERED' AND lower(u.email_normalized)=lower('$recovery_email'))::text;")" = 1:1
test "$(api_process)" = "$before_api"
test "$(fingerprint)" = "$before_jobs"
mkdir -p artifacts/metrics
mv "$scratch/evidence.json" artifacts/metrics/operator.json
mv "$scratch/backlog-unavailable.json" artifacts/metrics/backlog-unavailable.json
mv "$scratch/backlog-recovered.json" artifacts/metrics/backlog-recovered.json
echo 'Exact-image operator ingestion, database recovery, first-request account persistence, unchanged API process/backlog rows, health/readiness and private-field checks passed.'
