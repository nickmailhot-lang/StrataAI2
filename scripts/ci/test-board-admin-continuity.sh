#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable Board admin fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8080
scratch=$(mktemp -d)
pids=()
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; done
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null || true
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Board administrator continuity failed at line $LINENO" >&2' ERR
for actor in owner first second; do
  data=$(jq -nc --arg email "board-admin-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"board-admin-continuity-horse",displayName:"Board admin fixture"}')
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$data" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$data" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user")
first=$(jq -r '.user.id' "$scratch/first.user")
second=$(jq -r '.user.id' "$scratch/second.user")
request() {
  curl --max-time 30 --silent --show-error -b "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $5" -X "$2" -d "$4" -o "$scratch/$1.response" -w '%{http_code}' "$base$3"
}
key() { cat /proc/sys/kernel/random/uuid; }
test "$(request owner POST /organizations '{"name":"Exact Board admin continuity"}' "$(key)")" = 201
org=$(jq -r '.organization.id' "$scratch/owner.response")
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$first','MEMBER','ACTIVE'),(gen_random_uuid(),'$org','$second','MEMBER','ACTIVE');" >/dev/null
test "$(request owner POST /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Exact admin board",visibility:"PRIVATE"}')" "$(key)")" = 201
board=$(jq -r '.id' "$scratch/owner.response")
for user in "$first" "$second"; do test "$(request owner PATCH "/boards/$board/members/$user" '{"role":"ADMIN"}' "$(key)")" = 200; done
test "$(request owner DELETE "/boards/$board/members/$owner" '{}' "$(key)")" = 204
for actor in first second; do
  user=$first; if test "$actor" = second; then user=$second; fi
  request "$actor" PATCH "/boards/$board/members/$user" '{"role":"MEMBER"}' "$(key)" > "$scratch/$actor.status" & pids+=($!)
done
for pid in "${pids[@]}"; do wait "$pid"; done
pids=()
test "$(cat "$scratch/first.status" "$scratch/second.status" | grep -o 200 | wc -l)" = 1
test "$(cat "$scratch/first.status" "$scratch/second.status" | grep -o 409 | wc -l)" = 1
test "$(admin "SELECT count(*) FROM board_members WHERE board_id='$board' AND status='ACTIVE' AND role='ADMIN'")" = 1
survivor=$(admin "SELECT user_id FROM board_members WHERE board_id='$board' AND status='ACTIVE' AND role='ADMIN'")
actor=first; if test "$survivor" = "$second"; then actor=second; fi
state() {
  admin "SELECT jsonb_build_object('members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY user_id) FROM board_members m WHERE board_id='$board'),
    'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
    'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
    'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
    'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text;"
}
before=$(state); denied_key=$(key)
for _ in 1 2; do
  test "$(request "$actor" PATCH "/boards/$board/members/$survivor" '{"role":"MEMBER"}' "$denied_key")" = 409
  jq -e '.code=="sole_board_admin"' "$scratch/$actor.response" >/dev/null
  test "$before" = "$(state)"
done
# Preserve the existing explicit Organization-admin override, atomically.
override_key=$(key)
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "/boards/$board/members/$survivor" '{"role":"MEMBER"}' "$override_key")" = 503
test "$before" = "$(state)"
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$(request owner PATCH "/boards/$board/members/$survivor" '{"role":"MEMBER"}' "$override_key")" = 200
test "$(admin "SELECT count(*) FROM board_members WHERE board_id='$board' AND status='ACTIVE' AND role='ADMIN'")" = 0
test "$(request owner PATCH "/boards/$board/members/$survivor" '{"role":"ADMIN"}' "$(key)")" = 200
echo 'Exact-image Board admin continuity: concurrent self-demotions leave one admin, keyed rejection retains state, audit failure rolls back override, and current Organization-admin recovery remains available.'

# Separate disposable directory fixtures keep the continuity assertions above
# independent of paging and use real user/Organization/Board foreign keys.
test "$(request owner POST /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Paged member directory",visibility:"PRIVATE"}')" "$(key)")" = 201
page_board=$(jq -r '.id' "$scratch/owner.response")
admin "WITH fixture AS (
 SELECT gen_random_uuid() AS id FROM generate_series(1,52)
), inserted_users AS (
 INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 SELECT f.id,'board-page-'||f.id||'@example.test',upper('board-page-'||f.id||'@example.test'),
 'Directory fixture','ACTIVE',true,u.password_hash,now(),now() FROM fixture f CROSS JOIN users u WHERE u.id='$owner'
 RETURNING id
), inserted_members AS (
 INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),'$org',id,'MEMBER','ACTIVE' FROM inserted_users RETURNING user_id
)
INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$page_board',user_id,'MEMBER','ACTIVE',now(),now() FROM inserted_members;" >/dev/null
before_page=$(state)
test "$(curl --max-time 30 --silent --show-error -b "$scratch/owner.cookies" -H "Idempotency-Key: $(key)" -D "$scratch/page.headers" -o "$scratch/page.first" -w '%{http_code}' "$base/boards/$page_board/members")" = 200
jq -e 'length==50' "$scratch/page.first" >/dev/null
cursor=$(awk 'tolower($1)=="x-strataai-next-cursor:" {gsub("\r", "", $2); print $2}' "$scratch/page.headers")
test -n "$cursor"; test "$cursor" = "$(jq -r '.[49].userId' "$scratch/page.first")"
test "$(curl --max-time 30 --silent --show-error -b "$scratch/owner.cookies" -D "$scratch/page.headers" -o "$scratch/page.second" -w '%{http_code}' "$base/boards/$page_board/members?after=$cursor")" = 200
jq -e 'length==3' "$scratch/page.second" >/dev/null
! grep -qi '^X-StrataAI-Next-Cursor:' "$scratch/page.headers"
jq -s -e 'add | map(.userId) | length==53 and length==(unique|length) and .==sort' "$scratch/page.first" "$scratch/page.second" >/dev/null
jq -s -e 'add | all(.organizationMemberActive==true and (.displayName|type)=="string" and (.email|type)=="string" and (has("passwordHash")|not))' "$scratch/page.first" "$scratch/page.second" >/dev/null
test "$(curl --max-time 30 --silent --show-error -b "$scratch/owner.cookies" -o "$scratch/page.invalid" -w '%{http_code}' "$base/boards/$page_board/members?after=not-a-uuid")" = 400
jq -e '.code=="invalid_board_member_cursor"' "$scratch/page.invalid" >/dev/null
test "$before_page" = "$(state)"
former=$(jq -r '[.[] | select(.displayName=="Directory fixture")][0].userId' "$scratch/page.first")
test -n "$former"; test "$former" != null
admin "UPDATE organization_members SET status='REMOVED' WHERE tenant_id='$org' AND user_id='$former';" >/dev/null
test "$(curl --max-time 30 --silent --show-error -b "$scratch/owner.cookies" -o "$scratch/page.former" -w '%{http_code}' "$base/boards/$page_board/members")" = 200
jq -e --arg user "$former" 'map(select(.userId==$user)) | length==1 and .[0].organizationMemberActive==false and .[0].displayName==null and .[0].email==null' "$scratch/page.former" >/dev/null
test "$before_page" = "$(state)"
echo 'Exact-image Board member directory: real FK fixtures, bounded seek, complete ordered pages, terminal cursor absence, invalid cursor and no read audit/event/job/replay writes passed.'
