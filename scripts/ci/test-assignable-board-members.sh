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
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null || true
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
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -d "$5" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
list=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Assignment commands"}' "$base/boards/$board/lists" | jq -r '.id')
card=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"title":"Retained assignment Card","description":"Retained assignment description"}' "$base/lists/$list/cards" | jq -r '.id')
for id in "$list" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
member_path="/cards/$card/members/$member"
key=11111111-1111-1111-1111-111111111141
state() { admin "SELECT md5(jsonb_build_object(
 'board_members',(SELECT jsonb_agg(to_jsonb(b) ORDER BY board_id,user_id) FROM board_members b WHERE tenant_id='$org'),
 'organization_members',(SELECT jsonb_agg(to_jsonb(o) ORDER BY user_id) FROM organization_members o WHERE tenant_id='$org'),
 'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY card_id,user_id) FROM card_members m WHERE tenant_id='$org'),
 'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
before=$(state)
test "$(request outsider PUT "$member_path?version=1" "$key" '{}')" = 404
test "$before" = "$(state)"
test "$(request owner PUT "$member_path?version=0" "$key" '{}')" = 400
jq -e '.code=="invalid_card_member_version"' "$scratch/response.json" >/dev/null
test "$before" = "$(state)"
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner PUT "$member_path?version=1" "$key" '{}')" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request owner PUT "$member_path?version=1" "$key" '{}')" = 200
jq -e --arg member "$member" --arg card "$card" '.userId==$member and .assigned==true and .changed==true and .card.id==$card and .card.version==2' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/assignment-receipt.json"
test "$(admin "SELECT assigned_by='$owner' AND version=1 AND created_at=updated_at FROM card_members WHERE tenant_id='$org' AND card_id='$card' AND user_id='$member';")" = t
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id='$card' AND event_type='CARD_MEMBER_ADDED';")" = 1
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND entity_id='$card' AND event_type='CARD_MEMBER_ADDED';")" = 1
after=$(state)
test "$(request owner PUT "$member_path?version=1" "$key" '{}')" = 200
cmp "$scratch/response.json" "$scratch/assignment-receipt.json"; test "$after" = "$(state)"
test "$(request owner PUT "/cards/$card/members/$owner?version=2" 11111111-1111-1111-1111-111111111142 '{}')" = 200
jq -e '.card.version==3 and .changed==true' "$scratch/response.json" >/dev/null
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND card_id='$card';")" = 2
test "$(get owner "/cards/$card/members")" = 200
jq -e --arg card "$card" --arg owner "$owner" '.cardId==$card and .cardVersion==3 and .canEdit==true and (.items|length)==2 and .nextCursor==null
 and all(.items[];.assignedBy==$owner and (keys|sort)==["assignedAt","assignedBy","displayName","userId"])' "$scratch/response.json" >/dev/null
test "$(get outsider "/cards/$card/members?after=bad")" = 404
test "$(get owner "/cards/$card/members?after=bad")" = 400
test "$(get owner "/cards/$card/member-options" options-first)" = 200
jq -e --arg org "$org" --arg board "$board" --arg card "$card" '.organizationId==$org and .boardId==$board and .cardId==$card and .cardVersion==3
 and (.items|length)==50 and .nextCursor==.items[-1].userId' "$scratch/options-first.json" >/dev/null
options_cursor=$(jq -r '.nextCursor' "$scratch/options-first.json")
test "$(get member "/cards/$card/member-options?after=$options_cursor" options-second)" = 200
jq -e '(.items|length)==2 and .nextCursor==null and .cardVersion==3' "$scratch/options-second.json" >/dev/null
jq -se --arg owner "$owner" --arg member "$member" '([.[].items[].userId]|unique|length)==52
 and all(.[].items[]; (keys|sort)==["assigned","displayName","userId"] and .assigned==(.userId==$owner or .userId==$member))
 and all(.[].items[];.displayName!="Assignment seeded 51" and .displayName!="Assignment seeded 52" and .displayName!="Assignment seeded 53")' "$scratch/options-first.json" "$scratch/options-second.json" >/dev/null
