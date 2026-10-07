#!/usr/bin/env bash
set -euo pipefail
# PRD-03 / PRD-60 / ARCH-11: a real HTTP parent command publishes the source;
# the retained exact Worker delivers it. Candidate rows are disposable fixtures.
BASE_URL="${1:-http://127.0.0.1:8080}"
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
email="AUTHORITY-$organization@EXAMPLE.TEST"
other_email="AUTHORITY-OTHER-$organization@EXAMPLE.TEST"
admin "INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
 SELECT gen_random_uuid(),'$organization',lower(CASE WHEN i=205 THEN '$other_email' ELSE '$email' END),
 CASE WHEN i=205 THEN '$other_email' ELSE '$email' END,encode(sha256(('$organization/'||i)::bytea),'hex'),
 'PORTAL','OWNER','$actor',clock_timestamp(),clock_timestamp()+interval '1 day' FROM generate_series(1,205) i;" >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(jq -nc --arg id "$organization" '{organizationId:$id,name:"Authority unrelated Work queue"}')" "$BASE_URL/boards" >/dev/null
curl --fail --silent --show-error -b "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X PATCH \
  -d '{"name":"Authority changed parent","version":1}' "$BASE_URL/organizations/$organization" | jq -e '.version==2' >/dev/null
source="$(admin "SELECT event_id FROM organization_metadata_events WHERE tenant_id='$organization' AND event_type='ORGANIZATION_UPDATED';")"
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
  admin "SELECT (SELECT array_agg(scanned_count ORDER BY scanned_count)=ARRAY[5,100,100] AND bool_and(completed_at IS NOT NULL)
    FROM invitation_recipient_authority_pages WHERE tenant_id='$organization' AND source_event_id='$source')
    AND (SELECT count(*)=3 AND bool_and(j.state='SUCCEEDED' AND j.attempt_count=1 AND j.actor_id=e.actor_id
      AND j.correlation_id=e.correlation_id AND j.safe_metadata=jsonb_build_object('eventId',e.event_id))
      FROM background_jobs j JOIN invitation_recipient_authority_pages p ON p.tenant_id=j.tenant_id AND p.job_id=j.id
      JOIN organization_metadata_events e ON e.tenant_id=p.tenant_id AND e.event_id=p.source_event_id
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
# Restart the same immutable Worker after acknowledgment: completed pages,
# counters and deduplicated effects must remain unchanged.
authority_worker true
test "$(finished)" = t
test "$before" = "$(unrelated)"
echo 'Exact Worker authority delivery: canonical HTTP parent update, automatic scope, 100/100/5 pages, two deduplicated recipients, restart and unrelated queue isolation passed.'
