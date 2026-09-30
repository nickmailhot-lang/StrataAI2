#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${1:-http://127.0.0.1:18080}"
COOKIE_JAR="${2:?Pass the authenticated cookie jar path}"

create_response="$(
  curl --fail --silent     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d '{"name":"Quail Ridge Test","description":"PRD-03 CI organization"}'     "$BASE_URL/organizations"
)"

organization_id="$(printf '%s' "$create_response" | jq -r '.organization.id')"
version="$(printf '%s' "$create_response" | jq -r '.organization.version')"

test -n "$organization_id"
test "$organization_id" != "null"
test "$version" = "1"

list_response="$(curl --fail --silent -b "$COOKIE_JAR" "$BASE_URL/organizations")"
printf '%s' "$list_response" | grep -q "$organization_id"

update_response="$(
  curl --fail --silent     -X PATCH     -b "$COOKIE_JAR"     -H 'Content-Type: application/json'     -d "$(jq -nc --argjson version "$version"       '{name:"Quail Ridge Updated",description:"Updated by CI",logoUrl:null,version:$version}')"     "$BASE_URL/organizations/$organization_id"
)"

updated_version="$(printf '%s' "$update_response" | jq -r '.version')"
test "$updated_version" = "2"

boards_response="$(
  curl --fail --silent     -b "$COOKIE_JAR"     "$BASE_URL/organizations/$organization_id/boards"
)"
test "$boards_response" = "[]"

leave_status="$(
  curl --silent --output /dev/null --write-out '%{http_code}'     -X POST     -b "$COOKIE_JAR"     "$BASE_URL/organizations/$organization_id/leave"
)"
test "$leave_status" = "409"

delete_status="$(
  curl --silent --output /dev/null --write-out '%{http_code}'     -X DELETE     -b "$COOKIE_JAR"     "$BASE_URL/organizations/$organization_id?version=$updated_version"
)"
test "$delete_status" = "202"

echo "Demo Organization lifecycle checks passed."