test "$(get outsider "/cards/$card/member-options?after=bad")" = 404
test "$(get owner "/cards/$card/member-options?after=bad")" = 400
paged_card=$(admin 'SELECT gen_random_uuid();')
admin "INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,version,created_at,updated_at)
 VALUES('$paged_card','$org','$board','$list','Paged assignee Card','700000000000000000000000000000',53,now(),now());
 INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by)
 SELECT m.tenant_id,m.board_id,'$paged_card',m.user_id,'$owner' FROM board_members m
 JOIN organization_members o ON o.tenant_id=m.tenant_id AND o.user_id=m.user_id JOIN users u ON u.id=m.user_id
 WHERE m.tenant_id='$org' AND m.board_id='$board' AND m.status='ACTIVE' AND o.status='ACTIVE' AND u.status='ACTIVE';" >/dev/null
test "$(get owner "/cards/$paged_card/members" card-first)" = 200
jq -e '.cardVersion==53 and (.items|length)==50 and .nextCursor==.items[-1].userId' "$scratch/card-first.json" >/dev/null
assignee_cursor=$(jq -r '.nextCursor' "$scratch/card-first.json")
test "$(get owner "/cards/$paged_card/members?after=$assignee_cursor" card-second)" = 200
jq -e '(.items|length)==2 and .nextCursor==null' "$scratch/card-second.json" >/dev/null
jq -se '([.[].items[].userId]|length)==52 and ([.[].items[].userId]|unique|length)==52' "$scratch/card-first.json" "$scratch/card-second.json" >/dev/null
test "$(get owner "/boards/$board" member-preview)" = 200
jq -e --arg card "$card" --arg paged "$paged_card" '.cardMembers[$card].total==2 and .cardMembers[$card].cardVersion==3 and (.cardMembers[$card].items|length)==2
 and .cardMembers[$paged].total==52 and .cardMembers[$paged].cardVersion==53 and (.cardMembers[$paged].items|length)==6
 and all(.cardMembers[].items[];(keys|sort)==["displayName","userId"])
 and all(.cardMembers[].items[];.displayName!="Assignment seeded 51" and .displayName!="Assignment seeded 52" and .displayName!="Assignment seeded 53")' "$scratch/member-preview.json" >/dev/null
admin "UPDATE boards SET visibility='PUBLIC' WHERE id='$board';" >/dev/null
test "$(get outsider "/boards/$board" public-preview)" = 200
jq -e '.cardMembers==null' "$scratch/public-preview.json" >/dev/null
curl --max-time 60 --fail --silent --show-error "$base/boards/$board" > "$scratch/anonymous-preview.json"
jq -e '.cardMembers==null' "$scratch/anonymous-preview.json" >/dev/null
admin "UPDATE boards SET visibility='PRIVATE' WHERE id='$board';" >/dev/null
beyond_preview=$(admin "SELECT m.user_id FROM board_members m JOIN users u ON u.id=m.user_id
 WHERE m.tenant_id='$org' AND m.board_id='$board' AND m.status='ACTIVE' AND u.status='ACTIVE'
 AND u.display_name LIKE 'Assignment seeded %' AND u.display_name NOT IN ('Assignment seeded 52','Assignment seeded 53') ORDER BY m.user_id DESC LIMIT 1;")
test "$(get owner "/boards/$board/cards?members=$beyond_preview" beyond-preview-filter)" = 200
jq -e --arg card "$paged_card" '(.items|length)==1 and .items[0].id==$card' "$scratch/beyond-preview-filter.json" >/dev/null
test "$(get owner "/boards/$board/cards?members=$owner,$member&match=all" member-all)" = 200
jq -e '(.items|length)==2' "$scratch/member-all.json" >/dev/null
test "$(get owner "/boards/$board/cards?members=$member&keyword=absent&match=all")" = 200
jq -e '(.items|length)==0' "$scratch/response.json" >/dev/null
test "$(get owner "/boards/$board/cards?members=$member&keyword=absent&match=any")" = 200
jq -e '(.items|length)==2' "$scratch/response.json" >/dev/null
test "$(get owner "/boards/$board/cards?members=$owner,$owner")" = 400
test "$(get outsider "/boards/$board/cards?members=bad")" = 404
admin "UPDATE boards SET visibility='PUBLIC' WHERE id='$board';" >/dev/null
test "$(get outsider "/boards/$board/cards?members=$owner")" = 404
scripts/ci/assert-file-excludes.sh '"items"|Retained assignment|Paged assignee' "$scratch/response.json"
admin "UPDATE boards SET visibility='PRIVATE' WHERE id='$board';
 WITH seed AS (
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,version,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board','$list','Member filter paging '||n,lpad(n::text,30,'0'),2,now(),now() FROM generate_series(1,52) n RETURNING id
 ) INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by) SELECT '$org','$board',id,'$owner','$owner' FROM seed;" >/dev/null
