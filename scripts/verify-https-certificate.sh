#!/usr/bin/env bash
set -euo pipefail
# Read-only operator check. No renewal, deployment or scheduler is installed.
fail() { echo 'HTTPS certificate verification failed.' >&2; exit 1; }
if [ "$#" -lt 3 ] || [ "$#" -gt 4 ]; then
  echo 'Usage: verify-https-certificate.sh HOST PORT MIN_REMAINING_SECONDS [TRUSTED_CA_PEM]' >&2; exit 1
fi
host=$1; port=$2; margin=$3; budget=${STRATAAI_TLS_TIMEOUT_SECONDS-10}
[[ "$port" =~ ^[1-9][0-9]{0,4}$ ]] && ((port <= 65535)) || fail
[[ "$margin" =~ ^[1-9][0-9]{0,7}$ ]] && ((margin <= 31536000)) || fail
[[ "$budget" =~ ^[1-9][0-9]?$ ]] && ((budget <= 60)) || fail
if [[ "$host" == \[*\] ]]; then host=${host:1:-1}; fi
host=${host%.}
[[ -n "$host" && ${#host} -le 253 ]] || fail
sni=()
if [[ "$host" == *:* ]]; then
  [[ "$host" =~ ^[0-9a-fA-F:.]+$ ]] || fail
  connect="[$host]:$port"; identity=(-verify_ip "$host")
elif [[ "$host" =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  connect="$host:$port"; identity=(-verify_ip "$host")
else
  IFS='.' read -ra labels <<< "$host"
  for label in "${labels[@]}"; do
    [[ "$label" =~ ^[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?$ ]] || fail
  done
  connect="$host:$port"; identity=(-verify_hostname "$host"); sni=(-servername "$host")
fi
for dependency in openssl timeout mktemp head wc awk date rm rmdir; do
  command -v "$dependency" >/dev/null || fail
done
host_path() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s\n' "$1"; fi; }
ca=()
if [ "$#" = 4 ]; then
  test -f "$4" || fail
  ca=(-CAfile "$(host_path "$4")")
fi
umask 077
scratch=$(mktemp -d)
cleanup() { rm -f -- "$scratch/peer" "$scratch/chain.pem" "$scratch/leaf.pem"; rmdir -- "$scratch"; }
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
# Verification errors must terminate s_client; its diagnostic default otherwise
# accepts untrusted peers. Bound both execution and stored untrusted output.
if ! timeout --kill-after=1 "$budget" openssl s_client -connect "$connect" "${sni[@]}" \
  -verify_return_error "${identity[@]}" "${ca[@]}" -showcerts -no_ign_eof </dev/null \
  2>&1 | head -c 65537 > "$scratch/peer"; then fail; fi
test "$(wc -c < "$scratch/peer")" -le 65536 || fail
awk '/^-----BEGIN CERTIFICATE-----$/{inside=1} inside{print} /^-----END CERTIFICATE-----$/{inside=0}' \
  "$scratch/peer" > "$scratch/chain.pem"
awk '/^-----BEGIN CERTIFICATE-----$/{inside=1} inside{print} /^-----END CERTIFICATE-----$/{exit}' \
  "$scratch/peer" > "$scratch/leaf.pem"
now=$(date +%s); [[ "$now" =~ ^[1-9][0-9]{0,11}$ ]] || fail
future=$((now + margin))
# Check the same captured peer, including its usable trust path, at the chosen
# horizon. A long-lived leaf cannot conceal a soon-expiring intermediate/root.
timeout --kill-after=1 "$budget" openssl x509 -in "$(host_path "$scratch/leaf.pem")" \
  -noout -checkend "$margin" >/dev/null 2>&1 || fail
timeout --kill-after=1 "$budget" openssl verify -purpose sslserver -attime "$future" \
  "${identity[@]}" "${ca[@]}" -untrusted "$(host_path "$scratch/chain.pem")" \
  "$(host_path "$scratch/leaf.pem")" >/dev/null 2>&1 || fail
cleanup || fail
trap - EXIT
echo 'HTTPS certificate identity, trust path and configured remaining lifetime verified.'
