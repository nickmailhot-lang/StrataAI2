#!/usr/bin/env sh
set -eu

WEB_URL="${STRATAAI_WEB_URL:-http://127.0.0.1:8088}"
API_URL="${STRATAAI_API_URL:-http://127.0.0.1:8080}"
WORKER_URL="${STRATAAI_WORKER_URL:-http://127.0.0.1:8081}"
REQUEST_TIMEOUT="${STRATAAI_HEALTH_TIMEOUT_SECONDS:-30}"
case "$REQUEST_TIMEOUT" in
  [1-9]|[1-9][0-9]|[12][0-9][0-9]|300) ;;
  *) echo "Health timeout must be an integer from 1 to 300 seconds." >&2; exit 1 ;;
esac

check() {
  status=$(curl --fail --silent --show-error --connect-timeout 5 \
    --max-time "$REQUEST_TIMEOUT" --output /dev/null --write-out '%{http_code}' "$1")
  if [ "$status" != 200 ]; then
    echo "$2 check did not return HTTP 200." >&2
    exit 1
  fi
}

check "$API_URL/api/health" "API health"
check "$WORKER_URL/healthz" "Worker health"
check "$API_URL/readyz" "API readiness"
check "$WORKER_URL/readyz" "Worker readiness"
check "$WEB_URL/" "Web health"
check "$WEB_URL/api/health" "Web-to-API proxy health"

echo "StrataAI2 web, API, Worker, and web-to-API proxy health checks passed."
