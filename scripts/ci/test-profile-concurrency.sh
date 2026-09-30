#!/usr/bin/env bash
set -euo pipefail
# PRD-02-TC-07/08, PRD-24-TC-08: actual HTTP requests against either provider.
BASE_URL="${1:?Pass API URL}"
COOKIE_JAR="${2:?Pass authenticated cookie jar}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Profile concurrency failed at line $LINENO" >&2' ERR

curl --fail --silent --show-error -b "$COOKIE_JAR" "$BASE_URL/me" > "$scratch/before.json"
version="$(jq -r '.version' "$scratch/before.json")"
for invalid in '{}' '{"version":0}' '{"version":-1}'; do
  status="$(curl --silent --show-error -o "$scratch/error.json" -w '%{http_code}' \
    -X PATCH -b "$COOKIE_JAR" -H 'Content-Type: application/json' -d "$invalid" "$BASE_URL/me")"
  test "$status" = "400"
  jq -e '.code == "invalid_version"' "$scratch/error.json" >/dev/null
done

patch() {
  local name="$1"
  curl --silent --show-error -o "$scratch/$name.json" -w '%{http_code}' \
    -X PATCH -b "$COOKIE_JAR" -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg name "$name" --argjson version "$version" '{displayName:$name,version:$version}')" \
    "$BASE_URL/me" > "$scratch/$name.status"
}
patch 'First browser' & first_pid=$!
patch 'Second browser' & second_pid=$!
wait "$first_pid"
wait "$second_pid"
statuses="$(cat "$scratch/First browser.status" "$scratch/Second browser.status")"
test "$statuses" = "200409" || test "$statuses" = "409200"
for name in 'First browser' 'Second browser'; do
  if [ "$(cat "$scratch/$name.status")" = "200" ]; then
    winner="$name"
    jq -e --argjson version "$((version + 1))" '.version == $version' "$scratch/$name.json" >/dev/null
  else
    jq -e '.code == "version_conflict"' "$scratch/$name.json" >/dev/null
  fi
done
curl --fail --silent -b "$COOKIE_JAR" "$BASE_URL/me" > "$scratch/after.json"
jq -e --arg winner "$winner" --argjson version "$((version + 1))" \
  '.displayName == $winner and .version == $version' "$scratch/after.json" >/dev/null
# Retrying a completed save with its old version must never overwrite the winner.
patch 'Stale retry'
test "$(cat "$scratch/Stale retry.status")" = "409"
curl --fail --silent -b "$COOKIE_JAR" "$BASE_URL/me" | jq -e --arg winner "$winner" '.displayName == $winner' >/dev/null
# Reload and intentionally merge, using the new version.
curl --fail --silent --show-error -X PATCH -b "$COOKIE_JAR" -H 'Content-Type: application/json' \
  -d "$(jq -nc --argjson version "$((version + 1))" '{displayName:"Merged profile",version:$version}')" \
  "$BASE_URL/me" | jq -e --argjson version "$((version + 2))" '.displayName == "Merged profile" and .version == $version' >/dev/null
echo 'Profile version, simultaneous update, stale retry and recovery checks passed.'
