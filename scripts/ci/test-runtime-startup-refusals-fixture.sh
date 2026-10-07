#!/usr/bin/env bash
set -euo pipefail

# ARCH-09 / ARCH-12: the image refusal runner must not accept successful startup,
# an unrelated crash or a timed-out startup as proof of the intended refusal.
sandbox="$(mktemp -d)"
trap 'rm -rf "$sandbox"' EXIT
script="$(cd "$(dirname "$0")" && pwd)/test-runtime-startup-refusals.sh"
mkdir "$sandbox/bin"
cat > "$sandbox/bin/docker" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
if [ "$1" = stop ]; then echo stopped >> "$FIXTURE_LOG"; exit 0; fi
test "$1" = run
[[ " $* " == *' --network none '* ]]
echo run >> "$FIXTURE_LOG"
case "$FIXTURE_SCENARIO" in
  successful-startup) exit 0 ;;
  unrelated-crash) echo 'Unrelated startup failure'; exit 139 ;;
  timeout) exit 124 ;;
  disclosed-value) echo 'STRATAAI_RUNTIME_MODE is required invalid-policy-value'; exit 139 ;;
esac
case " $* " in
  *STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL=*) echo 'STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL must be true or false when supplied.' ;;
  *STRATAAI_AUTH_SESSION_HOURS=*) echo 'STRATAAI_AUTH_SESSION_HOURS must be an integer between 1 and 720 when supplied.' ;;
  *STRATAAI_AUTH_MIN_PASSWORD_LENGTH=*) echo 'STRATAAI_AUTH_MIN_PASSWORD_LENGTH must be an integer between 8 and 128 when supplied.' ;;
  *STRATAAI_RUNTIME_MODE=hybrid*) echo "STRATAAI_RUNTIME_MODE must be either 'demo' or 'production'." ;;
  *STRATAAI_RUNTIME_MODE=production*) echo 'Production mode requires a database connection string or complete runtime credentials.' ;;
  *) echo "STRATAAI_RUNTIME_MODE is required and must be 'demo' or 'production'." ;;
esac
exit 139
STUB
chmod +x "$sandbox/bin/docker"
export PATH="$sandbox/bin:$PATH" FIXTURE_LOG="$sandbox/result"
for scenario in intended-refusals successful-startup unrelated-crash timeout disclosed-value; do
  export FIXTURE_SCENARIO="$scenario"
  : > "$FIXTURE_LOG"
  status=0
  bash "$script" retained-api:fixture retained-worker:fixture > "$sandbox/output" 2>&1 || status=$?
  grep -q '^stopped$' "$FIXTURE_LOG"
  if [ "$scenario" = intended-refusals ]; then
    test "$status" = 0
    test "$(grep -c '^run$' "$FIXTURE_LOG")" = 12
  else
    test "$status" != 0
    test "$(grep -c '^run$' "$FIXTURE_LOG")" = 1
  fi
done
echo 'Runtime startup refusal runner rejects success, unrelated crashes, timeouts and disclosed input (5 fixture scenarios).'
