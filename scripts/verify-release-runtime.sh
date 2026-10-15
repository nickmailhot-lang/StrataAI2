#!/usr/bin/env bash
set -euo pipefail
# ARCH-12-FR-009: read-only post-deployment verification of the selected bundle.
if [ "$#" != 1 ]; then echo 'Usage: verify-release-runtime.sh build-metadata.json' >&2; exit 1; fi
for dependency in curl jq mktemp head wc cat rm rmdir; do
  command -v "$dependency" >/dev/null || { echo 'Release verification tools are unavailable.' >&2; exit 1; }
done
budget=${STRATAAI_HEALTH_TIMEOUT_SECONDS:-30}
case "$budget" in
  [1-9]|[1-9][0-9]|[12][0-9][0-9]|300) ;;
  *) echo 'Health timeout must be an integer from 1 to 300 seconds.' >&2; exit 1 ;;
esac
if [ "$budget" -gt 15 ]; then budget=15; fi
umask 077
scratch=$(mktemp -d)
cleanup() { rm -f -- "$scratch/expected" "$scratch/response" "$scratch/status"; rmdir -- "$scratch"; }
trap cleanup EXIT
fail() { echo 'Release runtime verification failed; retain the prior known-good release.' >&2; exit 1; }
if ! head -c 4097 -- "$1" > "$scratch/expected" 2>/dev/null; then fail; fi
test "$(wc -c < "$scratch/expected")" -le 4096 || fail
jq -se 'length==1 and (.[0] | type=="object" and
  (.commitSha | type=="string" and test("^([a-f0-9]{40}|[a-f0-9]{64})$")) and
  (.version | type=="string" and test("^[0-9A-Za-z][0-9A-Za-z.+-]{0,127}$")))' \
  "$scratch/expected" >/dev/null 2>&1 || fail
revision=$(jq -r '.commitSha' "$scratch/expected")
version=$(jq -r '.version' "$scratch/expected")
script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)
bash "$script_dir/health-check.sh" >/dev/null || fail
verify() {
  # Cap stored bytes even with older curl and a chunked response of unknown size.
  # pipefail preserves a late curl failure; status is separate from JSON bytes.
  if ! curl --disable --silent --globoff --proto '=http,https' --noproxy '*' \
    --connect-timeout 5 --max-time "$budget" --write-out '%{stderr}%{http_code}' "$1" \
    2> "$scratch/status" | head -c 4097 > "$scratch/response"; then fail; fi
  test "$(cat "$scratch/status")" = 200 || fail
  test "$(wc -c < "$scratch/response")" -le 4096 || fail
  jq -se --arg revision "$revision" --arg version "$version" --arg service "${2:-}" \
    'length==1 and (.[0] | type=="object" and .revision==$revision and .version==$version
      and (if $service=="" then true else .service==$service and .mode=="production" end))' \
    "$scratch/response" >/dev/null 2>&1 || fail
}
verify "${STRATAAI_WEB_URL:-http://127.0.0.1:8088}/build-metadata.json"
verify "${STRATAAI_API_URL:-http://127.0.0.1:8080}/api/runtime" strataai-api
verify "${STRATAAI_WORKER_URL:-http://127.0.0.1:8081}/runtime" strataai-worker
echo 'Selected release health, readiness, proxy and web/API/Worker build identity verified.'
