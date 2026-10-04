#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable operator metrics fixture is CI-only.' >&2; exit 1; }
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
base=http://localhost:8088
uuid() { cat /proc/sys/kernel/random/uuid; }
post() { curl --max-time 30 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$2" "$base$1"; }
credentials=$(jq -nc --arg email "metrics-$(uuid)@example.test" '{email:$email,password:"private-metric-fixture-password",displayName:"private-metric-fixture"}')
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/register" >/dev/null
curl --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null
org=$(post /organizations '{"name":"private-metric-fixture"}' | jq -r '.organization.id')
board=$(post /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"private-metric-fixture",visibility:"PRIVATE"}')" | jq -r '.id')
list=$(post "/boards/$board/lists" '{"name":"private-metric-fixture"}' | jq -r '.id')
card=$(post "/lists/$list/cards" '{"title":"private-metric-fixture"}' | jq -r '.id')
post "/cards/$card/checklists" '{"title":"private-metric-fixture","cardVersion":1}' >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" "$base/cards/$card/checklists" >/dev/null
post /me/checklist-client-events '{"events":[{"action":"disclosure","kind":"open","count":1},{"action":"create","kind":"use","count":1},{"action":"create","kind":"success","count":1,"durationMs":125}]}' >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" "$base/cards/$card/activity" >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" "$base/boards/$board/activity" >/dev/null
post /me/activity-client-events '{"events":[{"action":"card_disclosure","kind":"open","count":1},{"action":"board_disclosure","kind":"open","count":1},{"action":"card_read","kind":"retry","count":1},{"action":"card_read","kind":"success","count":1,"durationMs":125}]}' >/dev/null
test "$(curl --silent --show-error -o /dev/null -w '%{http_code}' -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"events":[{"action":"card_read","kind":"use","count":1}]}' "$base/me/activity-client-events")" = 401
test "$(curl --silent --show-error -o /dev/null -w '%{http_code}' -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"events":[{"action":"card_read","kind":"use","count":1,"cursor":"private-metric-fixture"}]}' "$base/me/activity-client-events")" = 400
test "$(curl --silent --show-error -o /dev/null -w '%{http_code}' -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"events":[{"action":"create","kind":"use","count":1}]}' "$base/me/checklist-client-events")" = 401
test "$(curl --silent --show-error -o /dev/null -w '%{http_code}' -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"events":[{"action":"create","kind":"use","count":1,"private":"private-metric-fixture"}]}' "$base/me/checklist-client-events")" = 400
# Poll bounded export/batch intervals; raw scrapes never become retained artifacts.
passed=false
for attempt in $(seq 1 30); do
  if curl --max-time 3 --fail --silent http://127.0.0.1:9464/metrics > "$scratch/metrics" &&
    python3 scripts/ci/verify-operator-metrics.py "$scratch/metrics" "$GITHUB_SHA" "$STRATAAI_BUILD_VERSION" "$scratch/evidence.json"; then passed=true; break; fi
  sleep 2
done
test "$passed" = true
mkdir -p artifacts/metrics
mv "$scratch/evidence.json" artifacts/metrics/operator.json
echo 'Exact-image authenticated observations reached the pinned OTLP Collector; fixed metric families, metadata and privacy checks passed.'
