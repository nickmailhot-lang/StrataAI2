#!/usr/bin/env bash
set -euo pipefail
# Actual cookie/HTTP/restricted PostgreSQL command against the immutable API.
test "${CI:-}" = true || { echo 'Account handle release fixtures may run only in CI.' >&2; exit 1; }
BASE_URL="${1:?Pass API URL}"
COOKIE_JAR="${2:?Pass authenticated cookie jar}"
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Account handle release check failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
curl --fail --silent --show-error -b "$COOKIE_JAR" -D "$scratch/read.headers" "$BASE_URL/me/mention-handle" > "$scratch/initial.json"
grep -qi 'cache-control:.*no-store' "$scratch/read.headers"
user="$(jq -r '.userId' "$scratch/initial.json")"
[[ "$user" =~ ^[0-9a-f-]{36}$ ]]
user_version="$(jq -r '.userVersion' "$scratch/initial.json")"
handle_version="$(jq -r '.handleVersion' "$scratch/initial.json")"
audit_before="$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='USER_PROFILE_UPDATED';")"
key="$(cat /proc/sys/kernel/random/uuid)"
name="http_${user//-/}"
body="$(jq -nc --arg handle "  ${name^^}  " --argjson userVersion "$user_version" --argjson handleVersion "$handle_version" '{handle:$handle,userVersion:$userVersion,handleVersion:$handleVersion}')"
request() {
  local suffix="$1" intent="$2" retry="$3"
  curl --max-time 60 --silent --show-error -b "$COOKIE_JAR" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $retry" -X PATCH -d "$intent" -D "$scratch/$suffix.headers" -o "$scratch/$suffix.json" -w '%{http_code}' \
    "$BASE_URL/me/mention-handle" > "$scratch/$suffix.status"
}
request first "$body" "$key"
test "$(cat "$scratch/first.status")" = 200
grep -qi 'cache-control:.*no-store' "$scratch/first.headers"
jq -e --arg user "$user" --arg name "$name" --argjson uv "$((user_version+1))" --argjson hv "$((handle_version+1))" \
  '.userId==$user and .handle==$name and .userVersion==$uv and .handleVersion==$hv and .changed==true' "$scratch/first.json" >/dev/null
request retry "$body" "$key"
test "$(cat "$scratch/retry.status")" = 200
diff <(jq -S . "$scratch/first.json") <(jq -S . "$scratch/retry.json")
request changed "$(jq --arg handle different '.handle=$handle' <<< "$body")" "$key"
test "$(cat "$scratch/changed.status")" = 409
jq -e '.code=="idempotency_key_reused"' "$scratch/changed.json" >/dev/null
request stale "$body" "$(cat /proc/sys/kernel/random/uuid)"
test "$(cat "$scratch/stale.status")" = 409
jq -e '.code=="version_conflict"' "$scratch/stale.json" >/dev/null
current_body="$(jq -nc --arg handle "$name" --argjson userVersion "$((user_version+1))" --argjson handleVersion "$((handle_version+1))" '{handle:$handle,userVersion:$userVersion,handleVersion:$handleVersion}')"
request noop "$current_body" "$(cat /proc/sys/kernel/random/uuid)"
test "$(cat "$scratch/noop.status")" = 200
jq -e '.changed==false' "$scratch/noop.json" >/dev/null
test "$(admin "SELECT count(*) FROM audit_events WHERE actor_id='$user' AND event_type='USER_PROFILE_UPDATED';")" = "$((audit_before+1))"
test "$(admin "SELECT count(*) FROM identity_handle_claim_replays WHERE user_id='$user' AND key_id='$key' AND user_version=$((user_version+1)) AND handle_version=$((handle_version+1)) AND changed;")" = 1
test "$(admin "SELECT count(*) FROM identity_events WHERE user_id='$user' AND entity_version=$((user_version+1)) AND event_type='USER_PROFILE_UPDATED' AND metadata='{}'::jsonb;")" = 1
# A separate profile edit changes the parent version but keeps this child.
curl --fail --silent --show-error -X PATCH -b "$COOKIE_JAR" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(jq -nc --argjson version "$((user_version+1))" '{displayName:"Handle peer edit",version:$version}')" "$BASE_URL/me" >/dev/null
request peer_recovery "$body" "$key"
test "$(cat "$scratch/peer_recovery.status")" = 200
diff <(jq -S . "$scratch/first.json") <(jq -S . "$scratch/peer_recovery.json")
renamed="next_${user//-/}"
request rename "$(jq -nc --arg handle "$renamed" --argjson userVersion "$((user_version+2))" --argjson handleVersion "$((handle_version+1))" '{handle:$handle,userVersion:$userVersion,handleVersion:$handleVersion}')" "$(cat /proc/sys/kernel/random/uuid)"
test "$(cat "$scratch/rename.status")" = 200
request former "$body" "$key"
test "$(cat "$scratch/former.status")" = 409
jq -e '.code=="mention_handle_unavailable" and (has("handle")|not)' "$scratch/former.json" >/dev/null
echo 'Exact-image account handle HTTP: private current setting, atomic revisions/events/receipt, original recovery, no-op, collisions and current-only hydration passed.'
