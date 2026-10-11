#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable Organization latency fixture requires CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
post() { curl --max-time 30 --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" -d "$2" "$base$1"; }
credentials=$(jq -nc --arg email "organization-latency-$(uuid)@example.test" '{email:$email,password:"Private-latency-fixture-password!",displayName:"Private latency Owner"}')
post /auth/register "$credentials" > "$scratch/account.json"
actor=$(jq -er '.user.id' "$scratch/account.json")
[[ "$actor" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
# Fresh disposable account only; provider verification has separate acceptance.
admin "UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE id='$actor';" >/dev/null
curl --max-time 30 --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null
org=$(post /organizations '{"name":"Private latency Organization"}' | jq -er '.organization.id')
board=$(post /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Private intake Board",visibility:"ORGANIZATION"}')" | jq -er '.id')
listing=$(post "/boards/$board/lists" '{"name":"Private intake List"}' | jq -er '.id')
python3 scripts/ci/measure-organization-latency.py --base "$base" --cookies "$scratch/cookies" --organization "$org" --actor "$actor" --board "$board" --listing "$listing" --revision "$GITHUB_SHA" --output artifacts/capacity/organization-latency.json
echo 'Organization configuration/lifecycle authorized HTTP p95 target passed; fixed aggregate measurements retained.'
