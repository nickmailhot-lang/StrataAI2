#!/usr/bin/env bash
set -euo pipefail
api="${1:?Pass exact API image tag}"
worker="${2:?Pass exact Worker image tag}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
scope=11111111-1111-1111-1111-111111111111
expect_failure() {
  local image="$1" mode="$2" message="$3" status
  shift 3
  set +e
  timeout 30 docker run --rm --network none \
    -e "STRATAAI_RUNTIME_MODE=$mode" \
    -e 'ConnectionStrings__Postgres=Host=unused;Database=unused;Username=unused;Password=unused' \
    -e STRATAAI_ATTACHMENT_STORAGE_ENABLED=true \
    -e STRATAAI_ATTACHMENT_S3_BUCKET=strata-private-ci-fixture \
    -e STRATAAI_ATTACHMENT_S3_OWNER=123456789012 \
    -e STRATAAI_ATTACHMENT_S3_REGION=us-east-1 \
    -e "STRATAAI_WORKER_ORGANIZATION_IDS=$scope" \
    -e STRATAAI_ATTACHMENT_SCANNER_SOCKET=/run/strata-scanner/clamd.sock \
    -e AWS_EC2_METADATA_DISABLED=true \
    "$@" "$image" >"$scratch/result" 2>&1
  status=$?
  set -e
  test "$status" != 0 && test "$status" != 124
  grep -Fq "$message" "$scratch/result"
}
for image in "$api" "$worker"; do
  expect_failure "$image" production 'Attachment storage enablement is invalid.' -e STRATAAI_ATTACHMENT_STORAGE_ENABLED=invalid
  expect_failure "$image" demo 'Managed attachment storage requires Production mode.'
  expect_failure "$image" production 'A canonical managed storage bucket is required.' -e STRATAAI_ATTACHMENT_S3_BUCKET=unsafe/bucket
  expect_failure "$image" production 'An expected managed storage owner is required.' -e STRATAAI_ATTACHMENT_S3_OWNER=123
  expect_failure "$image" production 'A supported explicit attachment storage region is required.' -e STRATAAI_ATTACHMENT_S3_REGION=custom-region
done
expect_failure "$worker" production 'Attachment scanning requires explicit Worker Organization scope.' -e STRATAAI_WORKER_ORGANIZATION_IDS=
expect_failure "$worker" production 'An absolute local scanner socket path is required.' -e STRATAAI_ATTACHMENT_SCANNER_SOCKET=relative.sock
expect_failure "$worker" production 'An absolute local scanner socket path is required.' -e STRATAAI_ATTACHMENT_SCANNER_SOCKET=/run/../scanner.sock
echo 'Exact API/Worker images reject malformed enabled storage, Demo activation, invalid bucket/owner/region and unscoped or invalid local scanning. No provider network or cloud credentials were used.'
