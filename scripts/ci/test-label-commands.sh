#!/usr/bin/env bash
set -euo pipefail
# PRD-10: disposable fixtures through the release web proxy to the exact API.
test "${CI:-}" = true || { echo 'Label command fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8088
scratch=$(mktemp -d); gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" || true; fi
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null || true
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Label command check failed at line $LINENO" >&2' ERR
for actor in owner editor outsider; do
  jq -nc --arg email "labels-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"labels-correct-horse-battery",displayName:"Label fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); editor=$(jq -r '.user.id' "$scratch/editor.user")
org=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Label command scope"}' "$base/organizations" | jq -r '.organization.id')
board=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Label command Board",visibility:"PRIVATE"}')" "$base/boards" | jq -r '.id')
for id in "$owner" "$editor" "$org" "$board"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$editor','MEMBER','ACTIVE');
INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES(gen_random_uuid(),'$org','$board','$editor','MEMBER','ACTIVE',now(),now());" >/dev/null
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -d "$5" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
get() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/response.json" -w '%{http_code}' "$base$2"; }
state() { admin "SELECT md5(jsonb_build_object(
 'labels',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_labels l WHERE tenant_id='$org'),
 'routes',(SELECT jsonb_agg(to_jsonb(r) ORDER BY label_id) FROM label_routes r WHERE tenant_id='$org'),
 'associations',(SELECT jsonb_agg(to_jsonb(a) ORDER BY card_id,label_id) FROM card_labels a WHERE tenant_id='$org'),
 'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
create_key=11111111-1111-1111-1111-111111111101
body='{"name":"  Priority  ","color":" BLUE "}'
before=$(state)
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner POST "/boards/$board/labels" "$create_key" "$body")" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request owner POST "/boards/$board/labels" "$create_key" "$body")" = 201
jq -e --arg org "$org" --arg board "$board" '.organizationId==$org and .boardId==$board and .name=="Priority" and .color=="blue" and .version==1 and .deleted==false and (.rank|test("^[0-9]{30}$"))' "$scratch/response.json" >/dev/null
label=$(jq -r '.id' "$scratch/response.json"); cp "$scratch/response.json" "$scratch/create-receipt.json"
after=$(state)
test "$(request owner POST "/boards/$board/labels" "$create_key" "$body")" = 201
cmp "$scratch/response.json" "$scratch/create-receipt.json"; test "$after" = "$(state)"
test "$(request owner POST "/boards/$board/labels" "$create_key" '{"name":"Changed","color":"blue"}')" = 409
test "$(get outsider "/boards/$board/labels")" = 404
test "$(request outsider PATCH "/labels/$label" 11111111-1111-1111-1111-111111111102 '{"name":"Guess","color":"red","version":1}')" = 404
scripts/ci/assert-file-excludes.sh 'Priority|"color"|"rank"' "$scratch/response.json"
test "$after" = "$(state)"
test "$(get owner "/boards/$board/labels?after=invalid")" = 400
test "$(request editor PATCH "/labels/$label" 11111111-1111-1111-1111-111111111103 '{"name":"","color":"green","version":1}')" = 200
jq -e '.name=="" and .color=="green" and .version==2' "$scratch/response.json" >/dev/null
test "$(request editor PATCH "/labels/$label" 11111111-1111-1111-1111-111111111104 '{"name":"Stale","color":"red","version":1}')" = 409
test "$(request editor DELETE "/labels/$label?version=2&confirmed=true" 11111111-1111-1111-1111-111111111105 '{}')" = 404
# An archived Card association is seeded; active Card assignment uses the API.
list=$(admin "INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES(gen_random_uuid(),'$org','$board','Label Cards','500000000000000000000000000000',now(),now()) RETURNING id;")
admin "INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,created_at,updated_at,archived_at,version)
 SELECT gen_random_uuid(),'$org','$board','$list','Retained Card',lpad(i::text,30,'0'),CASE i WHEN 1 THEN 'ACTIVE' ELSE 'ARCHIVED' END,now(),now(),CASE i WHEN 2 THEN now() END,7 FROM generate_series(1,2) i;
 INSERT INTO card_labels(tenant_id,board_id,card_id,label_id) SELECT tenant_id,board_id,id,'$label' FROM cards WHERE board_id='$board' AND lifecycle_state='ARCHIVED';" >/dev/null
