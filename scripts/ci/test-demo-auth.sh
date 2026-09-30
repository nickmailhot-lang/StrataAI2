#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:18080}"
COOKIE_JAR="$(mktemp)"
trap 'rm -f "$COOKIE_JAR"' EXIT

register_status="$(
  curl --silent --output /tmp/register.json --write-out '%{http_code}'     -H 'Content-Type: application/json'     -d '{"email":"council@example.test","password":"correct-horse-battery-staple","displayName":"Council Test","locale":"en-CA","timezone":"America/Vancouver"}'     "$BASE_URL/auth/register"
)"
test "$register_status" = "201"

login_status="$(
  curl --silent --output /tmp/login.json --write-out '%{http_code}'     -c "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d '{"email":"COUNCIL@example.test","password":"correct-horse-battery-staple"}'     "$BASE_URL/auth/login"
)"
test "$login_status" = "200"

me_status="$(
  curl --silent --output /tmp/me.json --write-out '%{http_code}'     -b "$COOKIE_JAR"     "$BASE_URL/me"
)"
test "$me_status" = "200"
grep -q '"email":"council@example.test"' /tmp/me.json

"$(dirname "$0")/test-demo-organizations.sh" "$BASE_URL" "$COOKIE_JAR"
"$(dirname "$0")/test-demo-onboarding.sh" "$BASE_URL" "$COOKIE_JAR"

forgot_response="$(
  curl --fail --silent     -H 'Content-Type: application/json'     -d '{"email":"council@example.test"}'     "$BASE_URL/auth/password/forgot"
)"
reset_token="$(printf '%s' "$forgot_response" | jq -r '.resetToken')"
test -n "$reset_token"
test "$reset_token" != "null"

curl --fail --silent   -H 'Content-Type: application/json'   -d "$(jq -nc --arg token "$reset_token"     '{token:$token,newPassword:"new-correct-horse-battery-staple"}')"   "$BASE_URL/auth/password/reset" >/dev/null

revoked_status="$(
  curl --silent --output /dev/null --write-out '%{http_code}'     -b "$COOKIE_JAR"     "$BASE_URL/me"
)"
test "$revoked_status" = "401"

curl --fail --silent   -c "$COOKIE_JAR"   -H 'Content-Type: application/json'   -d '{"email":"council@example.test","password":"new-correct-horse-battery-staple"}'   "$BASE_URL/auth/login" >/dev/null

curl --fail --silent   -b "$COOKIE_JAR"   -X POST   "$BASE_URL/me/deactivate" >/dev/null

post_deactivate_login="$(
  curl --silent --output /dev/null --write-out '%{http_code}'     -H 'Content-Type: application/json'     -d '{"email":"council@example.test","password":"new-correct-horse-battery-staple"}'     "$BASE_URL/auth/login"
)"
test "$post_deactivate_login" = "401"

echo "Demo authentication lifecycle checks passed."
