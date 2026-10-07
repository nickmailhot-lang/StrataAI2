#!/usr/bin/env bash
set -euo pipefail

# ARCH-05-TC-01: no success when Docker or the diagnostic endpoints fail to
# confirm the separate Demo Worker. Started fixtures are always retired.
sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT
script="$(cd "$(dirname "$0")" && pwd)/test-demo-worker-network-isolation.sh"
mkdir "$sandbox/bin"
cat > "$sandbox/bin/docker" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  run)
    [[ " $* " == *' --network none '* ]] || exit 71
    [[ " $* " != *' -p '* && " $* " != *' --publish '* ]] || exit 72
    echo fixture-worker ;;
  inspect)
    case "$*" in
      *HostConfig.NetworkMode*) echo "$FIXTURE_NETWORK" ;;
      *HostConfig.PortBindings*) echo "$FIXTURE_PORTS" ;;
      *State.Running*) echo "$FIXTURE_RUNNING" ;;
      *) exit 73 ;;
    esac ;;
  exec)
    case "$*" in
      *readyz*) printf '{"status":"ready","mode":"%s"}\n' "$FIXTURE_MODE" ;;
      *healthz*) printf '{"status":"ok","service":"%s","timestamp":"2026-10-07T00:00:00Z"}\n' "$FIXTURE_SERVICE" ;;
      *runtime*)
        echo diagnostics >> "$FIXTURE_LOG"
        if [ "$FIXTURE_RUNTIME" = failure ]; then exit 22; fi
        printf '{"service":"strataai-worker","mode":"%s","revision":"fixture","version":"1.0"}\n' "$FIXTURE_RUNTIME" ;;
      *) exit 74 ;;
    esac ;;
  stop) echo stopped >> "$FIXTURE_LOG" ;;
  *) exit 75 ;;
esac
STUB
chmod +x "$sandbox/bin/docker"
export PATH="$sandbox/bin:$PATH"
export FIXTURE_LOG="$sandbox/result"
for scenario in success external-network published-port stopped non-demo-ready wrong-service non-demo-runtime diagnostic-failure; do
  export FIXTURE_NETWORK=none FIXTURE_PORTS='{}' FIXTURE_RUNNING=true
  export FIXTURE_MODE=demo FIXTURE_SERVICE=strataai-worker FIXTURE_RUNTIME=demo
  case "$scenario" in
    external-network) export FIXTURE_NETWORK=bridge ;;
    published-port) export FIXTURE_PORTS='{"8081/tcp":[{"HostPort":"8081"}]}' ;;
    stopped) export FIXTURE_RUNNING=false ;;
    non-demo-ready) export FIXTURE_MODE=production ;;
    wrong-service) export FIXTURE_SERVICE=strataai-api ;;
    non-demo-runtime) export FIXTURE_RUNTIME=production ;;
    diagnostic-failure) export FIXTURE_RUNTIME=failure ;;
  esac
  : > "$FIXTURE_LOG"
  status=0
  bash "$script" 'retained-worker:fixture' > "$sandbox/output" 2>&1 || status=$?
  test "$(grep -c '^stopped$' "$FIXTURE_LOG")" = 1
  if [ "$scenario" = success ]; then
    test "$status" = 0
    grep -q '^Separate Demo Worker passed' "$sandbox/output"
  else
    test "$status" != 0
    if grep -q '^Separate Demo Worker passed' "$sandbox/output"; then
      echo "Unconfirmed $scenario reported Demo Worker success." >&2; exit 1
    fi
  fi
done
echo 'Separate Demo Worker isolation and failure-cleanup refusal fixtures passed (8 scenarios).'
