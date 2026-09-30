#!/usr/bin/env sh
set -eu

WEB_URL="${STRATAAI_WEB_URL:-http://127.0.0.1:8088}"
API_URL="${STRATAAI_API_URL:-http://127.0.0.1:8080}"
WORKER_URL="${STRATAAI_WORKER_URL:-http://127.0.0.1:8081}"

curl --fail --silent --show-error "$API_URL/api/health" >/dev/null
curl --fail --silent --show-error "$WORKER_URL/healthz" >/dev/null
curl --fail --silent --show-error "$WEB_URL/" >/dev/null
curl --fail --silent --show-error "$WEB_URL/api/health" >/dev/null

echo "StrataAI2 web, API, Worker, and web-to-API proxy health checks passed."
