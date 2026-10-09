#!/usr/bin/env bash
set -euo pipefail
# PRD-14 / ARCH-11: real engine with one deterministic harmless test signature.
# No freshclam/network, official signature coverage or production detection claim.
worker="${STRATAAI_WORKER_IMAGE:?Set the exact already-built Worker image}"
scanner='clamav/clamav@sha256:7769870154c74ce31b0047dd8771e81f7c4269278bc005782e9e419e4922c73d'
name="strataai-real-scanner-$(openssl rand -hex 8)"
volume="$name-private"
scratch=$(mktemp -d)
cleanup() {
  docker rm -f "$name" >/dev/null 2>&1 || true
  docker volume rm "$volume" >/dev/null 2>&1 || true
  rm -f "$scratch/result"
  rmdir "$scratch"
}
trap cleanup EXIT
docker volume create "$volume" >/dev/null
docker run --rm -i --network none --read-only --user 0:0 --cap-drop ALL \
  --security-opt no-new-privileges -v "$volume:/fixture" --entrypoint sh "$scanner" -s <<'SETUP'
set -eu
mkdir -p /fixture/database
sample='StrataAI real scanner rejection acceptance sample.'
hash=$(printf '%s' "$sample" | md5sum | cut -d ' ' -f 1)
size=$(printf '%s' "$sample" | wc -c | tr -d ' ')
printf '%s:%s:StrataAI.Acceptance.Harmless\n' "$hash" "$size" > /fixture/database/acceptance.hdb
cat > /fixture/clamd.conf <<'CONFIG'
DatabaseDirectory /fixture/database
LocalSocket /fixture/scanner.sock
LocalSocketMode 600
Foreground yes
LogTime no
MaxThreads 2
MaxQueue 4
StreamMaxLength 1M
CONFIG
SETUP
docker run -d --network none --read-only --user 0:0 --cap-drop ALL \
  --security-opt no-new-privileges --memory 256m --memory-swap 256m --cpus 1 --pids-limit 32 \
  --tmpfs /tmp:rw,noexec,nosuid,size=16m -v "$volume:/fixture" --name "$name" \
  --entrypoint clamd "$scanner" --config-file=/fixture/clamd.conf > /dev/null
for attempt in $(seq 1 30); do
  if docker exec "$name" test -S /fixture/scanner.sock; then break; fi
  sleep 1
done
docker exec "$name" test -S /fixture/scanner.sock
if ! timeout --signal=TERM --kill-after=5s 30s docker run --rm \
  --network none --read-only --user 0:0 --cap-drop ALL --security-opt no-new-privileges \
  --memory 256m --memory-swap 256m --cpus 1 --pids-limit 64 \
  --tmpfs /tmp:rw,noexec,nosuid,size=16m -v "$volume:/fixture:ro" \
  --env DOTNET_EnableDiagnostics=0 "$worker" --verify-attachment-scanner-runtime > "$scratch/result" 2>&1; then
  echo 'Real scanner Worker verification failed.'
  exit 1
fi
grep -Fxq 'Worker real scanner transport verified: clean, test-signature detection, empty refusal, cancellation and recovery.' "$scratch/result"
echo 'Exact Worker passes real ClamAV transport with a harmless deterministic test signature.'
