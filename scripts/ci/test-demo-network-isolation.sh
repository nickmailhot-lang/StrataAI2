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
# Verify the versioned packaged catalog, actual clear/read and deterministic
# reset before authentication changes any independent Demo identity state.
sample="$(sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent "$base/api/demo/state")"
printf '%s' "$sample" | jq -e '.sampleVersion==1 and (.organizations | length)==1
  and .organizations[0].id=="11111111-1111-1111-1111-111111111111"
  and .organizations[0].name=="Quail Ridge Demo"
  and (.organizations[0].boards | length)==1
  and .organizations[0].boards[0].id=="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
  and .organizations[0].boards[0].name=="Council Operations"' >/dev/null
sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent \
  -H 'X-StrataAI-Request: 1' -X DELETE "$base/api/demo/state" \
  | jq -e '.sampleVersion==1 and .organizations==[]' >/dev/null
sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent "$base/api/demo/state" \
  | jq -e '.sampleVersion==1 and .organizations==[]' >/dev/null
restored="$(sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent \
  -H 'X-StrataAI-Request: 1' -X POST "$base/api/demo/reset")"
test "$(printf '%s' "$restored" | jq -Sc .)" = "$(printf '%s' "$sample" | jq -Sc .)"
read_back="$(sudo nsenter --target "$pid" --net -- curl --noproxy '*' --fail --silent "$base/api/demo/state")"
test "$(printf '%s' "$read_back" | jq -Sc .)" = "$(printf '%s' "$sample" | jq -Sc .)"
sudo nsenter --target "$pid" --net -- bash "$(dirname "$0")/test-demo-auth.sh" "$base"
test "$(docker inspect --format '{{.State.Running}}' "$name")" = true
echo 'Demo documented workflows passed with network=none and no PostgreSQL or provider credentials.'
