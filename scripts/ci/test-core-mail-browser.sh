#!/usr/bin/env bash
set -euo pipefail
[[ "${CI:-}" == true && "${GITHUB_ACTIONS:-}" == true ]]
test -n "${STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE:-}"
provider=${STRATAAI_E2E_IDENTITY_PROVIDER_URL:-http://localhost:19090}
admitted() {
  curl --max-time 5 --fail --silent "$provider/health" \
    | jq -e --arg expected "$STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE" '.ready==true and .instance==$expected' >/dev/null
}
admitted
report=$(mktemp "${RUNNER_TEMP:-/tmp}/strataai-core-mail-browser.XXXXXXXX")
cleanup() {
  local code=$?; trap - EXIT
  mkdir -p test-results || code=1
  if [ -s "$report" ]; then cp -- "$report" test-results/core-mail-outage-report.json || code=1; fi
  rm -f -- "$report" || code=1
  if ! admitted || ! curl --max-time 5 --fail --silent -H 'Content-Type: application/json' \
    -d '{"outage":false}' "$provider/control" >/dev/null; then code=1; fi
  exit "$code"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
processes() {
  local service identity
  for service in api worker; do
    identity=$(docker compose -f compose.release.yml ps -q "$service"); test -n "$identity"
    docker inspect --format '{{.Id}} {{.State.StartedAt}}' "$identity"
  done
}
before=$(processes)
PLAYWRIGHT_JSON_OUTPUT_FILE="$report" npx playwright test --config playwright.mail-outage.config.ts --reporter=list,json
node scripts/ci/verify-core-mail-browser.mjs "$report"
test "$before" = "$(processes)"
echo 'Desktop/phone mail outage browser checks retained the original API and Worker processes.'
