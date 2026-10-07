#!/usr/bin/env bash
set -Eeuo pipefail
# PRD-03 / PRD-60 / ARCH-11: a real HTTP parent command publishes the source;
# the retained exact Worker delivers it. Candidate rows are disposable fixtures.
BASE_URL="${1:-http://127.0.0.1:8080}"
source_kind="${2:-organization}"
case "$source_kind" in organization|board|lifecycle|issuer) ;; *) echo 'Invalid authority source kind' >&2; exit 2 ;; esac
last_count=5
issuer_enabled=false
scratch="$(mktemp -d)"
worker_changed=false
cleanup() {
  local status=$?
  if test "$worker_changed" = true; then
    docker compose -f compose.release.yml up -d --no-deps --force-recreate --wait --wait-timeout 180 worker >/dev/null || status=1
  fi
  rm -rf "$scratch"
  exit "$status"
}
trap cleanup EXIT
trap 'echo "Recipient authority delivery check failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres psql -X -U strataai -d strataai -At -v ON_ERROR_STOP=1 -c "$1"; }
authority_worker() {
  STRATAAI_INVITATION_RECIPIENT_AUTHORITY_DISCOVERY_ENABLED="$1" STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=false \
    STRATAAI_INVITATION_ISSUER_AUTHORITY_DISCOVERY_ENABLED="${2:-$issuer_enabled}" \
    STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED=false STRATAAI_WORKER_ORGANIZATION_IDS='' \
    docker compose -f compose.release.yml up -d --no-deps --force-recreate --wait --wait-timeout 180 worker >/dev/null
}
worker_changed=true
authority_worker false
credentials="$(jq -nc --arg email "authority-owner-$(date +%s%N)@example.test" '{email:$email,password:"authority-owner-correct-horse",displayName:"Authority Owner"}')"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$BASE_URL/auth/register" > "$scratch/owner.json"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -c "$scratch/cookies" -d "$credentials" "$BASE_URL/auth/login" >/dev/null
actor="$(jq -r '.user.id' "$scratch/owner.json")"
organization="$(curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d '{"name":"Authority original parent"}' "$BASE_URL/organizations" | jq -r '.organization.id')"
[[ "$actor" =~ ^[0-9a-f-]{36}$ && "$organization" =~ ^[0-9a-f-]{36}$ ]]
# Populate 205 candidates for two private recipients, including duplicate emails
# across page boundaries. This does not manufacture audits, pages or readiness.
# UUID text is lowercase; normalize the complete address, including its UUID.
email="AUTHORITY-${organization^^}@EXAMPLE.TEST"
other_email="AUTHORITY-OTHER-${organization^^}@EXAMPLE.TEST"
if test "$source_kind" = issuer; then
  # Preserve real Owner continuity through ordinary invitation acceptance.
  # Reuse the first recipient address so this extra accepted historical row
  # still represents only two distinct recipient effects.
  successor="$(jq -nc --arg email "${email,,}" '{email:$email,password:"authority-successor-correct-horse",displayName:"Authority successor"}')"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$successor" "$BASE_URL/auth/register" >/dev/null
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -c "$scratch/successor-cookies" -d "$successor" "$BASE_URL/auth/login" >/dev/null
  successor_invitation="$(curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -d "$(jq -nc --arg email "${email,,}" '{email:$email,surface:"INTERNAL",targetRole:"OWNER"}')" "$BASE_URL/organizations/$organization/invitations" | jq -r '.id')"
  [[ "$successor_invitation" =~ ^[0-9a-f-]{36}$ ]]
  curl --fail --silent --show-error -b "$scratch/successor-cookies" -H 'X-StrataAI-Request: 1' -X POST "$BASE_URL/me/invitations/$successor_invitation/accept" >/dev/null
  last_count=6
fi
admin "INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 SELECT gen_random_uuid(),'$organization',lower(CASE WHEN i=205 THEN '$other_email' ELSE '$email' END),
 CASE WHEN i=205 THEN '$other_email' ELSE '$email' END,encode(sha256(('$organization/'||i)::bytea),'hex'),
 'PORTAL','OWNER','$actor',clock_timestamp(),clock_timestamp()+interval '1 day' FROM generate_series(1,205) i;" >/dev/null