test "$(get owner "/boards/$board/cards?members=$owner&keyword=Member%20filter%20paging&match=all" member-filter-first)" = 200
jq -e '(.items|length)==50 and .nextCursor==.items[-1].id' "$scratch/member-filter-first.json" >/dev/null
member_filter_cursor=$(jq -r '.nextCursor' "$scratch/member-filter-first.json")
test "$(get owner "/boards/$board/cards?members=$owner&keyword=Member%20filter%20paging&match=all&after=$member_filter_cursor" member-filter-second)" = 200
jq -e '(.items|length)==2 and .nextCursor==null' "$scratch/member-filter-second.json" >/dev/null
jq -se '([.[].items[].id]|length)==52 and ([.[].items[].id]|unique|length)==52' "$scratch/member-filter-first.json" "$scratch/member-filter-second.json" >/dev/null
after=$(state)
test "$(request owner DELETE "$member_path?version=2" 11111111-1111-1111-1111-111111111143 '{}')" = 409
test "$after" = "$(state)"
test "$(request owner PUT "$member_path?version=3" 11111111-1111-1111-1111-111111111144 '{}')" = 200
jq -e '.card.version==3 and .changed==false' "$scratch/response.json" >/dev/null
test "$(request owner DELETE "$member_path?version=3" 11111111-1111-1111-1111-111111111145 '{}')" = 200
jq -e '.card.version==4 and .changed==true and .assigned==false and .card.title=="Retained assignment Card" and .card.description=="Retained assignment description"' "$scratch/response.json" >/dev/null
test "$(get owner "/cards/$card/member-options" options-removed-first)" = 200
options_cursor=$(jq -r '.nextCursor' "$scratch/options-removed-first.json")
test "$(get owner "/cards/$card/member-options?after=$options_cursor" options-removed-second)" = 200
jq -se --arg owner "$owner" --arg member "$member" 'all(.[];.cardVersion==4) and all(.[].items[];.assigned==(.userId==$owner))
 and any(.[].items[];.userId==$member and .assigned==false)' "$scratch/options-removed-first.json" "$scratch/options-removed-second.json" >/dev/null
