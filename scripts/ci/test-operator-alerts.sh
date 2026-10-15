#!/usr/bin/env bash
set -euo pipefail
umask 077
# Pinned development verifier, never part of the app images or operator payload.
version=3.15.0
digest=2a542df32eac02ee17b9d844fb2aa1de00dafa5476579ba8a3ba862e9d572ea0
temp_root=$(cd "${RUNNER_TEMP:-${TMPDIR:-/tmp}}" && pwd -P)
scratch=$(mktemp -d "$temp_root/strataai-alert-rules.XXXXXXXX")
scratch=$(cd "$scratch" && pwd -P)
[[ "$(dirname "$scratch")" == "$temp_root" && "$(basename "$scratch")" == strataai-alert-rules.* ]]
trap 'rm -rf -- "$scratch"' EXIT
curl --disable --fail --silent --show-error --location --proto '=https' --proto-redir '=https' \
  --connect-timeout 10 --max-time 180 \
  "https://github.com/prometheus/prometheus/releases/download/v$version/prometheus-$version.linux-amd64.tar.gz" \
  --output "$scratch/prometheus.tar.gz"
printf '%s  %s\n' "$digest" "$scratch/prometheus.tar.gz" | sha256sum --check --status
tar -xzf "$scratch/prometheus.tar.gz" -C "$scratch" --no-same-owner --no-same-permissions \
  --strip-components=1 "prometheus-$version.linux-amd64/promtool"
"$scratch/promtool" check rules deploy/metrics/alerts.yml
"$scratch/promtool" check config deploy/metrics/prometheus.example.yml
"$scratch/promtool" test rules tests/operator-alerts.test.yml