board="$(curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg id "$organization" '{organizationId:$id,name:"Authority unrelated Work queue"}')" "$BASE_URL/boards" | jq -r '.id')"
[[ "$board" =~ ^[0-9a-f-]{36}$ ]]
if test "$source_kind" = issuer; then
  request="$(cat /proc/sys/kernel/random/uuid)"
  for replay in 1 2; do
    status="$(curl --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
      -H "Idempotency-Key: $request" -X POST -d '{}' -o "$scratch/deactivation.json" -w '%{http_code}' \
      "$BASE_URL/me/deactivate?expectedActorId=$actor")"
    test "$status" = 204
  done
  source="$(admin "SELECT event_id FROM invitation_issuer_authority_sources WHERE actor_id='$actor';")"
  [[ "$source" =~ ^[0-9a-f-]{36}$ ]]
  test "$(admin "SELECT count(*) FROM invitation_recipient_authority_sources WHERE issuer_source_event_id='$source';")" = 0
  test "$(admin "SELECT count(*)=1 AND bool_and(state='PENDING' AND attempt_count=0) FROM invitation_issuer_authority_jobs WHERE event_id='$source';")" = t
  issuer_enabled=true
  authority_worker false true
  for ((attempt=0; attempt<60; attempt++)); do
    if test "$(admin "SELECT count(*)=1 AND bool_and(state='SUCCEEDED' AND attempt_count=1 AND scanned_count=1) FROM invitation_issuer_authority_jobs WHERE event_id='$source';")" = t; then break; fi
    sleep 1
  done
  test "$(admin "SELECT count(*)=1 AND bool_and(state='SUCCEEDED' AND attempt_count=1 AND scanned_count=1) FROM invitation_issuer_authority_jobs WHERE event_id='$source';")" = t
  test "$(admin "SELECT status='DEACTIVATED' FROM users WHERE id='$actor';")" = t
  test "$(admin "SELECT count(*)=1 FROM identity_events WHERE user_id='$actor' AND event_type='USER_DEACTIVATED';")" = t
elif test "$source_kind" = lifecycle; then
  request="$(cat /proc/sys/kernel/random/uuid)"
  status="$(curl --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: $request" -X DELETE -d '{}' -o "$scratch/request.json" -w '%{http_code}' \
    "$BASE_URL/organizations/$organization?version=1&expectedActorId=$actor")"
  test "$status" = 202
  source="$(admin "SELECT event_id FROM invitation_recipient_organization_lifecycle_sources WHERE tenant_id='$organization' AND event_type='ORGANIZATION_DELETION_REQUESTED';")"
elif test "$source_kind" = board; then
  curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X PATCH \
    -d '{"name":"Authority changed Board","version":1}' "$BASE_URL/boards/$board" | jq -e '.version==2' >/dev/null
  source="$(admin "SELECT event_id FROM work_events WHERE tenant_id='$organization' AND board_id='$board' AND event_type='BOARD_UPDATED';")"
else
  curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X PATCH \
    -d '{"name":"Authority changed parent","version":1}' "$BASE_URL/organizations/$organization" | jq -e '.version==2' >/dev/null
  source="$(admin "SELECT event_id FROM organization_metadata_events WHERE tenant_id='$organization' AND event_type='ORGANIZATION_UPDATED';")"
fi
[[ "$source" =~ ^[0-9a-f-]{36}$ ]]
test "$(admin "SELECT count(*)=1 AND bool_and(completed_at IS NULL) FROM invitation_recipient_authority_pages WHERE tenant_id='$organization' AND source_event_id='$source';")" = t
unrelated() {
  admin "SELECT jsonb_build_object('jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j
    WHERE tenant_id='$organization' AND job_type<>'INVITATION_RECIPIENT_AUTHORITY_PAGE'),
    'work',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM work_events e WHERE tenant_id='$organization'),
    'metadata',(SELECT jsonb_agg(to_jsonb(e) ORDER BY event_id) FROM organization_metadata_events e WHERE tenant_id='$organization'))::text;"
}
before="$(unrelated)"
authority_worker true
finished() {
  admin "SELECT (SELECT array_agg(scanned_count ORDER BY scanned_count)=ARRAY[$last_count,100,100] AND bool_and(completed_at IS NOT NULL)
    FROM invitation_recipient_authority_pages WHERE tenant_id='$organization' AND source_event_id='$source')
    AND (SELECT count(*)=3 AND bool_and(j.state='SUCCEEDED' AND j.attempt_count=1 AND j.actor_id=e.actor_id
      AND j.correlation_id=e.correlation_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id))
      FROM background_jobs j JOIN invitation_recipient_authority_pages p ON p.tenant_id=j.tenant_id AND p.job_id=j.id
      JOIN invitation_recipient_authority_source_rows e ON e.tenant_id=p.tenant_id AND e.event_id=p.source_event_id
      WHERE p.tenant_id='$organization' AND p.source_event_id='$source')
    AND (SELECT count(*)=2 FROM invitation_recipient_authority_effects WHERE tenant_id='$organization' AND source_event_id='$source')
    AND (SELECT count(*)=2 AND bool_and(revision=1) FROM invitation_recipient_authority_revisions WHERE email_normalized IN ('$email','$other_email'));"
}
for ((attempt=0; attempt<60; attempt++)); do
  if test "$(finished)" = t; then break; fi
  sleep 1
