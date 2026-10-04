#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Attachment pipeline fixtures require CI.' >&2; exit 1; }
umask 077
scratch=$(mktemp -d /tmp/strata-attachment-release.XXXXXXXX)
export STRATAAI_TEST_ATTACHMENT_DIR="$scratch"
export STRATAAI_TEST_ATTACHMENT_ORG=11111111-1111-1111-1111-111111111111
mkdir "$scratch/objects"
normal=(-f compose.release.yml -f scripts/ci/compose.auth-test.yml)
fixture=("${normal[@]}" -f scripts/ci/compose.attachment-pipeline-test.yml)
scanner_pid=''
cleanup() {
  local status=$?
  docker compose "${normal[@]}" up -d --no-deps --force-recreate --wait --wait-timeout 180 api worker web >/dev/null || status=1
  if [ -n "$scanner_pid" ]; then kill "$scanner_pid" 2>/dev/null || true; wait "$scanner_pid" 2>/dev/null || true; fi
  # Container-created private files require root cleanup, only inside this
  # verified mktemp directory. Never traverse an unverified computed target.
  if [[ "$scratch" == /tmp/strata-attachment-release.* ]] && [ "$(readlink -f "$scratch")" = "$scratch" ]; then sudo rm -rf -- "$scratch"; fi
  exit "$status"
}
trap cleanup EXIT
trap 'echo "Attachment release pipeline failed at line $LINENO" >&2' ERR
python3 scripts/ci/fake-attachment-scanner.py "$scratch/scanner.sock" "$scratch/scans" & scanner_pid=$!
for attempt in $(seq 1 20); do [ ! -S "$scratch/scanner.sock" ] || break; sleep 1; done
test -S "$scratch/scanner.sock"
docker compose "${fixture[@]}" up -d --no-deps --force-recreate --wait --wait-timeout 180 api web >/dev/null
base=http://localhost:8088
json() { curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" "$@"; }
jq -nc --arg email "image-pipeline-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"image-pipeline-correct-horse",displayName:"Image pipeline owner"}' > "$scratch/account"
json -X POST -d "$(cat "$scratch/account")" "$base/auth/register" > "$scratch/registered"
json -c "$scratch/cookies" -X POST -d "$(cat "$scratch/account")" "$base/auth/login" >/dev/null
org=$(json -X POST -d '{"name":"Image pipeline fixture"}' "$base/organizations" | jq -r '.organization.id')
export STRATAAI_TEST_ATTACHMENT_ORG="$org"
[[ "$org" =~ ^[0-9a-f-]{36}$ ]]
board=$(json -X POST -d "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Owned image source",visibility:"PRIVATE"}')" "$base/boards" | jq -r '.id')
list=$(json -X POST -d '{"name":"Source"}' "$base/boards/$board/lists" | jq -r '.id')
card=$(json -X POST -d '{"title":"Source image"}' "$base/lists/$list/cards" | jq -r '.id')
for id in "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
docker compose "${fixture[@]}" up -d --no-deps --force-recreate --wait --wait-timeout 180 worker >/dev/null
printf '%s' 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4z8DwHwAFAAH/iZk9HQAAAABJRU5ErkJggg==' | base64 -d > "$scratch/original"
printf '%s' 'PRIVATE ORIGINAL TRAILING METADATA' >> "$scratch/original"
curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/octet-stream' \
  -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" -H "X-Attachment-Name: $(printf 'Private original.png' | base64 -w0)" \
  -H "X-Attachment-SHA256: $(sha256sum "$scratch/original" | cut -d' ' -f1)" -H "X-Attachment-Size: $(wc -c < "$scratch/original")" \
  -H 'X-Card-Version: 1' --data-binary "@$scratch/original" "$base/cards/$card/attachments" > "$scratch/upload"
attachment=$(jq -r '.attachment.id' "$scratch/upload"); [[ "$attachment" =~ ^[0-9a-f-]{36}$ ]]
jq -e '.cardVersion==2' "$scratch/upload" >/dev/null
for attempt in $(seq 1 90); do
  json "$base/cards/$card/cover/candidates" > "$scratch/candidates"
  if jq -e --arg id "$attachment" '.items|any(.attachmentId==$id and .attachmentVersion==3)' "$scratch/candidates" >/dev/null; then break; fi
  sleep 2
done
jq -e --arg id "$attachment" '.items|any(.attachmentId==$id and .attachmentVersion==3)' "$scratch/candidates" >/dev/null
test "$(cat "$scratch/scans")" -ge 1
jq --arg org "$org" --arg board "$board" --arg card "$card" '{email,password,organizationId:$org,boardId:$board,cardId:$card}' "$scratch/account" > "$scratch/browser-fixture"
STRATAAI_ATTACHMENT_BROWSER_FIXTURE="$scratch/browser-fixture" STRATAAI_E2E_RATE_PACING=1 STRATAAI_E2E_RELEASE_HEADERS=1 \
  npx playwright test --config playwright.attachment.config.ts
revision=$(json "$base/boards/$board" | jq -r '.board.version')
[[ "$revision" =~ ^[1-9][0-9]*$ ]]
selected_version=$((revision + 1))
body=$(jq -nc --arg card "$card" --arg attachment "$attachment" --argjson revision "$revision" '{cardId:$card,attachmentId:$attachment,attachmentVersion:3,boardVersion:$revision}')
selection_key=$(cat /proc/sys/kernel/random/uuid)
competing_key=$(cat /proc/sys/kernel/random/uuid)
race_selection() {
  local key=$1 output=$2
  curl --max-time 60 --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $key" -X POST -d "$body" -o "$scratch/$output.body" -w '%{http_code}' "$base/boards/$board/background/image" > "$scratch/$output.status"
}
# Independent requests contend on the same reviewed Board revision. Exactly
# one ownership selection may commit; the stale command must stay rejected.
race_selection "$selection_key" race-a & race_a=$!
race_selection "$competing_key" race-b & race_b=$!
wait "$race_a"
wait "$race_b"
if [ "$(cat "$scratch/race-a.status")" = 200 ]; then
  winner=race-a; loser=race-b; losing_key=$competing_key
else
  winner=race-b; loser=race-a; losing_key=$selection_key; selection_key=$competing_key
fi
test "$(cat "$scratch/$winner.status")" = 200
test "$(cat "$scratch/$loser.status")" = 409
jq -e '.code=="version_conflict"' "$scratch/$loser.body" >/dev/null
cp "$scratch/$winner.body" "$scratch/selected"
race_selection "$losing_key" race-loser-retry
test "$(cat "$scratch/race-loser-retry.status")" = 409
jq -e '.code=="version_conflict"' "$scratch/race-loser-retry.body" >/dev/null
select_image() {
  curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $selection_key" -X POST -d "$body" "$base/boards/$board/background/image"
}
select_image > "$scratch/race-winner-recovered"
cmp "$scratch/selected" "$scratch/race-winner-recovered"
json "$base/boards/$board" > "$scratch/race-current"
jq -e --slurpfile selected "$scratch/selected" '.board.version==$selected[0].version and .board.backgroundValue==$selected[0].backgroundValue' "$scratch/race-current" >/dev/null
jq -e --argjson version "$selected_version" '.version==$version and .backgroundType=="IMAGE"' "$scratch/selected" >/dev/null
owned=$(jq -r '.backgroundValue' "$scratch/selected"); [[ "$owned" =~ ^[0-9a-f-]{36}$ ]]
test "$owned" != 00000000-0000-0000-0000-000000000000
curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" -D "$scratch/preview.headers" "$base/boards/$board/background/image?boardVersion=$selected_version" > "$scratch/preview"
python3 - "$scratch/preview" "$scratch/preview.headers" <<'PY'
import sys
with open(sys.argv[1], 'rb') as source:
    png = source.read()
assert png.startswith(b'\x89PNG\r\n\x1a\n')
assert png.endswith(bytes.fromhex('0000000049454e44ae426082'))
headers = {}
with open(sys.argv[2], encoding='ascii') as source:
    for line in source:
        if ':' in line:
            name, value = line.split(':', 1)
            headers.setdefault(name.lower(), []).append(value.strip())
assert headers['content-type'] == ['image/png']
assert any('private' in value and 'no-store' in value for value in headers['cache-control'])
assert 'nosniff' in headers['x-content-type-options']
assert 'none' in headers['accept-ranges']
assert any('sandbox' in value for value in headers['content-security-policy'])
assert any('filename=background.png' in value for value in headers['content-disposition'])
assert 'location' not in headers and 'etag' not in headers
PY
! cmp -s "$scratch/original" "$scratch/preview"
! grep -aq 'PRIVATE ORIGINAL' "$scratch/preview"
test "$(curl --max-time 60 --silent --show-error -o /dev/null -w '%{http_code}' "$base/boards/$board/background/image")" = 404
# Real lifecycle command removes the original source from preview admission.
# Board ownership and original acknowledgment recovery remain independent.
json -X POST -d '{"cardVersion":2,"version":3}' "$base/cards/$card/attachments/$attachment/archive" > "$scratch/archived-attachment"
select_image > "$scratch/recovered"
cmp "$scratch/selected" "$scratch/recovered"
curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" "$base/boards/$board/background/image?boardVersion=$selected_version" > "$scratch/retained-preview"
cmp "$scratch/preview" "$scratch/retained-preview"
json -X POST -d "$(jq -nc --argjson version "$selected_version" '{name:"Independent owned image",version:$version}')" "$base/boards/$board/copy" > "$scratch/copied"
copy=$(jq -r '.id' "$scratch/copied"); [[ "$copy" =~ ^[0-9a-f-]{36}$ ]]
jq -e --arg source "$(jq -r '.backgroundValue' "$scratch/selected")" '.version==1 and .visibility=="PRIVATE" and .backgroundType=="IMAGE" and .backgroundValue!=$source' "$scratch/copied" >/dev/null
json -X POST -d "$(jq -nc --argjson version "$selected_version" '{version:$version}')" "$base/boards/$board/archive" >/dev/null
curl --max-time 60 --fail --silent --show-error -b "$scratch/cookies" "$base/boards/$copy/background/image?boardVersion=1" > "$scratch/copied-preview"
cmp "$scratch/preview" "$scratch/copied-preview"
test "$(curl --max-time 60 --silent --show-error -o /dev/null -w '%{http_code}' "$base/boards/$copy/background/image")" = 404
json -X PATCH -d '{"visibility":"PUBLIC","version":1}' "$base/boards/$copy/visibility" >/dev/null
curl --max-time 60 --fail --silent --show-error "$base/boards/$copy/background/image?boardVersion=2" > "$scratch/public-preview"
cmp "$scratch/preview" "$scratch/public-preview"
json -X PATCH -d '{"visibility":"PRIVATE","version":2}' "$base/boards/$copy/visibility" >/dev/null
test "$(curl --max-time 60 --silent --show-error -o /dev/null -w '%{http_code}' "$base/boards/$copy/background/image")" = 404
json -X PATCH -d '{"name":"Independent owned image","version":3,"backgroundType":"COLOR","backgroundValue":null}' "$base/boards/$copy" >/dev/null
test "$(curl --max-time 60 --silent --show-error -b "$scratch/cookies" -o /dev/null -w '%{http_code}' "$base/boards/$copy/background/image")" = 404
echo 'Exact API/Worker images uploaded, scanned via a declared protocol simulator, decoded/published PNG, recovered owned selection after attachment archive, copied independent bytes, and enforced public/private/retired selection. Local private storage; no AWS or real malware-engine claim.'