card=$(admin "SELECT id FROM cards WHERE board_id='$board' AND lifecycle_state='ACTIVE';")
assignment_key=11111111-1111-1111-1111-111111111107
before=$(state)
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request editor PUT "/cards/$card/labels/$label?version=7" "$assignment_key" '{}')" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request editor PUT "/cards/$card/labels/$label?version=7" "$assignment_key" '{}')" = 200
jq -e --arg label "$label" '.labelId==$label and .assigned==true and .changed==true and .card.version==8' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/assignment-receipt.json"; after_assignment=$(state)
test "$(request editor PUT "/cards/$card/labels/$label?version=7" "$assignment_key" '{}')" = 200
cmp "$scratch/response.json" "$scratch/assignment-receipt.json"; test "$after_assignment" = "$(state)"
test "$(request editor PUT "/cards/$card/labels/$label?version=8" 11111111-1111-1111-1111-111111111108 '{}')" = 200
jq -e '.changed==false and .card.version==8' "$scratch/response.json" >/dev/null
# A new no-op request stores its receipt, but creates no extra LABEL_ADDED event.
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id='$card' AND event_type='LABEL_ADDED';")" = 1
test "$(request editor DELETE "/cards/$card/labels/$label?version=7" 11111111-1111-1111-1111-111111111109 '{}')" = 409
test "$(request editor DELETE "/cards/$card/labels/$label?version=8" 11111111-1111-1111-1111-111111111110 '{}')" = 200
jq -e '.assigned==false and .changed==true and .card.version==9' "$scratch/response.json" >/dev/null
test "$(request editor PUT "/cards/$card/labels/$label?version=9" 11111111-1111-1111-1111-111111111111 '{}')" = 200
jq -e '.assigned==true and .card.version==10' "$scratch/response.json" >/dev/null
before=$(state); delete_key=11111111-1111-1111-1111-111111111106
test "$(request owner DELETE "/labels/$label?version=2" "$delete_key" '{}')" = 400
test "$before" = "$(state)"
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner DELETE "/labels/$label?version=2&confirmed=true" "$delete_key" '{}')" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request owner DELETE "/labels/$label?version=2&confirmed=true" "$delete_key" '{}')" = 200
jq -e '.deleted==true and .version==3' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/delete-receipt.json"
test "$(admin "SELECT count(*) FROM card_labels WHERE board_id='$board';")" = 0
test "$(admin "SELECT count(*) FROM cards WHERE board_id='$board' AND title='Retained Card' AND ((lifecycle_state='ACTIVE' AND version=11) OR (lifecycle_state='ARCHIVED' AND version=8));")" = 2
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND entity_id='$label' AND event_type='LABEL_DELETED';")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND entity_id='$label' AND event_type='LABEL_DELETED';")" = 1
after=$(state)
test "$(request owner DELETE "/labels/$label?version=2&confirmed=true" "$delete_key" '{}')" = 200
cmp "$scratch/response.json" "$scratch/delete-receipt.json"; test "$after" = "$(state)"
test "$(get owner "/boards/$board/labels")" = 200
jq -e '.items==[] and .nextCursor==null' "$scratch/response.json" >/dev/null
# Observe the actual Board lock wait before changing lifecycle or authority.
hold() {
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 & gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\nSELECT id FROM boards WHERE id=\047%s\047 FOR UPDATE;\n\\echo label_locked\n' "$board" >&3
  for ((attempt=0;attempt<100;attempt++)); do if grep -q '^label_locked$' "$scratch/gate.log"; then return; fi; kill -0 "$gate_pid" || return 1; sleep 0.05; done
  return 1
}
blocked() {
  for ((attempt=0;attempt<100;attempt++)); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%FOR UPDATE%';")" -ge 1; then return; fi
    sleep 0.05
  done
  echo 'Expected label command Board lock wait was not observed.' >&2; return 1
}
release() { printf '%s\nCOMMIT;\n\\q\n' "$1" >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; rm "$scratch/gate.in" "$scratch/gate.log"; }
hold; request owner POST "/boards/$board/labels" "$create_key" "$body" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE boards SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$board';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404; test "$after" = "$(state)"
test "$(request owner DELETE "/labels/$label?version=2&confirmed=true" "$delete_key" '{}')" = 404
admin "UPDATE boards SET lifecycle_state='ACTIVE',version=version+1 WHERE id='$board';" >/dev/null
hold; request owner DELETE "/labels/$label?version=2&confirmed=true" "$delete_key" '{}' > "$scratch/status" & request_pid=$!
blocked; release "UPDATE sessions SET revoked_at=now() WHERE user_id='$owner' AND revoked_at IS NULL;"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401; test "$after" = "$(state)"
curl --fail --silent --show-error -c "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/owner.credentials")" "$base/auth/login" >/dev/null
test "$(request owner DELETE "/labels/$label?version=2&confirmed=true" "$delete_key" '{}')" = 200
cmp "$scratch/response.json" "$scratch/delete-receipt.json"
admin "INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank)
 SELECT gen_random_uuid(),'$org','$board','Paged label','blue',lpad((i*1000)::text,30,'0') FROM generate_series(1,52) i;
 INSERT INTO card_labels(tenant_id,board_id,card_id,label_id)
 SELECT tenant_id,board_id,'$card',id FROM board_labels WHERE board_id='$board' AND status='ACTIVE';" >/dev/null
