#!/usr/bin/env bash
set -euo pipefail
image="${1:?Pass exact Worker image tag}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
expect_failure() {
  local mode="$1" scope="$2" message="$3" status
  set +e
  timeout 30 docker run --rm -e "STRATAAI_RUNTIME_MODE=$mode" \
    -e 'ConnectionStrings__Postgres=Host=unused;Database=unused;Username=unused;Password=unused' \
    -e "STRATAAI_WORKER_ORGANIZATION_IDS=$scope" "$image" >"$scratch/result" 2>&1
  status=$?
  set -e
  test "$status" != 0 && test "$status" != 124
  grep -Fq "$message" "$scratch/result"
}
# ARCH-07-TC-01 / ARCH-06-FR-012: an operator must explicitly scope handlers.
expect_failure demo '11111111-1111-1111-1111-111111111111' 'Organization job execution requires Production mode.'
expect_failure production 'not-an-id' 'Worker Organization scope contains an invalid ID.'
expect_failure production '00000000-0000-0000-0000-000000000000' 'Worker Organization scope contains an invalid ID.'
expect_failure production '11111111-1111-1111-1111-111111111111,' 'Worker Organization scope contains an invalid ID.'
large_scope="$(printf '%08d-0000-0000-0000-000000000001,' $(seq 1 101))"
expect_failure production "${large_scope%,}" 'Worker Organization scope exceeds 100 IDs.'

expect_discovery_failure() {
  local mode="$1" enabled="$2" message="$3" setting="${4:-STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED}" status
  set +e
  timeout 30 docker run --rm -e "STRATAAI_RUNTIME_MODE=$mode" \
    -e 'ConnectionStrings__Postgres=Host=unused;Database=unused;Username=unused;Password=unused' \
    -e "$setting=$enabled" "$image" >"$scratch/result" 2>&1
  status=$?
  set -e
  test "$status" != 0 && test "$status" != 124
  grep -Fq "$message" "$scratch/result"
}
expect_discovery_failure demo true 'Organization deletion discovery requires Production mode.'
expect_discovery_failure production invalid 'Organization deletion discovery setting must be true or false.'
expect_discovery_failure demo true 'Organization metadata discovery requires Production mode.' STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED
expect_discovery_failure production invalid 'Organization metadata discovery setting must be true or false.' STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED
expect_discovery_failure demo true 'Organization configuration discovery requires Production mode.' STRATAAI_ORGANIZATION_CONFIGURATION_DISCOVERY_ENABLED
expect_discovery_failure production invalid 'Organization configuration discovery setting must be true or false.' STRATAAI_ORGANIZATION_CONFIGURATION_DISCOVERY_ENABLED

echo 'Exact Worker image rejects invalid, unbounded, Demo job execution and invalid/Demo deletion discovery. Actual automatic discovery and leased deletion run against PostgreSQL in the Organization command fixture.'
