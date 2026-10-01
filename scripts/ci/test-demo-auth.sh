#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:18080}"
COOKIE_JAR="$(mktemp)"
trap 'rm -f "$COOKIE_JAR"' EXIT

register_status="$(
  curl -H 'X-StrataAI-Request: 1' --silent --output /tmp/register.json --write-out '%{http_code}'     -H 'Content-Type: application/json'     -d '{"email":"council@example.test","password":"correct-horse-battery-staple","displayName":"Council Test","locale":"en-CA","timezone":"America/Vancouver"}'     "$BASE_URL/auth/register"
)"
if [ "$register_status" != "201" ]; then
  echo "Registration failed with HTTP $register_status" >&2
  cat /tmp/register.json >&2
  exit 1
fi

login_status="$(
  curl -H 'X-StrataAI-Request: 1' --silent --output /tmp/login.json --write-out '%{http_code}'     -c "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d '{"email":"COUNCIL@example.test","password":"correct-horse-battery-staple"}'     "$BASE_URL/auth/login"
)"
if [ "$login_status" != "200" ]; then
  echo "Login failed with HTTP $login_status" >&2
  cat /tmp/login.json >&2
  exit 1
fi

me_status="$(
  curl -H 'X-StrataAI-Request: 1' --silent --output /tmp/me.json --write-out '%{http_code}'     -b "$COOKIE_JAR"     "$BASE_URL/me"
)"
if [ "$me_status" != "200" ]; then
  echo "/me failed with HTTP $me_status" >&2
  cat /tmp/me.json >&2
  exit 1
fi
grep -q '"email":"council@example.test"' /tmp/me.json
"$(dirname "$0")/test-csrf.sh" "$BASE_URL" "$COOKIE_JAR"

# PRD-02-TC-01/03/04/06: persisted profile changes, validation and authorization.
profile_version="$(jq -r '.version' /tmp/me.json)"
profile="$(curl -H 'X-StrataAI-Request: 1' --fail --silent --show-error -X PATCH -b "$COOKIE_JAR" \
  -H 'Content-Type: application/json' \
  -d "$(jq -nc --argjson version "$profile_version" '{displayName:"Updated Council",avatarUrl:"https://example.test/avatar.png",locale:"fr-CA",timezone:"UTC",version:$version}')" \
  "$BASE_URL/me")"
test "$(printf '%s' "$profile" | jq -r '.displayName')" = "Updated Council"
test "$(printf '%s' "$profile" | jq -r '.timezone')" = "UTC"
profile_version="$(printf '%s' "$profile" | jq -r '.version')"
curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$COOKIE_JAR" "$BASE_URL/me" | jq -e '.locale == "fr-CA" and .timezone == "UTC"' >/dev/null
for invalid in '{"displayName":" "}' '{"avatarUrl":"javascript:alert(1)"}' '{"timezone":"Not/AZone"}' '{"locale":""}'; do
  status="$(curl -H 'X-StrataAI-Request: 1' --silent --output /tmp/invalid-profile.json --write-out '%{http_code}' \
    -X PATCH -b "$COOKIE_JAR" -H 'Content-Type: application/json' -d "$(printf '%s' "$invalid" | jq --argjson version "$profile_version" '. + {version:$version}')" "$BASE_URL/me")"
  test "$status" = "400"
done
curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$COOKIE_JAR" "$BASE_URL/me" | jq -e '.displayName == "Updated Council" and .timezone == "UTC"' >/dev/null
curl -H 'X-StrataAI-Request: 1' --fail --silent -X PATCH -b "$COOKIE_JAR" -H 'Content-Type: application/json' \
  -d "$(jq -nc --argjson version "$profile_version" '{avatarUrl:"",version:$version}')" "$BASE_URL/me" | jq -e '.avatarUrl == null' >/dev/null
unauthorized_profile="$(curl -H 'X-StrataAI-Request: 1' --silent --output /dev/null --write-out '%{http_code}' \
  -X PATCH -H 'Content-Type: application/json' -d '{"displayName":"Intruder"}' "$BASE_URL/me")"
test "$unauthorized_profile" = "401"
"$(dirname "$0")/test-profile-concurrency.sh" "$BASE_URL" "$COOKIE_JAR"

"$(dirname "$0")/test-demo-organizations.sh" "$BASE_URL" "$COOKIE_JAR"
"$(dirname "$0")/test-demo-onboarding.sh" "$BASE_URL" "$COOKIE_JAR"
"$(dirname "$0")/test-demo-work-management.sh" "$BASE_URL" "$COOKIE_JAR"

