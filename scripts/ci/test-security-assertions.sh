#!/usr/bin/env bash
set -euo pipefail
# ARCH-09-AC-001 / ARCH-11: a privacy regression must fail the required gate.
scratch="$(mktemp -d "${1:-${TMPDIR:-/tmp}}/strata-security-assertions.XXXXXX")"
cleanup() {
  rm -f -- "$scratch/clean.json" "$scratch/forbidden fixture.json" "$scratch/result.log"
  rmdir -- "$scratch"
}
trap cleanup EXIT
printf '{"code":"board_not_found"}\n' > "$scratch/clean.json"
printf '{"debug":"Protected token-never-print-91847"}\n' > "$scratch/forbidden fixture.json"
expect_status() {
  local expected="$1" actual
  shift
  if "$@" > "$scratch/result.log" 2>&1; then actual=0; else actual=$?; fi
  if test "$actual" != "$expected"; then
    echo 'Security assertion returned an unexpected exit status.' >&2
    return 1
  fi
  scripts/ci/assert-file-excludes.sh 'token-never-print-91847' "$scratch/result.log"
}
expect_status 0 scripts/ci/assert-file-excludes.sh 'Protected|Npgsql' "$scratch/clean.json"
expect_status 1 scripts/ci/assert-file-excludes.sh 'Protected|Npgsql' "$scratch/forbidden fixture.json"
expect_status 2 scripts/ci/assert-file-excludes.sh 'Protected' "$scratch/missing.json"
expect_status 2 scripts/ci/assert-file-excludes.sh '[' "$scratch/clean.json"
expect_status 1 scripts/ci/assert-file-excludes.sh '' "$scratch/clean.json"
# A matching fixture must terminate the caller, not merely invert grep's result.
expect_status 1 bash -euo pipefail -c 'scripts/ci/assert-file-excludes.sh "$1" "$2"; echo RELEASE_SHOULD_NOT_PASS' _ 'Protected' "$scratch/forbidden fixture.json"
scripts/ci/assert-file-excludes.sh 'RELEASE_SHOULD_NOT_PASS' "$scratch/result.log"
echo 'Security assertions reject forbidden output, inspection errors, and unsafe gate continuation.'
