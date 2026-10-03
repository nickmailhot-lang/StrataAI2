#!/usr/bin/env bash
set -euo pipefail
api="${1:?Pass exact API image tag}"
worker="${2:?Pass exact Worker image tag}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
scope=11111111-1111-1111-1111-111111111111
case_number=0
expect_failure() {
  local image="$1" mode="$2" message="$3" status key
  shift 3
  # Each override must replace its default instead of supplying duplicate
  # environment entries and depending on Docker/runtime precedence.
  local -A settings=(
    [STRATAAI_RUNTIME_MODE]="$mode"
    [ConnectionStrings__Postgres]='Host=unused;Database=unused;Username=unused;Password=unused'
    [STRATAAI_ATTACHMENT_STORAGE_ENABLED]=true
    [STRATAAI_ATTACHMENT_S3_BUCKET]=strata-private-ci-fixture
    [STRATAAI_ATTACHMENT_S3_OWNER]=123456789012
    [STRATAAI_ATTACHMENT_S3_REGION]=us-east-1
    [STRATAAI_WORKER_ORGANIZATION_IDS]="$scope"
    [STRATAAI_ATTACHMENT_SCANNER_SOCKET]=/run/strata-scanner/clamd.sock
    [AWS_EC2_METADATA_DISABLED]=true
  )
  while [ "$#" -gt 0 ]; do
    if [ "$#" -lt 2 ] || [ "$1" != -e ] || [[ "$2" != *=* ]]; then
      echo 'Invalid configuration refusal fixture override.' >&2; exit 2
    fi
    key="${2%%=*}"; settings["$key"]="${2#*=}"; shift 2
  done
  local -a environment=()
  for key in "${!settings[@]}"; do environment+=(-e "$key=${settings[$key]}"); done
  case_number=$((case_number + 1))
  echo "Checking attachment configuration refusal case $case_number."
  set +e
  timeout 30 docker run --rm --network none "${environment[@]}" "$image" >"$scratch/result" 2>&1
  status=$?
  set -e
  if [ "$status" -eq 0 ] || [ "$status" -eq 124 ] || ! grep -Fq "$message" "$scratch/result"; then
    # Raw startup exceptions may contain provider diagnostics. Retain only the
    # fixture number and exit status, never dump arbitrary container output.
    echo "Attachment configuration refusal case $case_number failed (exit $status)." >&2
    exit 1
  fi
}
for image in "$api" "$worker"; do
  expect_failure "$image" production 'Attachment storage enablement is invalid.' -e STRATAAI_ATTACHMENT_STORAGE_ENABLED=invalid
  expect_failure "$image" demo 'Managed attachment storage requires Production mode.'
  expect_failure "$image" production 'A canonical managed storage bucket is required.' -e STRATAAI_ATTACHMENT_S3_BUCKET=unsafe/bucket
  expect_failure "$image" production 'An expected managed storage owner is required.' -e STRATAAI_ATTACHMENT_S3_OWNER=123
  expect_failure "$image" production 'A supported explicit attachment storage region is required.' -e STRATAAI_ATTACHMENT_S3_REGION=custom-region
  expect_failure "$image" production 'Attachment upload size policy is invalid.' -e STRATAAI_ATTACHMENT_MAX_BYTES=invalid
  expect_failure "$image" production 'Attachment upload policy is invalid.' -e STRATAAI_ATTACHMENT_ALLOWED_TYPES=image/svg+xml
done
expect_failure "$worker" production 'Attachment scanning requires explicit Worker Organization scope.' -e STRATAAI_WORKER_ORGANIZATION_IDS=
expect_failure "$worker" production 'An absolute local scanner socket path is required.' -e STRATAAI_ATTACHMENT_SCANNER_SOCKET=relative.sock
expect_failure "$worker" production 'An absolute local scanner socket path is required.' -e STRATAAI_ATTACHMENT_SCANNER_SOCKET=/run/../scanner.sock
echo 'Exact API/Worker images reject malformed enabled storage, Demo activation, invalid bucket/owner/region and unscoped or invalid local scanning. No provider network or cloud credentials were used.'
