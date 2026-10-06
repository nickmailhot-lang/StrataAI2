#!/usr/bin/env bash
set -euo pipefail
# PRD-03-TC-04/05/08: exact-image restricted-role member discovery.
test "${CI:-}" = true || { echo 'Disposable member fixtures may run only in CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"
scratch="$(mktemp -d)"; gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml up -d --wait --wait-timeout 180 api >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Member directory check failed at line $LINENO" >&2' ERR
docker compose -f compose.release.yml -f scripts/ci/compose.auth-test.yml -f scripts/ci/compose.atomic-test.yml up -d --wait --wait-timeout 180 api >/dev/null
login() { curl --fail --silent --show-error -c "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$1.credentials")" "$base/auth/login" >/dev/null; }
for actor in owner member portal; do
  jq -nc --arg email "directory-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"directory-correct-horse-battery",displayName:"Directory fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  login "$actor"
done
owner="$(jq -r '.user.id' "$scratch/owner.user")"; member="$(jq -r '.user.id' "$scratch/member.user")"; portal="$(jq -r '.user.id' "$scratch/portal.user")"
org="$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Bounded member directory"}' "$base/organizations" | jq -r '.organization.id')"
foreign="$(curl --fail --silent --show-error -b "$scratch/portal.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Other private directory"}' "$base/organizations" | jq -r '.organization.id')"
for id in "$owner" "$member" "$portal" "$org" "$foreign"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');
  INSERT INTO portal_access(id,tenant_id,user_id,status,relationship_type) VALUES(gen_random_uuid(),'$org','$portal','ACTIVE','OWNER');
  WITH seed AS (SELECT gen_random_uuid() id,ordinal FROM generate_series(1,66) AS seed_index(ordinal))
  INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
  SELECT id,'directory-seed-'||id||'@example.test',upper('directory-seed-'||id||'@example.test'),'Directory seeded member',
      CASE WHEN ordinal=1 THEN 'DEACTIVATED' ELSE 'ACTIVE' END,true,'unused-directory-fixture-hash',now(),now() FROM seed;
  INSERT INTO organization_members(id,tenant_id,user_id,role,status)
  SELECT gen_random_uuid(),'$org',id,CASE WHEN status='DEACTIVATED' THEN 'OWNER' ELSE 'MEMBER' END,'ACTIVE'
  FROM users WHERE password_hash='unused-directory-fixture-hash';" >/dev/null
get() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/${3:-response}.json" -w '%{http_code}' "$base$2"; }
test "$(get owner "/organizations/$org" metadata)" = 200
jq -e --arg org "$org" '.organization.id==$org and .organization.name=="Bounded member directory" and .organization.version==1 and .role==0' "$scratch/metadata.json" >/dev/null
test "$(get member "/organizations/$org" metadata)" = 200
jq -e '.role==2' "$scratch/metadata.json" >/dev/null
test "$(get portal "/organizations/$org" denied)" = 404
scripts/ci/assert-file-excludes.sh 'Bounded member directory|ownerUserId|createdAt' "$scratch/denied.json"
test "$(get owner "/organizations/$foreign" denied)" = 404
scripts/ci/assert-file-excludes.sh 'Other private directory|ownerUserId|createdAt' "$scratch/denied.json"
test "$(get owner '/organizations/00000000-0000-0000-0000-000000000000')" = 404
test "$(get owner "/organizations/$org/members" first)" = 200
jq -e --arg org "$org" '.organizationId==$org and (.items|length)==50 and .nextCursor==.items[-1].userId' "$scratch/first.json" >/dev/null
cursor="$(jq -r '.nextCursor' "$scratch/first.json")"
test "$(get owner "/organizations/$org/members?after=$cursor" second)" = 200
jq -e '.nextCursor==null and (.items|length)==18' "$scratch/second.json" >/dev/null
jq -s '[.[].items[]]' "$scratch/first.json" "$scratch/second.json" > "$scratch/all.json"
jq -e --arg portal "$portal" 'length==68 and ([.[].userId]|length)==([.[].userId]|unique|length)
  and ([.[].userId]==([.[].userId]|sort)) and all(.[];.userId!=$portal)
  and ([.[]|select(.accountStatus=="DEACTIVATED" and .role==0 and .isUsableOwner==false)]|length)==1
  and ([.[]|select(.isUsableOwner)]|length)==1' "$scratch/all.json" >/dev/null
