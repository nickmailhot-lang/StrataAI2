#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable member event fixtures may run only in CI.' >&2; exit 1; }
base="${1:-http://127.0.0.1:8080}"
scratch=$(mktemp -d)
worker_changed=false
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test "$worker_changed" = true; then
    docker compose -f compose.release.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Board member event check failed at line $LINENO" >&2' ERR
key() { python3 -c 'import sys,uuid; sys.stdout.write(str(uuid.uuid4()))'; }
for actor in owner member; do
  data=$(jq -nc --arg email "member-event-$actor-$(key)@example.test" '{email:$email,password:"member-event-contract-horse",displayName:"Member event fixture"}')
  curl --max-time 30 --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$data" "$base/auth/register" > "$scratch/$actor.user"
  curl --max-time 30 --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$data" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); member=$(jq -r '.user.id' "$scratch/member.user")
request() {
  local match=(); if test -n "${6:-}"; then match=(-H "If-Match: $6"); fi
  curl --max-time 30 --silent --show-error -b "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $5" "${match[@]}" -X "$2" -d "$4" -o "$scratch/response" -w '%{http_code}' "$base$3"
}
test "$(request owner POST /organizations '{"name":"Member event release fixture"}' "$(key)")" = 201
org=$(jq -r '.organization.id' "$scratch/response")
test "$(request owner POST /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Member event release Board",visibility:"PRIVATE"}')" "$(key)")" = 201
board=$(jq -r '.id' "$scratch/response")
for id in "$owner" "$member" "$org" "$board"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');" >/dev/null
path="/boards/$board/members/$member"
effects() {
  admin "SELECT md5(jsonb_build_object(
   'member',(SELECT to_jsonb(m) FROM board_members m WHERE tenant_id='$org' AND board_id='$board' AND user_id='$member'),
   'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
   'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
   'stream',(SELECT last_sequence FROM work_event_streams WHERE tenant_id='$org' AND board_id='$board'),
   'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
   'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"
}
version=''
for transition in MEMBER:BOARD_MEMBER_ADDED ADMIN:BOARD_MEMBER_ROLE_CHANGED MEMBER:BOARD_MEMBER_ROLE_CHANGED MEMBER:BOARD_MEMBER_UPDATED; do
  role=${transition%%:*}; expected=${transition#*:}; retry=$(key)
  before=$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org';")
  test "$(request owner PATCH "$path" "$(jq -nc --arg role "$role" '{role:$role}')" "$retry" "$version")" = 200
  cp "$scratch/response" "$scratch/receipt"
  test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org';")" = "$((before + 1))"
  test "$(admin "SELECT count(*) FROM work_events e JOIN audit_events a ON a.tenant_id=e.tenant_id AND a.correlation_id=e.correlation_id
   AND a.actor_id=e.actor_id AND a.event_type=e.event_type AND a.entity_id=e.entity_id AND a.entity_type=e.entity_type
   WHERE e.tenant_id='$org' AND e.board_id='$board' AND e.event_type='$expected' AND e.actor_id='$owner'
   AND e.entity_type='Board' AND e.entity_id='$board' AND e.entity_version=1 AND e.sequence=$((before + 1));")" = 1
  saved=$(effects)
  test "$(request owner PATCH "$path" "$(jq -nc --arg role "$role" '{role:$role}')" "$retry" "$version")" = 200
  cmp "$scratch/receipt" "$scratch/response"; test "$saved" = "$(effects)"
  version=$(jq -r '.version' "$scratch/response")
done
test "$(request owner PATCH "$path" '{"role":"ADMIN"}' "$(key)" "$((version + 1))")" = 409
jq -e '.code=="version_conflict"' "$scratch/response" >/dev/null; test "$saved" = "$(effects)"
test "$(request member PATCH "$path" '{"role":"ADMIN"}' "$(key)" "$version")" = 404
jq -e '.code=="board_not_found"' "$scratch/response" >/dev/null; test "$saved" = "$(effects)"
export STRATAAI_TEST_EVENT_ORGANIZATION_ID="$org"
worker_changed=true
docker compose -f compose.release.yml -f scripts/ci/compose.work-event-test.yml up -d --force-recreate --wait --wait-timeout 180 worker >/dev/null
for attempt in $(seq 1 60); do
  ready=$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND ready_at IS NOT NULL;")
  if test "$ready" = 5; then break; fi
  sleep 1
done
test "$ready" = 5
curl --max-time 30 --fail --silent --show-error -b "$scratch/member.cookies" "$base/boards/$board/sync?since=0" > "$scratch/recovered"
jq -e --arg org "$org" --arg board "$board" '
 .cursor=="5" and .pending==false and .resetRequired==false and (.events|length)==5
 and ([.events[].eventId]|unique|length)==5 and all(.events[];.organizationId==$org and .boardId==$board)
 and all(.events[];.actorId==null and .metadata=={})
 and ([.events[].eventType]==["BOARD_CREATED","BOARD_MEMBER_ADDED","BOARD_MEMBER_ROLE_CHANGED","BOARD_MEMBER_ROLE_CHANGED","BOARD_MEMBER_UPDATED"])
' "$scratch/recovered" >/dev/null
curl --max-time 30 --fail --silent --show-error -b "$scratch/owner.cookies" "$base/boards/$board/sync?since=0" > "$scratch/admin-recovered"
jq -e --arg owner "$owner" --slurpfile member "$scratch/recovered" '
 all(.events[];.actorId==$owner) and ([.events[].eventId]==[$member[0].events[].eventId])
 and ([.events[].eventType]==[$member[0].events[].eventType]) and .cursor==$member[0].cursor
' "$scratch/admin-recovered" >/dev/null
for sequence in $(seq 1 5); do
  expected_id=$(admin "SELECT event_id FROM work_events WHERE tenant_id='$org' AND board_id='$board' AND sequence=$sequence;")
  jq -e --arg sequence "$sequence" --arg id "$expected_id" '.events[]|select(.sequence==$sequence)|.eventId==$id' "$scratch/recovered" >/dev/null
done
curl --max-time 30 --fail --silent --show-error -b "$scratch/member.cookies" "$base/boards/$board/sync?since=5" | jq -e '.cursor=="5" and .events==[] and .pending==false' >/dev/null
test "$(request owner DELETE "$path" '{}' "$(key)" "$version")" = 204
test "$(curl --max-time 30 --silent --show-error -b "$scratch/member.cookies" -o "$scratch/withdrawn" -w '%{http_code}' "$base/boards/$board/sync?since=5")" = 404
jq -e '.code=="board_not_found" and (has("events")|not)' "$scratch/withdrawn" >/dev/null
test "$(admin "SELECT count(*) FROM organization_members WHERE tenant_id='$org' AND user_id='$member' AND status='ACTIVE' AND role='MEMBER';")" = 1
echo 'Board member events: exact HTTP receipts, audit/source identity, real Worker readiness, canonical client recovery and current membership withdrawal passed.'