test "$(get owner "/boards/$board" removed-preview)" = 200
jq -e --arg card "$card" --arg owner "$owner" '.cardMembers[$card].total==1 and .cardMembers[$card].cardVersion==4 and .cardMembers[$card].items[0].userId==$owner' "$scratch/removed-preview.json" >/dev/null
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND card_id='$card';")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id='$card' AND event_type='CARD_MEMBER_REMOVED';")" = 1
test "$(request owner PUT "$member_path?version=4" 11111111-1111-1111-1111-111111111146 '{}')" = 200
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
hold; get member "/boards/$board/cards?members=$owner" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh '"items"|Retained assignment|Member filter paging' "$scratch/response.json"
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
hold; get member "/boards/$board/cards?members=$owner" > "$scratch/status" & request_pid=$!
blocked; release "DELETE FROM sessions WHERE user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
scripts/ci/assert-file-excludes.sh '"items"|Retained assignment|Member filter paging' "$scratch/response.json"
curl --fail --silent --show-error -c "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/member.credentials")" "$base/auth/login" >/dev/null
hold; get member "/boards/$board" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh '"cardMembers"|Assignment fixture|displayName|Retained assignment' "$scratch/response.json"
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
hold; get member "/boards/$board" > "$scratch/status" & request_pid=$!
blocked; release "DELETE FROM sessions WHERE user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
scripts/ci/assert-file-excludes.sh '"cardMembers"|Assignment fixture|displayName|Retained assignment' "$scratch/response.json"
curl --fail --silent --show-error -c "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/member.credentials")" "$base/auth/login" >/dev/null
hold; get member "/cards/$card/member-options" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh '"items"|Assignment fixture|displayName|assigned|cardVersion' "$scratch/response.json"
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
hold; get member "/cards/$card/member-options" > "$scratch/status" & request_pid=$!
blocked; release "DELETE FROM sessions WHERE user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
scripts/ci/assert-file-excludes.sh '"items"|Assignment fixture|displayName|assigned|cardVersion' "$scratch/response.json"
curl --fail --silent --show-error -c "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/member.credentials")" "$base/auth/login" >/dev/null
hold; get member "/cards/$card/members" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh '"items"|Assignment fixture|displayName|assignedBy|cardVersion' "$scratch/response.json"
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
hold; get member "$path" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh '"items"|Assignment seeded|displayName' "$scratch/response.json"
test "$(request owner PUT "$member_path?version=1" "$key" '{}')" = 404
scripts/ci/assert-file-excludes.sh 'Retained assignment|"card"|"userId"' "$scratch/response.json"
test "$(get owner "$path" removed)" = 200
jq -e --arg member "$member" 'all(.items[];.userId!=$member)' "$scratch/removed.json" >/dev/null
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
hold; get member "$path" > "$scratch/status" & request_pid=$!
blocked; release "DELETE FROM sessions WHERE user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
scripts/ci/assert-file-excludes.sh '"items"|Assignment seeded|displayName' "$scratch/response.json"
curl --fail --silent --show-error -c "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/member.credentials")" "$base/auth/login" >/dev/null
hold; get member "/cards/$card/members" > "$scratch/status" & request_pid=$!
blocked; release "DELETE FROM sessions WHERE user_id='$member';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
scripts/ci/assert-file-excludes.sh '"items"|Assignment fixture|displayName|assignedBy|cardVersion' "$scratch/response.json"
departure_key=11111111-1111-1111-1111-111111111147
departure_version=$(admin "SELECT version FROM board_members WHERE board_id='$board' AND user_id='$member';")
departure_path="/boards/$board/members/$member?version=$departure_version"
before=$(state)
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner DELETE "$departure_path" "$departure_key" '{}')" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request owner DELETE "$departure_path" "$departure_key" '{}')" = 204
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND board_id='$board' AND user_id='$member';")" = 0
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND card_id='$card' AND user_id='$owner';")" = 1
test "$(admin "SELECT version FROM cards WHERE id='$card';")" = 6
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id='$card' AND event_type='CARD_MEMBER_REMOVED';")" = 2
after=$(state)
test "$(request owner DELETE "$departure_path" "$departure_key" '{}')" = 204
test "$after" = "$(state)"
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$member';" >/dev/null
test "$(request owner PUT "$member_path?version=6" 11111111-1111-1111-1111-111111111148 '{}')" = 200
second_board=$(admin 'SELECT gen_random_uuid();'); second_list=$(admin 'SELECT gen_random_uuid();'); archived_card=$(admin 'SELECT gen_random_uuid();')
foreign_org=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Retained foreign assignments"}' "$base/organizations" | jq -r '.organization.id')
foreign_board=$(admin 'SELECT gen_random_uuid();'); foreign_list=$(admin 'SELECT gen_random_uuid();'); foreign_card=$(admin 'SELECT gen_random_uuid();')
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$foreign_org','$member','MEMBER','ACTIVE');
 INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('$second_board','$org','Second cleanup Board',now(),now()),('$foreign_board','$foreign_org','Retained foreign Board',now(),now());
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES
 (gen_random_uuid(),'$org','$second_board','$member','MEMBER','ACTIVE',now(),now()),
 (gen_random_uuid(),'$foreign_org','$foreign_board','$member','MEMBER','ACTIVE',now(),now());
 INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('$second_list','$org','$second_board','Archived cleanup List','500000000000000000000000000000',now(),now()),
 ('$foreign_list','$foreign_org','$foreign_board','Retained foreign List','500000000000000000000000000000',now(),now());
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,archived_at,version,created_at,updated_at) VALUES
 ('$archived_card','$org','$second_board','$second_list','Archived assigned Card','500000000000000000000000000000','ARCHIVED',now(),3,now(),now()),
 ('$foreign_card','$foreign_org','$foreign_board','$foreign_list','Retained foreign Card','500000000000000000000000000000','ACTIVE',null,2,now(),now());
 INSERT INTO card_members(tenant_id,board_id,card_id,user_id,assigned_by) VALUES
 ('$org','$second_board','$archived_card','$member','$owner'),('$foreign_org','$foreign_board','$foreign_card','$member','$owner');" >/dev/null