test "$(get owner "/cards/$card/labels")" = 200
jq -e --arg card "$card" --arg board "$board" '.cardId==$card and .boardId==$board and .cardVersion==11 and .canEdit==true and (.items|length)==50 and .nextCursor==.items[-1].id' "$scratch/response.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/response.json"); cp "$scratch/response.json" "$scratch/labels-first.json"
test "$(get owner "/cards/$card/labels?after=$cursor")" = 200
jq -e '.nextCursor==null and (.items|length)==2' "$scratch/response.json" >/dev/null
jq -se '([.[0].items[].id,.[1].items[].id]|length)==52 and ([.[0].items[].id,.[1].items[].id]|unique|length)==52' "$scratch/labels-first.json" "$scratch/response.json" >/dev/null
test "$(get owner "/cards/$card/labels?after=bad")" = 400
test "$(get owner "/boards/$board")" = 200
jq -e --arg card "$card" '.cardLabels[$card].total==52 and (.cardLabels[$card].items|length)==6 and (.cardLabels|keys|length)==1' "$scratch/response.json" >/dev/null
admin "UPDATE boards SET visibility='PUBLIC' WHERE id='$board';" >/dev/null
test "$(curl --silent --show-error -o "$scratch/public-labels.json" -w '%{http_code}' "$base/boards/$board")" = 200
jq -e --arg card "$card" '.access.canEdit==false and .cardLabels[$card].total==52 and (.cardLabels[$card].items|length)==6' "$scratch/public-labels.json" >/dev/null
admin "UPDATE boards SET visibility='PRIVATE' WHERE id='$board';" >/dev/null
test "$(get outsider "/cards/$card/labels")" = 404
hold; get editor "/cards/$card/labels?after=$cursor" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$editor';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh 'Paged label|"items"|"cardVersion"' "$scratch/response.json"
moving=$(admin "SELECT id FROM board_labels WHERE board_id='$board' AND status='ACTIVE' ORDER BY rank DESC,id DESC LIMIT 1;")
anchor=$(admin "SELECT id FROM board_labels WHERE board_id='$board' AND status='ACTIVE' ORDER BY rank,id LIMIT 1;")
move_body=$(jq -nc --arg id "$anchor" '{beforeLabelId:$id,version:1}')
move_key=11111111-1111-1111-1111-111111111112
neighbors() { admin "SELECT md5(jsonb_agg(to_jsonb(l) ORDER BY id)::text) FROM board_labels l WHERE board_id='$board' AND id<>'$moving';"; }
before=$(state); original_neighbors=$(neighbors)
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(request owner POST "/labels/$moving/move" "$move_key" "$move_body")" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(request owner POST "/labels/$moving/move" "$move_key" "$move_body")" = 200
jq -e '.version==2' "$scratch/response.json" >/dev/null
cp "$scratch/response.json" "$scratch/move-receipt.json"
test "$(admin "SELECT a.rank<b.rank FROM board_labels a JOIN board_labels b ON b.id='$anchor' WHERE a.id='$moving';")" = t
test "$original_neighbors" = "$(neighbors)"
after=$(state)
test "$(request owner POST "/labels/$moving/move" "$move_key" "$move_body")" = 200
cmp "$scratch/response.json" "$scratch/move-receipt.json"; test "$after" = "$(state)"
test "$(request owner POST "/labels/$moving/move" 11111111-1111-1111-1111-111111111113 '{"version":2}')" = 200
test "$(admin "SELECT rank>(SELECT max(rank) FROM board_labels WHERE board_id='$board' AND status='ACTIVE' AND id<>'$moving') FROM board_labels WHERE id='$moving';")" = t
test "$original_neighbors" = "$(neighbors)"
test "$(get owner "/cards/$card/label-options")" = 200
jq -e '.cardVersion==11 and (.items|length)==50 and all(.items[];.assigned==true) and .nextCursor==.items[-1].label.id' "$scratch/response.json" >/dev/null
option_cursor=$(jq -r '.nextCursor' "$scratch/response.json"); option_id=$(jq -r '.items[0].label.id' "$scratch/response.json")
test "$(get owner "/cards/$card/label-options?after=$option_cursor")" = 200
jq -e '(.items|length)==2 and .nextCursor==null and all(.items[];.assigned==true)' "$scratch/response.json" >/dev/null
test "$(request owner DELETE "/cards/$card/labels/$option_id?version=11" 11111111-1111-1111-1111-111111111114 '{}')" = 200
test "$(get owner "/cards/$card/label-options")" = 200
jq -e --arg id "$option_id" '.cardVersion==12 and ([.items[]|select(.label.id==$id and .assigned==false)]|length)==1' "$scratch/response.json" >/dev/null
test "$(get owner "/cards/$card/label-options?after=invalid")" = 400
test "$(get editor "/cards/$card/label-options")" = 404
# PRD-10/16 filtering uses all associations, rather than the six face indicators.
filter_label=$(admin "SELECT l.id FROM board_labels l JOIN card_labels a ON a.label_id=l.id AND a.tenant_id=l.tenant_id AND a.board_id=l.board_id WHERE a.card_id='$card' AND l.status='ACTIVE' ORDER BY l.rank DESC,l.id DESC LIMIT 1;")
test "$(get owner "/boards/$board/cards?labels=$filter_label&keyword=Retained&match=all")" = 200
jq -e --arg card "$card" --arg org "$org" --arg board "$board" '.organizationId==$org and .boardId==$board and (.items|length)==1 and .items[0].id==$card and .nextCursor==null' "$scratch/response.json" >/dev/null
test "$(get owner "/boards/$board/cards?labels=$filter_label&keyword=absent&match=all")" = 200
jq -e '.items==[]' "$scratch/response.json" >/dev/null
test "$(get owner "/boards/$board/cards?labels=$filter_label&keyword=absent&match=any")" = 200
jq -e --arg card "$card" '(.items|length)==1 and .items[0].id==$card' "$scratch/response.json" >/dev/null
test "$(get owner "/boards/$board/cards?labels=$filter_label,11111111-1111-1111-1111-111111111199&match=all")" = 200
jq -e '.items==[]' "$scratch/response.json" >/dev/null
test "$(get owner "/boards/$board/cards?labels=$option_id")" = 200
jq -e '.items==[]' "$scratch/response.json" >/dev/null
test "$(get outsider "/boards/$board/cards?labels=invalid&match=invalid")" = 404
test "$(get owner "/boards/$board/cards?after=invalid")" = 400
test "$(get owner "/boards/$board/cards?labels=$filter_label,$filter_label")" = 400
filter_list=$(admin "SELECT list_id FROM cards WHERE id='$card';")
admin "INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board','$filter_list','Filter result 100%_ '||i,lpad((i*1000)::text,30,'0'),now(),now() FROM generate_series(1,52) i;" >/dev/null
test "$(get owner "/boards/$board/cards?keyword=100%25_")" = 200
jq -e '(.items|length)==50 and .nextCursor==.items[-1].id' "$scratch/response.json" >/dev/null
filter_cursor=$(jq -r '.nextCursor' "$scratch/response.json"); cp "$scratch/response.json" "$scratch/filter-first.json"
test "$(get owner "/boards/$board/cards?keyword=100%25_&after=$filter_cursor")" = 200
jq -e '(.items|length)==2 and .nextCursor==null' "$scratch/response.json" >/dev/null
jq -se '([.[0].items[].id,.[1].items[].id]|unique|length)==52' "$scratch/filter-first.json" "$scratch/response.json" >/dev/null
admin "UPDATE board_lists SET lifecycle_state='ARCHIVED' WHERE id='$filter_list';" >/dev/null
test "$(get owner "/boards/$board/cards?keyword=100%25_")" = 200
jq -e '.items==[]' "$scratch/response.json" >/dev/null
admin "UPDATE board_lists SET lifecycle_state='ACTIVE' WHERE id='$filter_list'; UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$editor';" >/dev/null
hold; get editor "/boards/$board/cards?keyword=100%25_" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED' WHERE board_id='$board' AND user_id='$editor';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh 'Filter result|Retained Card|"items"' "$scratch/response.json"
hold; get owner "/boards/$board/cards?keyword=100%25_" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE sessions SET revoked_at=now() WHERE user_id='$owner' AND revoked_at IS NULL;"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
scripts/ci/assert-file-excludes.sh 'Filter result|Retained Card|"items"' "$scratch/response.json"
echo 'Label commands: exact-image CRUD, admission, retry identity, atomic audit rollback and association removal, Card revisions, full-association bounded ANY/ALL filters, and observed post-wait authorization passed.'

