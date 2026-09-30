#!/usr/bin/env bash
set -euo pipefail
# PRD-24 SEC-FR-005 / TC-03/04: denied requests must not mutate or revoke.
BASE_URL="${1:?Pass API URL}"
COOKIE_JAR="${2:?Pass authenticated cookie jar}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "CSRF check failed at line $LINENO" >&2' ERR
curl --fail --silent --show-error -b "$COOKIE_JAR" "$BASE_URL/me" > "$scratch/before.json"
for path in /auth/logout /me/deactivate /auth/password/forgot; do
  status="$(curl --silent --show-error -o "$scratch/problem.json" -w '%{http_code}' \
    -X POST -b "$COOKIE_JAR" -H 'Content-Type: application/json' -d '{}' "$BASE_URL$path")"
  test "$status" = '403'
  jq -e '.code == "csrf_rejected"' "$scratch/problem.json" >/dev/null
done
for site in cross-site same-site; do
  status="$(curl --silent --show-error -o "$scratch/problem.json" -w '%{http_code}' \
    -X POST -b "$COOKIE_JAR" -H 'X-StrataAI-Request: 1' -H "Sec-Fetch-Site: $site" \
    "$BASE_URL/auth/logout")"
  test "$status" = '403'
done
status="$(curl --silent --show-error -o "$scratch/problem.json" -w '%{http_code}' \
  -X POST -H 'Content-Type: application/x-www-form-urlencoded' \
  -d 'email=attacker@example.test&password=wrong' "$BASE_URL/auth/login")"
test "$status" = '403'
curl --silent --show-error -o /dev/null -D "$scratch/preflight.headers" -X OPTIONS \
  -H 'Origin: https://attacker.example' -H 'Access-Control-Request-Method: POST' \
  -H 'Access-Control-Request-Headers: X-StrataAI-Request' "$BASE_URL/auth/logout"
if grep -qi '^access-control-allow-origin:' "$scratch/preflight.headers"; then
  echo 'Unexpected cross-origin CORS grant' >&2
  exit 1
fi
curl --fail --silent --show-error -b "$COOKIE_JAR" "$BASE_URL/me" > "$scratch/after.json"
cmp "$scratch/before.json" "$scratch/after.json"
echo 'CSRF header, fetch metadata, form attack, CORS and state-preservation checks passed.'
