#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:18080}"
OWNER_COOKIE="${2:?Pass owner cookie jar}"

internal_org="$(
  curl -H 'X-StrataAI-Request: 1' --fail --silent     -b "$OWNER_COOKIE"     -H 'Content-Type: application/json'     -d '{"name":"Invitation Internal Test","description":"Internal invite"}'     "$BASE_URL/organizations"
)"
internal_org_id="$(printf '%s' "$internal_org" | jq -r '.organization.id')"

internal_invite="$(
  curl -H 'X-StrataAI-Request: 1' --fail --silent     -b "$OWNER_COOKIE"     -H 'Content-Type: application/json'     -d '{"email":"member@example.test","surface":"INTERNAL","targetRole":"MEMBER"}'     "$BASE_URL/organizations/$internal_org_id/invitations"
)"
internal_token="$(printf '%s' "$internal_invite" | jq -r '.invitationToken')"
test -n "$internal_token"
test "$internal_token" != "null"

MEMBER_COOKIE="$(mktemp)"
PORTAL_COOKIE="$(mktemp)"
trap 'rm -f "$MEMBER_COOKIE" "$PORTAL_COOKIE"' EXIT

curl -H 'X-StrataAI-Request: 1' --fail --silent   -H 'Content-Type: application/json'   -d '{"email":"member@example.test","password":"member-correct-horse-battery","displayName":"Member Test"}'   "$BASE_URL/auth/register" >/dev/null

curl -H 'X-StrataAI-Request: 1' --fail --silent   -c "$MEMBER_COOKIE"   -H 'Content-Type: application/json'   -d '{"email":"member@example.test","password":"member-correct-horse-battery"}'   "$BASE_URL/auth/login" >/dev/null

curl -H 'X-StrataAI-Request: 1' --fail --silent   -X POST   -b "$MEMBER_COOKIE"   "$BASE_URL/invitations/$internal_token/accept" >/tmp/internal-accept.json

member_orgs="$(curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$MEMBER_COOKIE" "$BASE_URL/organizations")"
printf '%s' "$member_orgs" | grep -q "$internal_org_id"

portal_org="$(
  curl -H 'X-StrataAI-Request: 1' --fail --silent     -b "$OWNER_COOKIE"     -H 'Content-Type: application/json'     -d '{"name":"Invitation Portal Test","description":"Portal invite"}'     "$BASE_URL/organizations"
)"
portal_org_id="$(printf '%s' "$portal_org" | jq -r '.organization.id')"

portal_invite="$(
  curl -H 'X-StrataAI-Request: 1' --fail --silent     -b "$OWNER_COOKIE"     -H 'Content-Type: application/json'     -d '{"email":"resident@example.test","surface":"PORTAL","targetRole":"OWNER"}'     "$BASE_URL/organizations/$portal_org_id/invitations"
)"
portal_token="$(printf '%s' "$portal_invite" | jq -r '.invitationToken')"

curl -H 'X-StrataAI-Request: 1' --fail --silent   -H 'Content-Type: application/json'   -d '{"email":"resident@example.test","password":"resident-correct-horse-battery","displayName":"Resident Test"}'   "$BASE_URL/auth/register" >/dev/null

curl -H 'X-StrataAI-Request: 1' --fail --silent   -c "$PORTAL_COOKIE"   -H 'Content-Type: application/json'   -d '{"email":"resident@example.test","password":"resident-correct-horse-battery"}'   "$BASE_URL/auth/login" >/dev/null

portal_accept="$(
  curl -H 'X-StrataAI-Request: 1' --fail --silent     -X POST     -b "$PORTAL_COOKIE"     "$BASE_URL/invitations/$portal_token/accept"
)"
printf '%s' "$portal_accept" | grep -q '"surface":"PORTAL"'

portal_internal_orgs="$(curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$PORTAL_COOKIE" "$BASE_URL/organizations")"
if printf '%s' "$portal_internal_orgs" | grep -q "$portal_org_id"; then
  echo "Portal invitation incorrectly created internal Organization membership." >&2
  exit 1
fi

echo "Invitation and Portal-isolation checks passed."
