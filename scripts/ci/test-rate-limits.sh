#!/usr/bin/env bash
set -euo pipefail
BASE_URL="${1:?Pass API or edge URL}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Rate-limit check failed at line $LINENO" >&2' ERR
# An unmapped sensitive path has no API rate policy, so its 429 proves that the
# Nginx limit/error location itself executes, independently of API exhaustion.
if [ "${2:-}" = 'security-headers' ]; then
  edge_limited=false
  for attempt in $(seq 1 200); do
    status="$(curl --silent --show-error -o "$scratch/problem.json" -D "$scratch/headers" -w '%{http_code}' \
      -H "X-Forwarded-For: 198.51.100.$attempt" "$BASE_URL/auth/not-a-route")"
    if [ "$status" = '429' ]; then
      jq -e '.code == "rate_limit_exceeded"' "$scratch/problem.json" >/dev/null
      grep -Eiq '^retry-after: 60' "$scratch/headers"
      grep -Eiq '^content-security-policy: .*script-src .self.' "$scratch/headers"
      grep -Eiq '^x-content-type-options: nosniff' "$scratch/headers"
      grep -Eiq '^x-frame-options: DENY' "$scratch/headers"
      grep -Eiq '^referrer-policy: no-referrer' "$scratch/headers"
      edge_limited=true
      break
    fi
    test "$status" = '404'
  done
  test "$edge_limited" = true
fi
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
  if [ "$path" = /auth/login ]; then
    # AUTH-FR-009 / SEC-FR-010: changing sensitive routes or spoofing a new
    # peer must not escape the exhausted authentication partition.
    routes=(/auth/register /auth/password/forgot /auth/password/reset /auth/verification/resend /auth/verify-email /auth/logout)
    pids=()
    for index in "${!routes[@]}"; do
      route="${routes[$index]}"
      # Assertions spawn processes: do not confuse time spent checking replies
      # with Nginx's continuously replenished 60r/m capacity. Each concurrent
      # pair must include a refusal; direct API fixed-window pairs both refuse.
      for probe in 1 2; do
        curl --max-time 60 --silent --show-error -o "$scratch/shared-$index-$probe.json" -D "$scratch/shared-$index-$probe.headers" -w '%{http_code}' \
          -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
          -H 'X-Forwarded-For: 203.0.113.245' -H 'X-Real-IP: 203.0.113.246' \
          -d '{"email":"","password":"","displayName":"","token":"private-rate-probe-token","newPassword":""}' \
          "$BASE_URL$route" > "$scratch/shared-$index-$probe.status" &
        pids+=("$!")
      done
    done
    for pid in "${pids[@]}"; do wait "$pid"; done
    for index in "${!routes[@]}"; do
      denials=0
      for probe in 1 2; do
        status="$(cat "$scratch/shared-$index-$probe.status")"
        if [ "$status" != 429 ]; then
          test "${2:-}" = security-headers
          # Empty credentials cannot register or consume a valid token. An
          # anonymous logout/neutral recovery acknowledgment has no account effect.
          case "$status" in 400|401|202|204|503) ;; *) exit 1 ;; esac
          continue
        fi
        denials=$((denials + 1))
        jq -e '.code=="rate_limit_exceeded" and .status==429' "$scratch/shared-$index-$probe.json" >/dev/null
        grep -Eiq '^retry-after: [1-9][0-9]*' "$scratch/shared-$index-$probe.headers"
        scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/shared-$index-$probe.headers"
        scripts/ci/assert-file-excludes.sh 'private-rate-probe|verificationToken|resetToken|sessionToken' "$scratch/shared-$index-$probe.json"
        if [ "${2:-}" = 'security-headers' ]; then
          grep -Eiq '^content-security-policy: .*script-src .self.' "$scratch/shared-$index-$probe.headers"
          grep -Eiq '^x-content-type-options: nosniff' "$scratch/shared-$index-$probe.headers"
          grep -Eiq '^x-frame-options: DENY' "$scratch/shared-$index-$probe.headers"
          grep -Eiq '^referrer-policy: no-referrer' "$scratch/shared-$index-$probe.headers"
        fi
      done
      test "$denials" -ge 1
    done
  fi
done
curl --fail --silent --show-error "$BASE_URL/api/health" >/dev/null
echo 'Authentication/invitation rate limits, shared recovery-route budget, privacy, retry metadata, spoof resistance and health isolation passed.'
