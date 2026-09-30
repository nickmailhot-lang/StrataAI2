#!/usr/bin/env bash
set -euo pipefail
# ARCH-01-AC-002 / ARCH-11: compare actual release assets, not runtime labels.
revision="${STRATAAI_BUILD_REVISION:?Set the expected CI commit}"
version="${STRATAAI_BUILD_VERSION:?Set the expected CI build version}"
check_identity() {
  curl --fail --silent --show-error "$1" | jq -e --arg revision "$revision" --arg version "$version" \
    '.revision == $revision and .version == $version' >/dev/null
}
check_identity http://127.0.0.1:8088/build-metadata.json
check_identity http://127.0.0.1:8080/api/runtime
check_identity http://127.0.0.1:8081/runtime
for image in "$STRATAAI_WEB_IMAGE" "$STRATAAI_API_IMAGE" "$STRATAAI_WORKER_IMAGE"; do
  test "$(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.revision"}}' "$image")" = "$revision"
  test "$(docker inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' "$image")" = "$version"
done

containers=()
cleanup() { if [ "${#containers[@]}" -gt 0 ]; then docker rm -f "${containers[@]}" >/dev/null 2>&1 || true; fi; }
trap cleanup EXIT
for host in api worker; do
  if [ "$host" = api ]; then image="$STRATAAI_API_IMAGE"; port=18082; container_port=8080; path=/api/runtime
  else image="$STRATAAI_WORKER_IMAGE"; port=18083; container_port=8081; path=/runtime; fi
  id="$(docker run -d --rm -e STRATAAI_RUNTIME_MODE=demo \
    -e STRATAAI_BUILD_REVISION=runtime-spoof -e STRATAAI_BUILD_VERSION=runtime-spoof \
    -p "127.0.0.1:$port:$container_port" "$image")"
  containers+=("$id")
  for attempt in $(seq 1 30); do
    if curl --fail --silent "http://127.0.0.1:$port/healthz" >/dev/null; then break; fi
    sleep 1
  done
  check_identity "http://127.0.0.1:$port$path"
done
echo 'Web/API/Worker embedded identity and OCI labels match; runtime overrides cannot relabel hosts.'