admin "SELECT user_id FROM organization_members WHERE tenant_id='$org' AND status='ACTIVE' ORDER BY user_id;" > "$scratch/expected.ids"
jq -r '.[].userId' "$scratch/all.json" > "$scratch/actual.ids"
cmp "$scratch/expected.ids" "$scratch/actual.ids"
test "$(get owner "/organizations/$org/members/$member" exact)" = 200
jq -e --arg org "$org" --arg member "$member" '.organizationId==$org and .actorRole==0 and .member.userId==$member and .member.role==2' "$scratch/exact.json" >/dev/null
test "$(get owner "/organizations/$org/members/$portal" absent)" = 200
jq -e '.member==null' "$scratch/absent.json" >/dev/null
for actor in member portal; do test "$(get "$actor" "/organizations/$org/members/$member")" = 404; done
test "$(get owner "/organizations/$foreign/members/$member")" = 404
scripts/ci/assert-file-excludes.sh 'passwordHash|tokenHash|emailNormalized|Other private directory' "$scratch/all.json"
for actor in member portal; do test "$(get "$actor" "/organizations/$org/members")" = 404; done
test "$(get owner "/organizations/$foreign/members")" = 404
test "$(get owner "/organizations/$org/members?after=bad")" = 400
test "$(get owner "/organizations/$org/members?after=00000000-0000-0000-0000-000000000000")" = 400
audits="$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org';")"
hold() {
  rm -f "$scratch/gate.in" "$scratch/gate.log"; mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 &
  gate_pid=$!; exec 3> "$scratch/gate.in"; printf 'BEGIN;\n%s\n\\echo directory_locked\n' "$1" >&3
  for ((attempt=0;attempt<100;attempt++)); do grep -q '^directory_locked$' "$scratch/gate.log" && return; sleep 0.05; done
  return 1
}
blocked() {
  local count
  for ((attempt=0;attempt<100;attempt++)); do
    count="$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '$1';")" || return 1
    [[ "$count" =~ ^[0-9]+$ ]] || return 1
    ((count > 0)) && return
    sleep 0.05
  done
  return 1
}
release() { printf '%s\nCOMMIT;\n\\q\n' "$1" >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; }
# ARCH-02-AC-003: current surface admission against the restricted exact API.
# These reads must not manufacture a membership or alter a separate Portal grant.
surface_state() { admin "SELECT json_build_object(
  'members',(SELECT md5(coalesce(string_agg(row_to_json(m)::text,',' ORDER BY m.id),'')) FROM organization_members m WHERE tenant_id='$org'),
  'portal',(SELECT md5(coalesce(string_agg(row_to_json(p)::text,',' ORDER BY p.id),'')) FROM portal_access p WHERE tenant_id='$org'),
  'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
  'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
  'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
  'identity_events',(SELECT count(*) FROM identity_events WHERE user_id IN ('$owner','$member','$portal')))::text;"; }
surface_before="$(surface_state)"
for actor in owner member; do
  test "$(get "$actor" "/organizations/$org/surface-access?surface=INTERNAL" admission)" = 200
  jq -e --arg org "$org" 'keys==["organizationId","surface"] and .organizationId==$org and .surface=="INTERNAL"' "$scratch/admission.json" >/dev/null
  test "$(get "$actor" "/organizations/$org/surface-access?surface=PORTAL")" = 404
done
test "$(get portal "/organizations/$org/surface-access?surface=PORTAL" admission)" = 200
jq -e --arg org "$org" 'keys==["organizationId","surface"] and .organizationId==$org and .surface=="PORTAL"' "$scratch/admission.json" >/dev/null
test "$(get portal "/organizations/$org/surface-access?surface=INTERNAL" denied)" = 404
test "$(get owner "/organizations/$foreign/surface-access?surface=PORTAL" denied)" = 404
scripts/ci/assert-file-excludes.sh 'Bounded member directory|Other private directory|Directory fixture|directory-' "$scratch/denied.json"
test "$(get owner "/organizations/$org/surface-access?surface=INVALID")" = 400
jq -e '.code=="invalid_access_surface"' "$scratch/response.json" >/dev/null
for surface in INTERNAL PORTAL; do
  test "$(get owner "/organizations/00000000-0000-0000-0000-000000000000/surface-access?surface=$surface")" = 404
  jq -e '.code=="organization_not_found"' "$scratch/response.json" >/dev/null
done
test "$(surface_state)" = "$surface_before"

# A committed Portal revocation wins after the dedicated grant-row lock wait.
hold "SELECT id FROM portal_access WHERE tenant_id='$org' AND user_id='$portal' FOR UPDATE;"
get portal "/organizations/$org/surface-access?surface=PORTAL" portal-revoked > "$scratch/status" & request_pid=$!
blocked '%SELECT id FROM portal_access%FOR SHARE%'
release "UPDATE portal_access SET status='REVOKED',version=version+1 WHERE tenant_id='$org' AND user_id='$portal';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
jq -e '.code=="organization_not_found"' "$scratch/portal-revoked.json" >/dev/null
scripts/ci/assert-file-excludes.sh 'Bounded member directory|Directory fixture|directory-' "$scratch/portal-revoked.json"
admin "UPDATE portal_access SET status='ACTIVE',version=version+1 WHERE tenant_id='$org' AND user_id='$portal';" >/dev/null

