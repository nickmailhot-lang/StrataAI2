#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable invitation signup fixtures may run only in CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"; scratch="$(mktemp -d)"; pids=(); gate_pid=''
export STRATAAI_TEST_COMMAND_TIMEOUT=30
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'GRANT INSERT ON users,audit_events,identity_events,identity_registration_replays,board_members,work_events,background_jobs TO strataai_api_runtime;' >/dev/null
  if test -n "${org:-}"; then admin "UPDATE organization_members SET role='OWNER' WHERE tenant_id='$org' AND user_id='$owner_id';" >/dev/null; fi
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Invitation signup check failed at line $LINENO" >&2' ERR
compose=(docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml)
"${compose[@]}" up -d --wait --wait-timeout 180 api >/dev/null
owner_body="$(jq -nc --arg email "signup-owner-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"signup-correct-horse-battery",displayName:"Signup owner"}')"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$owner_body" "$base/auth/register" > "$scratch/owner"
owner_id="$(jq -r '.user.id' "$scratch/owner")"
curl --fail --silent --show-error -c "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$owner_body" "$base/auth/login" >/dev/null
owner_post() { curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$2" -o "$scratch/response" -w '%{http_code}' "$base$1"; }
test "$(owner_post /organizations '{"name":"Closed invitation signup"}')" = 201
org="$(jq -r '.organization.id' "$scratch/response")"
"${compose[@]}" -f scripts/ci/compose.invitation-signup-test.yml up -d --wait --wait-timeout 180 api >/dev/null
issue() {
  test "$(owner_post "/organizations/$org/invitations" "$(jq -nc --arg email "$email" --arg surface "${surface:-INTERNAL}" --arg role "${role:-MEMBER}" '{email:$email,surface:$surface,targetRole:$role}')")" = 201
  invitation_id="$(jq -r '.id' "$scratch/response")"; token="$(openssl rand -hex 32)"
  token_hash="$(printf '%s' "$token" | sha256sum | cut -d ' ' -f 1)"
  admin "UPDATE invitations SET token_hash='$token_hash' WHERE tenant_id='$org' AND id='$invitation_id';" >/dev/null
  body="$(jq -nc --arg email "$email" --arg token "$token" '{email:$email,password:"signup-correct-horse-battery",displayName:"Invited signup",invitationToken:$token}')"
  key="$(cat /proc/sys/kernel/random/uuid)"
}
register() { curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "${1:-$body}" -o "$scratch/response" -w '%{http_code}' "$base/auth/register"; }
state() { admin "SELECT jsonb_build_object('users',(SELECT count(*) FROM users WHERE email_normalized=upper('$email')),
  'receipts',(SELECT count(*) FROM identity_registration_replays r JOIN users u ON u.id=r.user_id WHERE u.email_normalized=upper('$email')),
  'events',(SELECT count(*) FROM identity_events e JOIN users u ON u.id=e.user_id WHERE u.email_normalized=upper('$email')),
  'audit',(SELECT count(*) FROM audit_events a JOIN users u ON u.id=a.actor_id WHERE u.email_normalized=upper('$email')),
  'members',(SELECT count(*) FROM organization_members m JOIN users u ON u.id=m.user_id WHERE m.tenant_id='$org' AND u.email_normalized=upper('$email')),
  'portal',(SELECT count(*) FROM portal_access p JOIN users u ON u.id=p.user_id WHERE p.tenant_id='$org' AND u.email_normalized=upper('$email')),
  'accepted',(SELECT count(*) FROM invitations WHERE tenant_id='$org' AND email_normalized=upper('$email') AND accepted_at IS NOT NULL))::text;"; }
for surface in INTERNAL PORTAL; do
  role=MEMBER; if test "$surface" = PORTAL; then role=OWNER; fi
  email="invitation-signup-${surface,,}-${RANDOM}-${RANDOM}@example.test"; issue
  before="$(state)"
  test "$(register "$(jq 'del(.invitationToken)' <<< "$body")")" = 403
  test "$(register "$(jq '.invitationToken="invalid-proof"' <<< "$body")")" = 400
  test "$before" = "$(state)"
  for table in audit_events identity_events identity_registration_replays; do
    admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
    test "$(register)" = 503; test "$before" = "$(state)"
    admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
  done
  for n in 1 2; do
    curl --max-time 60 --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "$body" -o "$scratch/ack-$n" -w '%{http_code}' "$base/auth/register" > "$scratch/status-$n" &
    pids+=($!)
  done
  for pid in "${pids[@]}"; do wait "$pid"; done; pids=()
  test "$(cat "$scratch/status-1")" = 201; test "$(cat "$scratch/status-2")" = 201; cmp "$scratch/ack-1" "$scratch/ack-2"
  test "$(register)" = 201; cmp "$scratch/ack-1" "$scratch/response"
  jq -e '.users==1 and .receipts==1 and .events==1 and .audit==1 and .members==0 and .portal==0 and .accepted==0' <<< "$(state)" >/dev/null
  scripts/ci/assert-file-excludes.sh "$token|invitationToken|tokenHash" "$scratch/response"
done
original_body="$body"; original_key="$key"; issue
key="$original_key"; test "$(register)" = 409
body="$original_body"; key="$original_key"
# A replay must re-admit the issuer after the actual parent wait, before receipt disclosure.
mkfifo "$scratch/gate.in"; exec 3<>"$scratch/gate.in"
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.out" &
gate_pid=$!; printf "BEGIN; SELECT id FROM organizations WHERE id='%s' FOR UPDATE; SELECT 'gate-ready';\n" "$org" >&3
for attempt in $(seq 1 100); do if grep -q '^gate-ready$' "$scratch/gate.out"; then break; fi; sleep 0.1; done
grep -q '^gate-ready$' "$scratch/gate.out"
register > "$scratch/wait-status" & pending=$!; pids+=($pending)
for attempt in $(seq 1 100); do if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock';")" -ge 1; then break; fi; sleep 0.1; done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock';")" -ge 1
printf "UPDATE organization_members SET role='MEMBER' WHERE tenant_id='%s' AND user_id='%s'; COMMIT;\n\\q\n" "$org" "$owner_id" >&3
exec 3>&-; wait "$gate_pid"; gate_pid=''; wait "$pending"; pids=()
test "$(cat "$scratch/wait-status")" = 400; scripts/ci/assert-file-excludes.sh 'Invited signup|verificationToken|organizationId' "$scratch/response"
admin "UPDATE organization_members SET role='OWNER' WHERE tenant_id='$org' AND user_id='$owner_id';" >/dev/null
# Expiry during an audit write wait rolls back the entire new account and receipt.
surface=INTERNAL; role=MEMBER; email="invitation-signup-expiry-${RANDOM}-${RANDOM}@example.test"; issue
admin "UPDATE invitations SET expires_at=clock_timestamp()+interval '8 seconds' WHERE id='$invitation_id';" >/dev/null
before="$(state)"; rm "$scratch/gate.in"; mkfifo "$scratch/gate.in"; exec 3<>"$scratch/gate.in"
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.out" &
gate_pid=$!; printf "BEGIN; LOCK TABLE audit_events IN SHARE MODE; SELECT 'audit-ready';\n" >&3
for attempt in $(seq 1 100); do if grep -q '^audit-ready$' "$scratch/gate.out"; then break; fi; sleep 0.1; done
grep -q '^audit-ready$' "$scratch/gate.out"
register > "$scratch/expiry-status" & pending=$!; pids+=($pending)
for attempt in $(seq 1 100); do if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query ILIKE '%INSERT INTO audit_events%';")" -ge 1; then break; fi; sleep 0.1; done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query ILIKE '%INSERT INTO audit_events%';")" -ge 1
sleep 9; printf 'COMMIT;\n\\q\n' >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; wait "$pending"; pids=()
test "$(cat "$scratch/expiry-status")" = 400; test "$before" = "$(state)"
# PRD-60 ONBOARD-FR-001: explicit Board target signup against restricted
# PostgreSQL. Administrative target attachment tests the proof consumer only;
# the public Board invitation creation endpoint is a separate integration.
test "$(owner_post /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Board signup",visibility:"PRIVATE"}')")" = 201
signup_board="$(jq -r '.id' "$scratch/response")"
board_accept() { curl --max-time 60 --silent --show-error -b "$scratch/board.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -d "$(jq -nc --arg token "$token" '{token:$token}')" -o "$scratch/response" -w '%{http_code}' "$base/invitations/accept"; }
board_accept_state() { admin "SELECT jsonb_build_object(
 'organization',(SELECT to_jsonb(m) FROM organization_members m WHERE tenant_id='$org' AND user_id='$board_recipient'),
 'board',(SELECT to_jsonb(m) FROM board_members m WHERE board_id='$signup_board' AND user_id='$board_recipient'),
 'accepted',(SELECT jsonb_build_array(accepted_at,accepted_by_user_id) FROM invitations WHERE id='$invitation_id'),
 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org' AND board_id='$signup_board'),
 'stream',(SELECT last_sequence FROM work_event_streams WHERE tenant_id='$org' AND board_id='$signup_board'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'))::text;"; }
for board_role in ADMIN MEMBER; do
 surface=INTERNAL; role=MEMBER; email="board-signup-${board_role,,}-${RANDOM}-${RANDOM}@example.test"; issue
 admin "UPDATE invitations SET target_board_id='$signup_board',target_board_role='$board_role' WHERE id='$invitation_id' AND tenant_id='$org';" >/dev/null
 before="$(state)"
 admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
 test "$(register)" = 503; test "$before" = "$(state)"
 admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
 test "$(register)" = 201; cp "$scratch/response" "$scratch/board-ack"
 test "$(register)" = 201; cmp "$scratch/board-ack" "$scratch/response"
 jq -e '.users==1 and .receipts==1 and .events==1 and .audit==1 and .members==0 and .portal==0 and .accepted==0' <<< "$(state)" >/dev/null
 test "$(admin "SELECT count(*) FROM board_members b JOIN users u ON u.id=b.user_id WHERE b.board_id='$signup_board' AND u.email_normalized=upper('$email');")" = 0
 scripts/ci/assert-file-excludes.sh "$token|invitationToken|tokenHash" "$scratch/response"
 admin "UPDATE boards SET lifecycle_state='ARCHIVED' WHERE id='$signup_board';" >/dev/null
 test "$(register)" = 400
 admin "UPDATE boards SET lifecycle_state='ACTIVE' WHERE id='$signup_board';" >/dev/null
 # Verification is an explicit disposable administrative fixture here, not
 # evidence of actual invitation/verification transport through the Worker.
 board_recipient="$(jq -r '.user.id' "$scratch/board-ack")"
 admin "UPDATE users SET email_verified=true WHERE id='$board_recipient';" >/dev/null
 curl --fail --silent --show-error -c "$scratch/board.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
  -d "$(jq '{email,password}' <<< "$body")" "$base/auth/login" >/dev/null
 before_review="$(board_accept_state)"
 test "$(curl --max-time 60 --silent --show-error -b "$scratch/board.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -X POST -d "$(jq -nc --arg token "$token" '{token:$token}')" -o "$scratch/response" -w '%{http_code}' "$base/invitations/review")" = 200
 jq -e --arg board "$signup_board" --arg role "$board_role" '.boardTarget.boardId==$board and .boardTarget.role==$role and (.boardName|length)>0' "$scratch/response" >/dev/null
 test "$before_review" = "$(board_accept_state)"
 before_accept="$(board_accept_state)"
 for denied in board_members audit_events work_events background_jobs; do
  admin "REVOKE INSERT ON $denied FROM strataai_api_runtime;" >/dev/null
  test "$(board_accept)" = 503; test "$before_accept" = "$(board_accept_state)"
  admin "GRANT INSERT ON $denied TO strataai_api_runtime;" >/dev/null
 done
 test "$(board_accept)" = 200
 jq -e --arg board "$signup_board" --arg role "$board_role" '.boardTarget.boardId==$board and .boardTarget.role==$role' "$scratch/response" >/dev/null
 scripts/ci/assert-file-excludes.sh "$token|invitationToken|tokenHash" "$scratch/response"
 cp "$scratch/response" "$scratch/board-accepted"
 test "$(admin "SELECT count(*) FROM board_members WHERE tenant_id='$org' AND board_id='$signup_board' AND user_id='$board_recipient' AND status='ACTIVE' AND role='$board_role';")" = 1
 test "$(board_accept)" = 400
 if test "$board_role" = ADMIN; then
  # A MEMBER invitation adds access; it cannot demote a current Organization
  # owner or active Board admin through the invitation acceptance path.
  admin "UPDATE organization_members SET role='OWNER' WHERE tenant_id='$org' AND user_id='$board_recipient';" >/dev/null
  issue; admin "UPDATE invitations SET target_board_id='$signup_board',target_board_role='MEMBER' WHERE id='$invitation_id';" >/dev/null
  test "$(board_accept)" = 200; cp "$scratch/response" "$scratch/board-accepted"
  test "$(admin "SELECT role FROM organization_members WHERE tenant_id='$org' AND user_id='$board_recipient';")" = OWNER
  test "$(admin "SELECT role FROM board_members WHERE board_id='$signup_board' AND user_id='$board_recipient';")" = ADMIN
 fi
 status="$(curl --silent --show-error -b "$scratch/board.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{}' \
  -o "$scratch/response" -w '%{http_code}' "$base/me/invitations/$invitation_id/accept")"
 test "$status" = 200; cmp "$scratch/board-accepted" "$scratch/response"
 test "$(curl --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -X DELETE \
  -o "$scratch/response" -w '%{http_code}' "$base/boards/$signup_board/members/$board_recipient")" = 204
 removed_state="$(board_accept_state)"
 test "$(curl --silent --show-error -b "$scratch/board.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{}' \
  -o "$scratch/response" -w '%{http_code}' "$base/me/invitations/$invitation_id/accept")" = 200
 test "$removed_state" = "$(board_accept_state)"
done
# A Board archive committed during the real Board lock wait must reject signup
# before any account/verification/receipt mutation, despite the earlier route hint.
surface=INTERNAL; role=MEMBER; email="board-signup-wait-${RANDOM}-${RANDOM}@example.test"; issue
admin "UPDATE invitations SET target_board_id='$signup_board',target_board_role='MEMBER' WHERE id='$invitation_id';" >/dev/null
before="$(state)"; rm "$scratch/gate.in"; mkfifo "$scratch/gate.in"; exec 3<>"$scratch/gate.in"
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.out" &
gate_pid=$!; printf "BEGIN; SELECT id FROM boards WHERE id='%s' FOR UPDATE; SELECT 'board-ready';\n" "$signup_board" >&3
for attempt in $(seq 1 100); do if grep -q '^board-ready$' "$scratch/gate.out"; then break; fi; sleep 0.1; done
grep -q '^board-ready$' "$scratch/gate.out"
register > "$scratch/board-wait-status" & pending=$!; pids+=($pending)
for attempt in $(seq 1 100); do if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query ILIKE '%FROM boards%';")" -ge 1; then break; fi; sleep 0.1; done
test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query ILIKE '%FROM boards%';")" -ge 1
printf "UPDATE boards SET lifecycle_state='ARCHIVED' WHERE id='%s'; COMMIT;\n\\q\n" "$signup_board" >&3
exec 3>&-; wait "$gate_pid"; gate_pid=''; wait "$pending"; pids=()
test "$(cat "$scratch/board-wait-status")" = 400; test "$before" = "$(state)"
admin "UPDATE boards SET lifecycle_state='ACTIVE' WHERE id='$signup_board';" >/dev/null
echo 'Exact-image Board invitation consumers: both roles, signup/acceptance rollback, no early grants, role preservation, one-use proof, non-restoring retry and fresh post-Board-wait signup admission passed.'
test -n "${RUNNER_TEMP:-}"; test -n "${GITHUB_ENV:-}"
signup_fixtures="$RUNNER_TEMP/invitation-signup-fixtures.json"
printf '[]' > "$signup_fixtures"; chmod 600 "$signup_fixtures"
for width in 1280 390; do
  surface=INTERNAL; role=MEMBER; if test "$width" = 390; then surface=PORTAL; role=OWNER; fi
  email="invitation-browser-signup-${width}-${RANDOM}-${RANDOM}@example.test"; issue
  jq --argjson width "$width" --arg email "$email" --arg token "$token" --arg id "$invitation_id" --arg org "$org" --arg surface "$surface" \
    '. + [{width:$width,email:$email,password:"signup-correct-horse-battery",token:$token,id:$id,organizationId:$org,surface:$surface,organizationName:"Closed invitation signup"}]' "$signup_fixtures" > "$scratch/signup-next"
  cat "$scratch/signup-next" > "$signup_fixtures"
done
printf 'STRATAAI_E2E_INVITATION_SIGNUP_FIXTURES=%s\n' "$signup_fixtures" >> "$GITHUB_ENV"
echo 'Exact-image invitation signup: closed policy, both surfaces, one connection, identical concurrent receipts, atomic rollback, no premature access and fresh post-wait issuer/expiry admission passed.'
