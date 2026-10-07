#!/usr/bin/env bash
set -euo pipefail

# ARCH-05-AC-001/004: the separate Demo Worker must start without a database,
# provider credentials, published ports or external network. curl is already
# part of the retained Worker image; this gate neither installs nor rebuilds it.
image="${1:?Pass the exact retained Worker image}"
name="strataai-demo-worker-isolated-$$"
cleanup() { docker stop "$name" >/dev/null 2>&1 || true; }
trap cleanup EXIT

docker run -d --rm --name "$name" --network none \
  -e STRATAAI_RUNTIME_MODE=demo -e ASPNETCORE_ENVIRONMENT=Development \
  -e Logging__LogLevel__Default=Warning "$image" >/dev/null
test "$(docker inspect --format '{{.HostConfig.NetworkMode}}' "$name")" = none
test "$(docker inspect --format '{{json .HostConfig.PortBindings}}' "$name")" = '{}'
base='http://127.0.0.1:8081'
for i in $(seq 1 30); do
  test "$(docker inspect --format '{{.State.Running}}' "$name")" = true
  if docker exec "$name" curl --noproxy '*' --max-time 3 --fail --silent "$base/readyz" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
docker exec "$name" curl --noproxy '*' --max-time 3 --fail --silent "$base/readyz" \
  | jq -e '.status=="ready" and .mode=="demo"' >/dev/null
docker exec "$name" curl --noproxy '*' --max-time 3 --fail --silent "$base/healthz" \
  | jq -e '.status=="ok" and .service=="strataai-worker" and (.timestamp | type)=="string"' >/dev/null
docker exec "$name" curl --noproxy '*' --max-time 3 --fail --silent "$base/runtime" \
  | jq -e '.service=="strataai-worker" and .mode=="demo" and (.revision | type)=="string" and (.version | type)=="string"' >/dev/null
test "$(docker inspect --format '{{.State.Running}}' "$name")" = true
echo 'Separate Demo Worker passed health, readiness and runtime diagnostics with network=none, no published ports or production credentials.'