done
test "$(finished)" = t
test "$before" = "$(unrelated)"
if test "$source_kind" = issuer; then
  test "$(admin "SELECT count(*)=2 FROM invitation_issuer_authority_effects WHERE event_id='$source';")" = t
fi
# Restart the same immutable Worker after acknowledgment: completed pages,
# counters and deduplicated effects must remain unchanged.
authority_worker true
test "$(finished)" = t
test "$before" = "$(unrelated)"
if test "$source_kind" = issuer; then
  test "$(admin "SELECT count(*)=1 AND bool_and(state='SUCCEEDED' AND attempt_count=1 AND scanned_count=1) FROM invitation_issuer_authority_jobs WHERE event_id='$source';")" = t
fi
echo "Exact Worker authority delivery: canonical HTTP $source_kind command, automatic scope, 100/100/$last_count pages, two deduplicated recipients, restart and unrelated queue isolation passed."

if test "$source_kind" = lifecycle; then
  # Resume automatic deletion discovery without a configured Organization ID.
  # The same retained Worker traverses the real graph, emits BOARD_DELETED and
  # the canonical terminal event, then delivers each authority source normally.
  STRATAAI_INVITATION_RECIPIENT_AUTHORITY_DISCOVERY_ENABLED=true STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED=false \
    STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED=true STRATAAI_WORKER_ORGANIZATION_IDS='' \
    docker compose -f compose.release.yml up -d --no-deps --force-recreate --wait --wait-timeout 180 worker >/dev/null
  terminal_finished() {
    admin "SELECT
      (SELECT status='DELETED' AND version=3 AND deleted_by='$actor' FROM organizations WHERE id='$organization')
      AND (SELECT count(*)=1 AND bool_and(e.actor_id='$actor' AND e.ready_at IS NOT NULL
        AND s.actor_id=e.actor_id AND s.correlation_id=e.correlation_id AND s.terminal_event_id=e.event_id)
        FROM organization_lifecycle_events e JOIN invitation_recipient_organization_lifecycle_sources s
        ON s.tenant_id=e.tenant_id AND s.event_id=e.event_id WHERE e.tenant_id='$organization')
      AND (SELECT count(*)=3 FROM invitation_recipient_authority_sources WHERE tenant_id='$organization')
      AND (SELECT count(*)=9 AND bool_and(p.completed_at IS NOT NULL AND j.state='SUCCEEDED' AND j.attempt_count=1
          AND j.actor_id=e.actor_id AND j.correlation_id=e.correlation_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id))
        FROM invitation_recipient_authority_pages p JOIN background_jobs j ON j.tenant_id=p.tenant_id AND j.id=p.job_id
        JOIN invitation_recipient_authority_source_rows e ON e.tenant_id=p.tenant_id AND e.event_id=p.source_event_id
        WHERE p.tenant_id='$organization')
      AND NOT EXISTS(SELECT 1 FROM invitation_recipient_authority_sources s WHERE s.tenant_id='$organization' AND
        (SELECT array_agg(scanned_count ORDER BY scanned_count) FROM invitation_recipient_authority_pages p
          WHERE p.tenant_id=s.tenant_id AND p.source_event_id=s.event_id) IS DISTINCT FROM ARRAY[5,100,100])
      AND (SELECT count(*)=6 FROM invitation_recipient_authority_effects WHERE tenant_id='$organization')
      AND (SELECT count(*)=2 AND bool_and(revision=3) FROM invitation_recipient_authority_revisions WHERE email_normalized IN ('$email','$other_email'))
      AND (SELECT count(*)=1 FROM audit_events WHERE tenant_id='$organization' AND event_type='ORGANIZATION_DELETION_REQUESTED')
      AND (SELECT count(*)=1 AND bool_and(ready_at IS NOT NULL) FROM work_events WHERE tenant_id='$organization' AND event_type='BOARD_DELETED');"
  }
  for ((attempt=0; attempt<120; attempt++)); do
    if test "$(terminal_finished)" = t; then break; fi
    sleep 1
  done
  test "$(terminal_finished)" = t
  authority_worker true
  test "$(terminal_finished)" = t
  echo 'Exact Worker Organization lifecycle: real HTTP request, automatic graph completion, canonical request/Board/terminal sources, 100/100/5 per source, retained actor/correlation, ready completion and restart deduplication passed.'
fi
