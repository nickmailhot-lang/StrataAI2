#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable lifecycle history fixtures require CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"
scratch=$(mktemp -d)
worker_active=false
restore_worker() {
  docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
  worker_active=false
}
cleanup() {
  local result=$?
  if $worker_active; then restore_worker || result=1; fi
  rm -rf "$scratch"
  exit "$result"
}
trap cleanup EXIT
trap 'echo "Lifecycle historical preservation failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
request() {
  curl --max-time 30 --silent --show-error -b "$scratch/cookies" -X "$1" \
    -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuid)" \
    -d "$3" -o "$scratch/response.json" -w '%{http_code}' "$base$2"
}
get() { curl --max-time 30 --silent --show-error -b "$scratch/cookies" -o "$scratch/response.json" -w '%{http_code}' "$base$1"; }
credentials=$(jq -nc --arg email "lifecycle-history-$(uuid)@example.test" '{email:$email,password:"Lifecycle-history-correct-horse!",displayName:"Lifecycle history fixture"}')
test "$(request POST /auth/register "$credentials")" = 201
actor=$(jq -r '.user.id' "$scratch/response.json")
[[ "$actor" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
admin "UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE id='$actor';" >/dev/null
curl --max-time 30 --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null
retained() {
  # Publication readiness can advance independently; compare the retained
  # graph/revisions/receipts and original audits, excluding new lifecycle facts.
  admin "SELECT md5(jsonb_build_object(
    'boards',(SELECT jsonb_agg(to_jsonb(b) ORDER BY id) FROM boards b WHERE tenant_id='$org'),
    'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id='$org'),
    'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
    'configuration',(SELECT jsonb_agg(to_jsonb(c) ORDER BY tenant_id) FROM organization_configurations c WHERE tenant_id='$org'),
    'history',(SELECT jsonb_agg(to_jsonb(h) ORDER BY version) FROM organization_configuration_history h WHERE tenant_id='$org'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY version) FROM organization_configuration_receipts r WHERE tenant_id='$org'),
    'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$org' AND event_type NOT IN ('ORGANIZATION_SUSPENDED','ORGANIZATION_DEACTIVATED','ORGANIZATION_REACTIVATED')))::text);"
}
for target in SUSPENDED DEACTIVATED; do
  test "$(request POST /organizations '{"name":"Private retained lifecycle parent"}')" = 201
  org=$(jq -r '.organization.id' "$scratch/response.json")
  [[ "$org" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
  configuration="/organizations/$org/configuration"
  test "$(request PATCH "$configuration" '{"version":0,"configuration":{"legalName":"Original legal name","jurisdiction":"CA-BC","timezone":"America/Vancouver","managementCompanyName":"Original management"}}')" = 200
  test "$(request PATCH "$configuration" '{"version":1,"configuration":{"legalName":"Later legal name","jurisdiction":"CA-ON","timezone":"America/Toronto","managementCompanyName":"Later management"}}')" = 200
  test "$(get "$configuration/history")" = 200
  jq -S '.items' "$scratch/response.json" > "$scratch/original-history.json"
  jq -e '.items|map(.version)==[2,1]' "$scratch/response.json" >/dev/null
  test "$(request POST /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Retained Board",visibility:"ORGANIZATION"}')")" = 201
  board=$(jq -r '.id' "$scratch/response.json")
  test "$(request POST "/boards/$board/lists" '{"name":"Retained List"}')" = 201
  list=$(jq -r '.id' "$scratch/response.json")
  test "$(request POST "/lists/$list/cards" '{"title":"Retained private Card","description":"Original private history"}')" = 201
  card=$(jq -r '.id' "$scratch/response.json")
  for id in "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]; done
  intake=$(jq -nc --arg board "$board" --arg list "$list" '{version:2,configuration:{legalName:"Later legal name",jurisdiction:"CA-ON",timezone:"America/Toronto",managementCompanyName:"Later management",intakeBoardId:$board,intakeListId:$list}}')
  test "$(request PATCH "$configuration" "$intake")" = 200
  test "$(get "$configuration/history")" = 200
  jq -e --arg board "$board" --arg list "$list" '.items[0].version==3 and .items[0].configuration.intakeBoardId==$board and .items[0].configuration.intakeListId==$list' "$scratch/response.json" >/dev/null
  jq -S '.items' "$scratch/response.json" > "$scratch/original-history.json"
  test "$(request POST "/cards/$card/archive" '{"version":1}')" = 200
  test "$(request POST "/lists/$list/archive" '{"version":1}')" = 200
  details="/boards/$board/cards/$card/archived-details"
  test "$(get "$details")" = 200
  cp "$scratch/response.json" "$scratch/original-card.json"
  before=$(retained)
  stale_intake=$(jq '.version=3' <<< "$intake")
  test "$(request PATCH "$configuration" "$stale_intake")" = 400
  jq -e '.code=="configuration_intake_unavailable"' "$scratch/response.json" >/dev/null
  test "$(retained)" = "$before"
  test "$(get "/organizations/$org/operational-lifecycle")" = 200
  version=$(jq -r '.version' "$scratch/response.json")
  test "$(request PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --arg target "$target" --argjson version "$version" '{status:$target,version:$version}')")" = 200
  for route in "$configuration/history" "$details" "/boards/$board/archived-lists" "/boards/$board/archived-cards"; do
    test "$(get "$route")" = 404
    scripts/ci/assert-file-excludes.sh 'Original legal name|Later legal name|Retained private Card|Original private history' "$scratch/response.json"
  done
  test "$(request POST "/lists/$list/restore" '{"version":2}')" = 404
  test "$(request POST "/cards/$card/restore" '{"version":2}')" = 404
  test "$(request DELETE "/organizations/$org?version=$((version+1))" '{}')" = 404
  test "$(retained)" = "$before"
  test "$(request PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --argjson version "$((version+1))" '{status:"ACTIVE",version:$version}')")" = 200
  test "$(retained)" = "$before"
  test "$(get "$configuration/history")" = 200
  jq -S '.items' "$scratch/response.json" > "$scratch/restored-history.json"
  cmp "$scratch/original-history.json" "$scratch/restored-history.json"
  test "$(get "$details")" = 200
  cmp "$scratch/original-card.json" "$scratch/response.json"
  test "$(request PATCH "$configuration" "$stale_intake")" = 400
  jq -e '.code=="configuration_intake_unavailable"' "$scratch/response.json" >/dev/null
  test "$(retained)" = "$before"
  # Clearing the obsolete operational selection creates a new revision; old
  # historical foreign keys and source captions must retain their meaning.
  test "$(request PATCH "$configuration" '{"version":3,"configuration":{"legalName":"Later legal name","jurisdiction":"CA-ON","timezone":"America/Toronto","managementCompanyName":"Later management"}}')" = 200
  test "$(get "$configuration/history")" = 200
  jq -e '.items[0].version==4 and .items[0].configuration.intakeBoardId==null and .items[0].configuration.intakeListId==null' "$scratch/response.json" >/dev/null
  jq -S '.items[1:]' "$scratch/response.json" > "$scratch/earlier-history.json"
  cmp "$scratch/original-history.json" "$scratch/earlier-history.json"
  configuration_snapshot() {
    admin "SELECT md5(jsonb_build_object(
      'current',(SELECT to_jsonb(c) FROM organization_configurations c WHERE tenant_id='$org'),
      'history',(SELECT jsonb_agg(to_jsonb(h) ORDER BY version) FROM organization_configuration_history h WHERE tenant_id='$org'),
      'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY version) FROM organization_configuration_receipts r WHERE tenant_id='$org'),
      'lifecycle',(SELECT jsonb_agg(to_jsonb(h) ORDER BY version) FROM organization_operational_lifecycle_history h WHERE tenant_id='$org'))::text);"
  }
  before_deletion=$(configuration_snapshot)
  list_archive=$(admin "SELECT archived_at FROM board_lists WHERE id='$list' AND tenant_id='$org';")
  card_archive=$(admin "SELECT archived_at FROM cards WHERE id='$card' AND tenant_id='$org';")
  test "$(request DELETE "/organizations/$org?version=$((version+2))" '{}')" = 202
  for route in "$configuration/history" "$details" "/organizations/$org/operational-lifecycle" "/organizations/$org/operational-lifecycle/events?afterVersion=0"; do
    test "$(get "$route")" = 404
    scripts/ci/assert-file-excludes.sh 'Original legal name|Later legal name|Retained private Card|Original private history' "$scratch/response.json"
  done
  test "$(request PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --argjson version "$((version+3))" '{status:"ACTIVE",version:$version}')")" = 404
  test "$(configuration_snapshot)" = "$before_deletion"
  test "$(admin "SELECT count(*) FROM organizations WHERE id='$org' AND status IN ('DELETING','DELETED');")" = 1
  # The ordinary release Worker must complete through its restricted queue;
  # neither the API acknowledgment nor a fixture SQL update proves completion.
  # CI deliberately leaves discovery disabled for stable command snapshots.
  # Give this completion phase only its fixture Organization, using the same
  # built image and runtime role, then restore the job's canonical settings.
  worker_active=true
  STRATAAI_WORKER_ORGANIZATION_IDS="$org" STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED=false \
  STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=false STRATAAI_INVITATION_RECIPIENT_AUTHORITY_DISCOVERY_ENABLED=false \
  STRATAAI_INVITATION_ISSUER_AUTHORITY_DISCOVERY_ENABLED=false \
  docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
  for attempt in $(seq 1 60); do
    if test "$(admin "SELECT count(*) FROM organizations WHERE id='$org' AND status='DELETED';")" = 1; then break; fi
    sleep 1
  done
  test "$(admin "SELECT count(*) FROM organizations WHERE id='$org' AND status='DELETED';")" = 1
  test "$(configuration_snapshot)" = "$before_deletion"
  test "$(admin "SELECT count(*) FROM boards WHERE id='$board' AND tenant_id='$org' AND lifecycle_state='DELETED' AND deleted_at IS NOT NULL;")" = 1
  test "$(admin "SELECT count(*) FROM board_lists WHERE id='$list' AND board_id='$board' AND tenant_id='$org' AND lifecycle_state='DELETED' AND deleted_at IS NOT NULL;")" = 1
  test "$(admin "SELECT count(*) FROM cards WHERE id='$card' AND list_id='$list' AND board_id='$board' AND tenant_id='$org' AND lifecycle_state='DELETED' AND deleted_at IS NOT NULL;")" = 1
  test "$(admin "SELECT archived_at FROM board_lists WHERE id='$list' AND tenant_id='$org';")" = "$list_archive"
  test "$(admin "SELECT archived_at FROM cards WHERE id='$card' AND tenant_id='$org';")" = "$card_archive"
  for route in "$configuration/history" "$details" "/organizations/$org/operational-lifecycle" "/organizations/$org/operational-lifecycle/events?afterVersion=0"; do
    test "$(get "$route")" = 404
    scripts/ci/assert-file-excludes.sh 'Original legal name|Later legal name|Retained private Card|Original private history' "$scratch/response.json"
  done
  test "$(request PATCH "/organizations/$org/operational-lifecycle" "$(jq -nc --argjson version "$((version+4))" '{status:"ACTIVE",version:$version}')")" = 404
  test "$(configuration_snapshot)" = "$before_deletion"
  restore_worker
done
echo 'Production lifecycle preserved archived/configuration history and references; deletion withdrew access and lifecycle reactivation stayed denied.'