# Anonymous reads retain the same bounded filter semantics without a fabricated
# session actor. Direct fixture visibility changes isolate post-wait admission;
# native coverage separately uses the canonical visibility command.
anonymous_get() { curl --max-time 60 --silent --show-error -o "$scratch/response.json" -w '%{http_code}' "$base$1"; }
admin "UPDATE boards SET visibility='PUBLIC' WHERE id='$board';" >/dev/null
public_before=$(state)
test "$(anonymous_get "/boards/$board/cards?keyword=100%25_")" = 200
jq -e '(.items|length)==50 and .nextCursor==.items[-1].id' "$scratch/response.json" >/dev/null
public_cursor=$(jq -r '.nextCursor' "$scratch/response.json"); cp "$scratch/response.json" "$scratch/public-first.json"
test "$(anonymous_get "/boards/$board/cards?keyword=100%25_&after=$public_cursor")" = 200
jq -e '(.items|length)==2 and .nextCursor==null' "$scratch/response.json" >/dev/null
jq -se --slurpfile expected "$scratch/filter-first.json" '([.[0].items[].id]==[$expected[0].items[].id]) and ([.[0].items[].id,.[1].items[].id]|unique|length)==52' "$scratch/public-first.json" "$scratch/response.json" >/dev/null
test "$(anonymous_get "/boards/$board/labels")" = 200
jq -e '.canEdit==false and .canDelete==false' "$scratch/response.json" >/dev/null
test "$(anonymous_get "/boards/$board/cards?members=$editor")" = 404
scripts/ci/assert-file-excludes.sh 'Filter result|Retained Card|"items"' "$scratch/response.json"
test "$(anonymous_get "/boards/$board/cards?match=invalid")" = 400
jq -e '.code=="invalid_board_filter"' "$scratch/response.json" >/dev/null
test "$public_before" = "$(state)"
hold; anonymous_get "/boards/$board/cards?keyword=100%25_&after=$public_cursor" > "$scratch/status" & request_pid=$!
for ((attempt=0;attempt<100;attempt++)); do
  if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%visibility=%FOR SHARE%';")" -ge 1; then break; fi
  sleep 0.05
done
test "$attempt" -lt 100
release "UPDATE boards SET visibility='PRIVATE' WHERE id='$board';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh 'Filter result|Retained Card|"items"' "$scratch/response.json"
test "$(anonymous_get "/boards/$board/labels?after=invalid")" = 404
echo 'Anonymous PUBLIC Board filters: exact 50+2 IDs, read-only choices, member nondisclosure, unchanged state and observed post-wait private-visibility withdrawal passed.'
