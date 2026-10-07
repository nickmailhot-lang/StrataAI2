#!/usr/bin/env bash
set -euo pipefail

# ARCH-05-AC-002 / ARCH-12 / PRD-02: actual retained host images must reject
# missing/unknown mode, missing Production persistence and invalid API policy.
api_image="${1:?Pass the exact retained API image}"
worker_image="${2:?Pass the exact retained Worker image}"
scratch="$(mktemp -d)"
name=''
sequence=0
cleanup() {
  if [ -n "$name" ]; then docker stop "$name" >/dev/null 2>&1 || true; fi
  rm -rf "$scratch"
}
trap cleanup EXIT
expect_refusal() {
  local image="$1" mode="$2" expected="$3" status
  shift 3
  sequence=$((sequence + 1))
  name="strataai-startup-refusal-$$-$sequence"
  status=0
  timeout 30 docker run --rm --name "$name" --network none \
    -e "STRATAAI_RUNTIME_MODE=$mode" -e ASPNETCORE_ENVIRONMENT=Development \
    -e Logging__LogLevel__Default=Warning \
    -e ConnectionStrings__Postgres= -e STRATAAI_DATABASE_HOST= \
    -e STRATAAI_DATABASE_NAME= -e STRATAAI_DATABASE_USERNAME= -e STRATAAI_DATABASE_PASSWORD= \
    -e STRATAAI_IDENTITY_EMAIL_ENABLED=false -e STRATAAI_ATTACHMENT_STORAGE_ENABLED=false \
    "$@" "$image" > "$scratch/refusal" 2>&1 || status=$?
  # A hang or unrelated crash does not prove the intended startup boundary.
  if [ "$status" = 0 ] || [ "$status" = 124 ]; then
    echo "Startup refusal case $sequence did not terminate with the required rejection." >&2; exit 1
  fi
  if ! grep -Fq "$expected" "$scratch/refusal"; then
    echo "Startup refusal case $sequence did not produce its expected configuration error." >&2; exit 1
  fi
  if grep -Fq 'invalid-policy-value' "$scratch/refusal"; then
    echo 'Startup failure echoed a supplied policy value.' >&2; exit 1
  fi
  docker stop "$name" >/dev/null 2>&1 || true
  name=''
}
for image in "$api_image" "$worker_image"; do
  expect_refusal "$image" '' 'STRATAAI_RUNTIME_MODE is required'
  expect_refusal "$image" hybrid 'STRATAAI_RUNTIME_MODE must be either'
  expect_refusal "$image" production 'Production mode requires a database connection string or complete runtime credentials.'
done
for mode in demo production; do
  for policy in 'STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL=invalid-policy-value' \
    'STRATAAI_AUTH_SESSION_HOURS=721' 'STRATAAI_AUTH_MIN_PASSWORD_LENGTH=7'; do
    key="${policy%%=*}"
    expect_refusal "$api_image" "$mode" "$key must be" \
      -e 'ConnectionStrings__Postgres=Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=fixture;Timeout=1' \
      -e "$policy"
  done
done
test "$sequence" = 12
echo 'Both retained hosts reject missing/unknown mode and missing Production persistence; API policy refuses across both modes (12 startup cases).'
