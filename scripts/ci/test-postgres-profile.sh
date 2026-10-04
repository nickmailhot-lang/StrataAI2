#!/usr/bin/env bash
set -euo pipefail
BASE_URL="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "PostgreSQL profile check failed at line $LINENO" >&2' ERR
email="profile-${RANDOM}-${RANDOM}@example.test"
curl -H 'X-StrataAI-Request: 1' --fail --silent --show-error -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" '{email:$email,password:"profile-correct-horse-battery",displayName:"Postgres Profile",locale:"en-CA",timezone:"UTC"}')" \
  "$BASE_URL/auth/register" > "$scratch/register.json"
jq -e '.verificationToken == null' "$scratch/register.json" >/dev/null
curl -H 'X-StrataAI-Request: 1' --fail --silent --show-error -c "$scratch/cookies" -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg email "$email" '{email:$email,password:"profile-correct-horse-battery"}')" \
  "$BASE_URL/auth/login" >/dev/null
"$(dirname "$0")/test-csrf.sh" "$BASE_URL" "$scratch/cookies"
"$(dirname "$0")/test-profile-concurrency.sh" "$BASE_URL" "$scratch/cookies"
version="$(curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$scratch/cookies" "$BASE_URL/me" | jq -r '.version')"
curl -H 'X-StrataAI-Request: 1' --fail --silent --show-error -X PATCH -b "$scratch/cookies" -H 'Content-Type: application/json' \
  -d "$(jq -nc --argjson version "$version" '{version:$version,avatarUrl:"https://example.test/avatar.png",locale:"fr-CA",timezone:"America/Vancouver"}')" \
  "$BASE_URL/me" | jq -e '.locale == "fr-CA" and .timezone == "America/Vancouver"' >/dev/null
version=$((version + 1))
curl -H 'X-StrataAI-Request: 1' --fail --silent --show-error -X PATCH -b "$scratch/cookies" -H 'Content-Type: application/json' \
  -d "$(jq -nc --argjson version "$version" '{version:$version,avatarUrl:""}')" \
  "$BASE_URL/me" | jq -e '.avatarUrl == null' >/dev/null
curl -H 'X-StrataAI-Request: 1' --fail --silent -b "$scratch/cookies" "$BASE_URL/me" | jq -e '.avatarUrl == null and .locale == "fr-CA"' >/dev/null
bash "$(dirname "$0")/test-account-mention-handle.sh" "$BASE_URL" "$scratch/cookies"
version="$(curl --fail --silent -b "$scratch/cookies" "$BASE_URL/me" | jq -r '.version')"
curl -H 'X-StrataAI-Request: 1' --fail --silent --show-error -X POST -b "$scratch/cookies" "$BASE_URL/me/deactivate" >/dev/null
status="$(curl -H 'X-StrataAI-Request: 1' --silent --show-error -o /dev/null -w '%{http_code}' -X PATCH -b "$scratch/cookies" \
  -H 'Content-Type: application/json' -d "$(jq -nc --argjson version "$((version + 1))" '{version:$version,displayName:"Denied"}')" "$BASE_URL/me")"
test "$status" = "401"
test "$(curl --silent --show-error -b "$scratch/cookies" -o /dev/null -w '%{http_code}' "$BASE_URL/me/mention-handle")" = 401
echo 'PostgreSQL profile persistence, avatar removal and revoked-session checks passed.'