before=$(state)
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner DELETE "/organizations/$org/members/$member" 11111111-1111-1111-1111-111111111149 '{}')" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request owner DELETE "/organizations/$org/members/$member" 11111111-1111-1111-1111-111111111149 '{}')" = 204
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND user_id='$member';")" = 0
test "$(admin "SELECT version FROM cards WHERE id='$card';")" = 8
test "$(admin "SELECT version FROM cards WHERE id='$archived_card';")" = 4
test "$(admin "SELECT version FROM cards WHERE id='$foreign_card';")" = 2
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$foreign_org' AND card_id='$foreign_card' AND user_id='$member';")" = 1
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND card_id='$card' AND user_id='$owner';")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='CARD_MEMBER_REMOVED' AND entity_id IN ('$card','$archived_card');")" = 4
admin "UPDATE organization_members SET status='ACTIVE' WHERE tenant_id='$org' AND user_id='$member';" >/dev/null
test "$(request owner DELETE "$member_path?version=8" 11111111-1111-1111-1111-111111111150 '{}')" = 200
jq -e '.changed==false and .card.version==8' "$scratch/response.json" >/dev/null
test "$(request owner PUT "$member_path?version=8" 11111111-1111-1111-1111-111111111151 '{}')" = 200
curl --fail --silent --show-error -c "$scratch/member.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/member.credentials")" "$base/auth/login" >/dev/null
before=$(state)
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request member POST "/organizations/$org/leave" 11111111-1111-1111-1111-111111111152 '{}')" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request member POST "/organizations/$org/leave" 11111111-1111-1111-1111-111111111152 '{}')" = 204
test "$(admin "SELECT count(*) FROM card_members WHERE tenant_id='$org' AND user_id='$member';")" = 0
test "$(admin "SELECT version FROM cards WHERE id='$card';")" = 10
test "$(admin "SELECT version FROM cards WHERE id='$foreign_card';")" = 2
test "$(admin "SELECT count(*) FROM users WHERE id='$member' AND status='ACTIVE';")" = 1
admin "UPDATE boards SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$board';" >/dev/null
test "$(get owner "$path")" = 404
echo 'Assignable Board members and Card assignments: scoped choices, 50+2 pages, atomic rollback, exact retry, revisions, multiple assignees and post-wait revocation passed.'
