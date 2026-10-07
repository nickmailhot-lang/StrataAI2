#!/usr/bin/env bash
set -euo pipefail

# ARCH-05-TC-01: an unconfirmed namespace, process or Demo readiness must never
# reach the workflow smoke suite; every started fixture is retired on failure.
sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT
script="$(cd "$(dirname "$0")" && pwd)/test-demo-network-isolation.sh"
mkdir "$sandbox/bin"
cat > "$sandbox/bin/docker" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
case "$1" in
  run) [[ " $* " == *' --network none '* ]] || exit 71; echo fixture-container ;;
  inspect)
    case "$*" in
      *HostConfig.NetworkMode*) echo "$FIXTURE_NETWORK" ;;
      *State.Pid*) echo "$FIXTURE_PID" ;;
      *State.Running*) echo true ;;
      *) exit 72 ;;
    esac ;;
  stop) echo stopped >> "$FIXTURE_LOG" ;;
  *) exit 73 ;;
esac
STUB
cat > "$sandbox/bin/sudo" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
[[ " $* " == *' nsenter --target 123 --net -- '* ]] || exit 74
case "$*" in
  *readyz*) printf '{"status":"ready","mode":"%s"}\n' "$FIXTURE_READY_MODE" ;;
  *api/demo/state*|*api/demo/reset*) echo '{}' ;;
  *test-demo-auth.sh*) echo workflows >> "$FIXTURE_LOG" ;;
  *) exit 75 ;;
esac
STUB
printf '#!/usr/bin/env bash\nexit 0\n' > "$sandbox/bin/nsenter"
chmod +x "$sandbox/bin/"*
export PATH="$sandbox/bin:$PATH"
export FIXTURE_LOG="$sandbox/result" FIXTURE_NETWORK=none FIXTURE_PID=123 FIXTURE_READY_MODE=demo
for scenario in success external-network invalid-process non-demo-ready; do
  export FIXTURE_NETWORK=none FIXTURE_PID=123 FIXTURE_READY_MODE=demo
  case "$scenario" in
    external-network) export FIXTURE_NETWORK=bridge ;;
    invalid-process) export FIXTURE_PID=0 ;;
    non-demo-ready) export FIXTURE_READY_MODE=production ;;
  esac
  : > "$FIXTURE_LOG"
  status=0
  bash "$script" 'retained-api:fixture' > "$sandbox/output" 2>&1 || status=$?
  test "$(grep -c '^stopped$' "$FIXTURE_LOG")" = 1
  if [ "$scenario" = success ]; then
    test "$status" = 0
    test "$(grep -c '^workflows$' "$FIXTURE_LOG")" = 1
  else
    test "$status" != 0
    if grep -q '^workflows$' "$FIXTURE_LOG"; then
      echo "Unconfirmed $scenario reached the Demo workflows." >&2; exit 1
    fi
  fi
done
echo 'Demo namespace, process, readiness and failure-cleanup refusal fixtures passed (4 scenarios).'
