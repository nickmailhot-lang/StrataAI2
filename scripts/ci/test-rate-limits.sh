#!/usr/bin/env bash
set -euo pipefail
BASE_URL="${1:?Pass API or edge URL}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Rate-limit check failed at line $LINENO" >&2' ERR
# PRD-24 SEC-FR-010: invalid inputs still consume capacity; spoofed forwarding
# headers cannot obtain fresh capacity. No passwords or real accounts are used.
for path in /auth/login /organizations/00000000-0000-0000-0000-000000000001/invitations; do
  limited=false
  for attempt in $(seq 1 200); do
    status="$(curl --silent --show-error -o "$scratch/problem.json" -D "$scratch/headers" -w '%{http_code}' \
      -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
      -H "X-Forwarded-For: 192.0.2.$attempt" -H "X-Real-IP: 192.0.2.$attempt" \
      -d '{"email":"","password":""}' "$BASE_URL$path")"
    if [ "$status" = '429' ]; then
      jq -e '.code == "rate_limit_exceeded"' "$scratch/problem.json" >/dev/null
      grep -Eiq '^retry-after: [1-9][0-9]*' "$scratch/headers"
      if [ "${2:-}" = 'security-headers' ]; then
        grep -Eiq '^content-security-policy: .*script-src .self.' "$scratch/headers"
        grep -Eiq '^x-content-type-options: nosniff' "$scratch/headers"
        grep -Eiq '^x-frame-options: DENY' "$scratch/headers"
        grep -Eiq '^referrer-policy: no-referrer' "$scratch/headers"
      fi
      limited=true
      break
    fi
    test "$status" = '401' || test "$status" = '400'
  done
  test "$limited" = true
done
curl --fail --silent --show-error "$BASE_URL/api/health" >/dev/null
echo 'Authentication/invitation rate limits, retry metadata, spoof resistance and health isolation passed.'
