#!/usr/bin/env bash
set -euo pipefail
[[ "${CI:-}" == true && "${GITHUB_ACTIONS:-}" == true ]]
compose=(docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.identity-test.yml)
previous_mail=${STRATAAI_INVITATION_EMAIL_ENABLED:-false}
previous_verification=${STRATAAI_TEST_REQUIRE_VERIFIED_EMAIL:-true}
report=$(mktemp "${RUNNER_TEMP:-/tmp}/strataai-recipient-clock.XXXXXXXX")
cleanup() {
  local code=$?; trap - EXIT
  if ! STRATAAI_INVITATION_EMAIL_ENABLED="$previous_mail" STRATAAI_TEST_REQUIRE_VERIFIED_EMAIL="$previous_verification" "${compose[@]}" up -d --pull never --no-deps --force-recreate --wait --wait-timeout 180 api worker; then code=1; fi
  mkdir -p test-results || code=1
  if [ -s "$report" ]; then cp -- "$report" test-results/recipient-clock-report.json || code=1; fi
  rm -f -- "$report" || code=1
  exit "$code"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
export STRATAAI_INVITATION_EMAIL_ENABLED=true STRATAAI_TEST_REQUIRE_VERIFIED_EMAIL=true
"${compose[@]}" up -d --pull never --no-deps --force-recreate --wait --wait-timeout 180 api
STRATAAI_INVITATION_EMAIL_ENABLED=false "${compose[@]}" up -d --pull never --no-deps --force-recreate --wait --wait-timeout 180 worker
test "$("${compose[@]}" exec -T api printenv STRATAAI_INVITATION_EMAIL_ENABLED)" = true
test "$("${compose[@]}" exec -T worker printenv STRATAAI_INVITATION_EMAIL_ENABLED)" = false
for service in api worker; do
  test "$("${compose[@]}" exec -T "$service" printenv STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL)" = true
done
PLAYWRIGHT_JSON_OUTPUT_FILE="$report" npx playwright test --config playwright.recipient-clock.config.ts --reporter=list,json
node scripts/ci/verify-recipient-clock-browser.mjs "$report"
