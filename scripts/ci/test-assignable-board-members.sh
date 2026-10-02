#!/usr/bin/env bash
set -euo pipefail
# PRD-11: admitted assignment choices through the exact release web/API images.
test "${CI:-}" = true || { echo 'Disposable assignment fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d); gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Assignable member check failed at line $LINENO" >&2' ERR
for actor in owner member outsider; do
  jq -nc --arg email "assignable-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"assignment-correct-horse-battery",displayName:"Assignment fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); member=$(jq -r '.user.id' "$scratch/member.user")
org=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Assignment directory"}' "$base/organizations" | jq -r '.organization.id')
board=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Assignment Board",visibility:"PRIVATE"}')" "$base/boards" | jq -r '.id')
for id in "$owner" "$member" "$org" "$board"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 VALUES(gen_random_uuid(),'$org','$board','$member','MEMBER','ACTIVE',now(),now());
 WITH seed AS (SELECT gen_random_uuid() id,ordinal FROM generate_series(1,53) AS s(ordinal))
 INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 SELECT id,'assignment-seed-'||id||'@example.test',upper('assignment-seed-'||id||'@example.test'),
 'Assignment seeded '||ordinal,CASE WHEN ordinal=51 THEN 'DEACTIVATED' ELSE 'ACTIVE' END,true,'unused-assignment-fixture-hash',now(),now() FROM seed;
 INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),'$org',id,'MEMBER',CASE WHEN display_name='Assignment seeded 52' THEN 'REMOVED' ELSE 'ACTIVE' END
 FROM users WHERE password_hash='unused-assignment-fixture-hash';
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board',id,'MEMBER',CASE WHEN display_name='Assignment seeded 53' THEN 'REMOVED' ELSE 'ACTIVE' END,now(),now()
 FROM users WHERE password_hash='unused-assignment-fixture-hash';" >/dev/null
get() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/${3:-response}.json" -w '%{http_code}' "$base$2"; }
path="/boards/$board/assignable-members"
test "$(get member "$path" first)" = 200
jq -e --arg org "$org" --arg board "$board" '.organizationId==$org and .boardId==$board and (.items|length)==50 and .nextCursor==.items[-1].userId' "$scratch/first.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/first.json")
test "$(get member "$path?after=$cursor" second)" = 200
jq -e '.nextCursor==null and (.items|length)==2' "$scratch/second.json" >/dev/null
jq -se '([.[].items[].userId]|unique|length)==52 and all(.[].items[]; (keys|sort)==["displayName","userId"])
 and all(.[].items[];.displayName!="Assignment seeded 51" and .displayName!="Assignment seeded 52" and .displayName!="Assignment seeded 53")' "$scratch/first.json" "$scratch/second.json" >/dev/null
test "$(get member "$path?after=bad")" = 400
test "$(get outsider "$path?after=bad")" = 404
admin "UPDATE boards SET visibility='PUBLIC' WHERE id='$board';" >/dev/null
test "$(get outsider "$path")" = 404
admin "UPDATE boards SET visibility='PRIVATE' WHERE id='$board';" >/dev/null
hold() {
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 & gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\nSELECT id FROM boards WHERE id=\047%s\047 FOR UPDATE;\n\\echo assignment_locked\n' "$board" >&3
  for ((attempt=0;attempt<100;attempt++)); do if grep -q '^assignment_locked$' "$scratch/gate.log"; then return; fi; kill -0 "$gate_pid" || return 1; sleep 0.05; done
  return 1
}
blocked() {
  for ((attempt=0;attempt<100;attempt++)); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%FOR UPDATE%';")" -ge 1; then return; fi
    sleep 0.05
  done
  echo 'Expected assignment directory Board lock wait was not observed.' >&2; return 1
}
release() { printf '%s\nCOMMIT;\n\\q\n' "$1" >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; rm "$scratch/gate.in" "$scratch/gate.log"; }
hold; get member "$path" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh '"items"|Assignment seeded|displayName' "$scratch/response.json"
test "$(get owner "$path" removed)" = 200
jq -e --arg member "$member" 'all(.items[];.userId!=$member)' "$scratch/removed.json" >/dev/null
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
hold; get member "$path" > "$scratch/status" & request_pid=$!
blocked; release "DELETE FROM sessions WHERE user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
scripts/ci/assert-file-excludes.sh '"items"|Assignment seeded|displayName' "$scratch/response.json"
admin "UPDATE boards SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$board';" >/dev/null
test "$(get owner "$path")" = 404
echo 'Assignable Board members: scoped minimal profiles, eligibility, 50+2 pages and post-wait revocation passed.'