# Internal membership loss does not become an implicit Portal grant.
hold "SELECT user_id FROM organization_members WHERE tenant_id='$org' AND user_id='$member' FOR UPDATE;"
get member "/organizations/$org/surface-access?surface=INTERNAL" member-revoked > "$scratch/status" & request_pid=$!
blocked '%SELECT user_id FROM organization_members%FOR UPDATE%'
release "UPDATE organization_members SET status='REMOVED',version=version+1 WHERE tenant_id='$org' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
test "$(get member "/organizations/$org/surface-access?surface=PORTAL")" = 404
admin "UPDATE organization_members SET status='ACTIVE',version=version+1 WHERE tenant_id='$org' AND user_id='$member';" >/dev/null
# Later removal-consent assertions must use the newly current member revision.
test "$(get owner "/organizations/$org/members" first)" = 200
cursor="$(jq -r '.nextCursor' "$scratch/first.json")"
test "$(get owner "/organizations/$org/members?after=$cursor" second)" = 200
jq -s '[.[].items[]]' "$scratch/first.json" "$scratch/second.json" > "$scratch/all.json"

# Session revocation committed while waiting for the parent invalidates admission.
for entry in 'owner INTERNAL' 'portal PORTAL'; do
  read -r actor surface <<< "$entry"
  hold "SELECT id FROM organizations WHERE id='$org' FOR UPDATE;"
  get "$actor" "/organizations/$org/surface-access?surface=$surface" surface-session > "$scratch/status" & request_pid=$!
  blocked '%SELECT id FROM organizations%FOR UPDATE%'
  if test "$actor" = owner; then actor_id="$owner"; else actor_id="$portal"; fi
  release "UPDATE sessions SET revoked_at=now() WHERE user_id='$actor_id' AND revoked_at IS NULL;"
  wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
  login "$actor"
done
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org';")" = "$audits"
echo 'Exact-image surface admission: independent minimal grants, unchanged read state, post-wait Portal/internal revocation and current session checks passed.'
# A role change committed during the parent wait invalidates removal consent.
member_version="$(jq -r --arg member "$member" '.[]|select(.userId==$member)|.version' "$scratch/all.json")"
[[ "$member_version" =~ ^[1-9][0-9]*$ ]]
hold "SELECT id FROM organizations WHERE id='$org' FOR UPDATE;"
curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -X DELETE \
  -o "$scratch/stale-removal.json" -w '%{http_code}' "$base/organizations/$org/members/$member?expectedVersion=$member_version" > "$scratch/status" & request_pid=$!
blocked '%SELECT id FROM organizations%FOR UPDATE%'
release "UPDATE organization_members SET role='ADMIN',version=version+1 WHERE tenant_id='$org' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 409
jq -e '.code=="member_version_conflict"' "$scratch/stale-removal.json" >/dev/null
test "$(admin "SELECT role||':'||status||':'||version FROM organization_members WHERE tenant_id='$org' AND user_id='$member';")" = "ADMIN:ACTIVE:$((member_version+1))"
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org';")" = "$audits"
# A committed demotion wins while the directory request waits for actor membership.
for path in "/organizations/$org/members" "/organizations/$org/members/$member"; do
hold "SELECT user_id FROM organization_members WHERE tenant_id='$org' AND user_id='$owner' FOR UPDATE;"
get owner "$path" denied > "$scratch/status" & request_pid=$!
blocked '%SELECT user_id FROM organization_members%FOR UPDATE%'
release "UPDATE organization_members SET role='MEMBER',version=version+1 WHERE tenant_id='$org' AND user_id='$owner';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh 'Directory seeded member|directory-seed-|Bounded member directory' "$scratch/denied.json"
admin "UPDATE organization_members SET role='OWNER',version=version+1 WHERE tenant_id='$org' AND user_id='$owner';" >/dev/null
done
# The original session revoked during a parent wait cannot authorize disclosure.
for path in "/organizations/$org" "/organizations/$org/members" "/organizations/$org/members/$member"; do
hold "SELECT id FROM organizations WHERE id='$org' FOR UPDATE;"
get owner "$path" revoked > "$scratch/status" & request_pid=$!
blocked '%SELECT id FROM organizations%FOR UPDATE%'
release "UPDATE sessions SET revoked_at=now() WHERE user_id='$owner' AND revoked_at IS NULL;"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
login owner
done
test "$(get owner "/organizations/$org/members")" = 200
test "$(get owner "/organizations/$org/members/$member" exact)" = 200
jq -e '.member.role==1' "$scratch/exact.json" >/dev/null
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org';")" = "$audits"
echo 'Exact-image member directory: one-connection paging, scoped profiles, Portal separation, historical ownership, post-wait stale removal consent rejection, fresh role/session admission and read-only audit behavior passed.'