forgot_response="$(
  curl -H 'X-StrataAI-Request: 1' --fail --silent     -H 'Content-Type: application/json'     -d '{"email":"council@example.test"}'     "$BASE_URL/auth/password/forgot"
)"
reset_token="$(printf '%s' "$forgot_response" | jq -r '.resetToken')"
test -n "$reset_token"
test "$reset_token" != "null"

curl -H 'X-StrataAI-Request: 1' --fail --silent   -H 'Content-Type: application/json'   -d "$(jq -nc --arg token "$reset_token"     '{token:$token,newPassword:"new-correct-horse-battery-staple"}')"   "$BASE_URL/auth/password/reset" >/dev/null
replay_status="$(curl -H 'X-StrataAI-Request: 1' --silent --output /tmp/reset-replay.json --write-out '%{http_code}' \
  -H 'Content-Type: application/json' -d "$(jq -nc --arg token "$reset_token" '{token:$token,newPassword:"another-correct-horse-battery"}')" \
  "$BASE_URL/auth/password/reset")"
test "$replay_status" = '400'
jq -e '.code == "invalid_or_expired_token"' /tmp/reset-replay.json >/dev/null

revoked_status="$(
  curl -H 'X-StrataAI-Request: 1' --silent --output /dev/null --write-out '%{http_code}'     -b "$COOKIE_JAR"     "$BASE_URL/me"
)"
if [ "$revoked_status" != "401" ]; then
  echo "Password reset did not revoke the old session; /me returned $revoked_status" >&2
  exit 1
fi

curl -H 'X-StrataAI-Request: 1' --fail --silent   -c "$COOKIE_JAR"   -H 'Content-Type: application/json'   -d '{"email":"council@example.test","password":"new-correct-horse-battery-staple"}'   "$BASE_URL/auth/login" >/dev/null

# These earlier fixtures leave active Organizations owned by this account.
# Deactivation must refuse continuity loss, then succeed after real Owner grants.
test "$(curl -H 'X-StrataAI-Request: 1' --silent -o /tmp/demo-owner-denied.json -w '%{http_code}' -b "$COOKIE_JAR" -X POST "$BASE_URL/me/deactivate")" = 409
jq -e '.code=="organization_owner_required"' /tmp/demo-owner-denied.json >/dev/null
CONTINUITY_COOKIE="$(mktemp)"
trap 'rm -f "$COOKIE_JAR" "$CONTINUITY_COOKIE"' EXIT
replacement='{"email":"continuity-owner@example.test","password":"continuity-correct-horse-battery","displayName":"Continuity Owner"}'
curl -H 'X-StrataAI-Request: 1' --fail --silent -H 'Content-Type: application/json' -d "$replacement" "$BASE_URL/auth/register" >/dev/null
curl -H 'X-StrataAI-Request: 1' --fail --silent -c "$CONTINUITY_COOKIE" -H 'Content-Type: application/json' -d "$replacement" "$BASE_URL/auth/login" >/dev/null
owned="$(curl --fail --silent -b "$COOKIE_JAR" "$BASE_URL/organizations" | jq -r '.[] | select(.role==0 and .organization.status!=2) | .organization.id')"
test -n "$owned"
while IFS= read -r organization_id; do
  invitation="$(curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$COOKIE_JAR" -H 'Content-Type: application/json' \
    -d '{"email":"continuity-owner@example.test","surface":"INTERNAL","targetRole":"OWNER"}' "$BASE_URL/organizations/$organization_id/invitations" | jq -r '.invitationToken')"
  test "$invitation" != null
  curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$CONTINUITY_COOKIE" -X POST "$BASE_URL/invitations/$invitation/accept" >/dev/null
done <<< "$owned"
curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$COOKIE_JAR" -X POST "$BASE_URL/me/deactivate" >/dev/null

post_deactivate_login="$(
  curl -H 'X-StrataAI-Request: 1' --silent --output /dev/null --write-out '%{http_code}'     -H 'Content-Type: application/json'     -d '{"email":"council@example.test","password":"new-correct-horse-battery-staple"}'     "$BASE_URL/auth/login"
)"
if [ "$post_deactivate_login" != "401" ]; then
  echo "Deactivated account login returned $post_deactivate_login instead of 401" >&2
  exit 1
fi

echo "Demo authentication lifecycle checks passed."
