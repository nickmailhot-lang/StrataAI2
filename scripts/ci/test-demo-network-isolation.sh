#!/usr/bin/env bash
set -euo pipefail

# ARCH-05-AC-001/004: run the documented Demo workflows inside an API
# namespace with only loopback. The Linux CI runner and its Docker daemon must
# share a host; nsenter is test tooling, not an application-image dependency.
image="${1:?Pass the exact retained API image}"
name="strataai-demo-isolated-$$"
cleanup() { docker stop "$name" >/dev/null 2>&1 || true; }
trap cleanup EXIT
command -v nsenter >/dev/null

docker run -d --rm --name "$name" --network none \
  -e STRATAAI_RUNTIME_MODE=demo -e ASPNETCORE_ENVIRONMENT=Development \
  -e Logging__LogLevel__Default=Warning "$image" >/dev/null
test "$(docker inspect --format '{{.HostConfig.NetworkMode}}' "$name")" = none
pid="$(docker inspect --format '{{.State.Pid}}' "$name")"
[[ "$pid" =~ ^[1-9][0-9]*$ ]]
base='http://127.0.0.1:8080'
for i in $(seq 1 30); do
  if sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent "$base/readyz" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent "$base/readyz" \
  | jq -e '.status=="ready" and .mode=="demo"' >/dev/null
sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent "$base/api/demo/state" >/dev/null
sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent \
  -H 'X-StrataAI-Request: 1' -X POST "$base/api/demo/reset" >/dev/null
sudo nsenter --target "$pid" --net -- bash "$(dirname "$0")/test-demo-auth.sh" "$base"
test "$(docker inspect --format '{{.State.Running}}' "$name")" = true
echo 'Demo documented workflows passed with network=none and no PostgreSQL or provider credentials.'
