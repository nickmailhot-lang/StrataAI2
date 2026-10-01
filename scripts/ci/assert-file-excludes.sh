#!/usr/bin/env bash
set -euo pipefail
if test "$#" != 2; then
  echo 'Expected a forbidden pattern and fixture file.' >&2
  exit 2
fi
# grep's 0/1/2 distinguish forbidden content, clean content, and inspection error.
# Do not use standalone ! grep: Bash exempts inverted pipelines from errexit.
if grep -Eq -- "$1" "$2" 2>/dev/null; then
  echo 'Security fixture contains forbidden content.' >&2
  exit 1
else
  status=$?
  if test "$status" = 1; then exit 0; fi
  echo 'Security fixture could not be inspected.' >&2
  exit 2
fi
