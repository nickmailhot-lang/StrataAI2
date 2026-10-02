#!/usr/bin/env bash
set -euo pipefail
# PRD-07/18-TC-02/04/05/08: exact-image archive paging and post-wait admission.
test "${CI:-}" = true || { echo 'Disposable archive fixtures may run only in CI.' >&2; exit 1; }
base=http://localhost:8080
scratch=$(mktemp -d); gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
  if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
  admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null || true
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Archived List check failed at line $LINENO" >&2' ERR
login() { curl --fail --silent --show-error -c "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$1.credentials")" "$base/auth/login" >/dev/null; }
for actor in owner editor outsider portal; do
  jq -nc --arg email "archive-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"archive-correct-horse-battery",displayName:"Archive fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  login "$actor"
done
owner=$(jq -r '.user.id' "$scratch/owner.user"); editor=$(jq -r '.user.id' "$scratch/editor.user"); portal=$(jq -r '.user.id' "$scratch/portal.user")
org=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Archive scope"}' "$base/organizations" | jq -r '.organization.id')
board=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Archive Board",visibility:"PRIVATE"}')" "$base/boards" | jq -r '.id')
for id in "$owner" "$editor" "$portal" "$org" "$board"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$editor','MEMBER','ACTIVE');
  INSERT INTO portal_access(id,tenant_id,user_id,status,relationship_type) VALUES(gen_random_uuid(),'$org','$portal','ACTIVE','OWNER');
  INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
    VALUES(gen_random_uuid(),'$org','$board','$editor','ADMIN','ACTIVE',now(),now());
  INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,created_at,updated_at,archived_at)
    SELECT gen_random_uuid(),'$org','$board','Archived fixture',lpad(i::text,30,'0'),'ARCHIVED',now(),now(),now() FROM generate_series(1,52) i;
  INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,created_at,updated_at)
    SELECT gen_random_uuid(),l.tenant_id,l.board_id,l.id,'Contained fixture',lpad(i::text,30,'0'),
      CASE i WHEN 1 THEN 'ACTIVE' WHEN 2 THEN 'ARCHIVED' ELSE 'DELETED' END,now(),now()
    FROM board_lists l CROSS JOIN generate_series(1,3) i WHERE l.board_id='$board';
  INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,created_at,updated_at)
    VALUES(gen_random_uuid(),'$org','$board','Active fixture','800000000000000000000000000000','ACTIVE',now(),now()),
      (gen_random_uuid(),'$org','$board','Deleted fixture','900000000000000000000000000000','DELETED',now(),now());" >/dev/null
get() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -H 'Idempotency-Key: 11111111-1111-1111-1111-111111111111' -o "$scratch/${3:-response}.json" -w '%{http_code}' "$base$2"; }
state() { admin "SELECT md5(jsonb_build_object(
  'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id='$org'),
  'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
  'audits',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$org'),
  'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
  'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
  'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
before=$(state)
# PRD-18-FR-004/011: card discovery is bounded and hides deleted parents/detail bodies.
test "$(get owner "/boards/$board/archived-cards" cards-first)" = 200
jq -e --arg org "$org" --arg board "$board" '.organizationId==$org and .boardId==$board and (.items|length)==50
  and .nextCursor==.items[-1].card.id and all(.items[];.card.lifecycleState=="archived"
    and .card.description==null and .list.lifecycleState=="archived" and .card.listId==.list.id)' "$scratch/cards-first.json" >/dev/null
card_cursor=$(jq -r '.nextCursor' "$scratch/cards-first.json")
test "$(get owner "/boards/$board/archived-cards?after=$card_cursor" cards-second)" = 200
jq -e '.nextCursor==null and (.items|length)==2' "$scratch/cards-second.json" >/dev/null
admin "SELECT c.id FROM cards c JOIN board_lists l ON l.tenant_id=c.tenant_id AND l.board_id=c.board_id AND l.id=c.list_id
  WHERE c.tenant_id='$org' AND c.board_id='$board' AND c.lifecycle_state='ARCHIVED' AND l.lifecycle_state<>'DELETED' ORDER BY c.id;" > "$scratch/expected-card.ids"
jq -sr '.[].items[].card.id' "$scratch/cards-first.json" "$scratch/cards-second.json" > "$scratch/actual-card.ids"
cmp "$scratch/expected-card.ids" "$scratch/actual-card.ids"
test "$(get owner "/boards/$board/archived-cards?after=bad")" = 400
jq -e '.code=="invalid_archive_cursor"' "$scratch/response.json" >/dev/null
test "$(get outsider "/boards/$board/archived-cards?after=bad")" = 404
scripts/ci/assert-file-excludes.sh 'Archived fixture|Contained fixture|"items"' "$scratch/response.json"
test "$(get portal "/boards/$board/archived-cards")" = 404
test "$before" = "$(state)"
test "$(get owner "/boards/$board/archived-lists" first)" = 200
jq -e --arg org "$org" --arg board "$board" '.organizationId==$org and .boardId==$board and (.items|length)==50
  and .nextCursor==.items[-1].list.id and all(.items[];.list.lifecycleState=="archived" and .containedCardCount==2)' "$scratch/first.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/first.json")
test "$(get owner "/boards/$board/archived-lists?after=$cursor" second)" = 200
jq -e '.nextCursor==null and (.items|length)==2 and all(.items[];.containedCardCount==2)' "$scratch/second.json" >/dev/null
admin "SELECT id FROM board_lists WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ARCHIVED' ORDER BY id;" > "$scratch/expected.ids"
jq -sr '.[].items[].list.id' "$scratch/first.json" "$scratch/second.json" > "$scratch/actual.ids"
cmp "$scratch/expected.ids" "$scratch/actual.ids"
test "$(get editor "/boards/$board/archived-lists")" = 200
test "$(get owner "/boards/$board/archived-lists?after=not-a-uuid")" = 400
jq -e '.code=="invalid_archive_cursor"' "$scratch/response.json" >/dev/null
test "$(get outsider "/boards/$board/archived-lists?after=not-a-uuid")" = 404
scripts/ci/assert-file-excludes.sh 'Archived fixture|Contained fixture|containedCardCount' "$scratch/response.json"
test "$(get portal "/boards/$board/archived-lists")" = 404
admin "UPDATE boards SET visibility='PUBLIC' WHERE id='$board';" >/dev/null
test "$(get outsider "/boards/$board/archived-lists")" = 404
test "$(curl --max-time 30 --silent --show-error -o "$scratch/anonymous.json" -w '%{http_code}' "$base/boards/$board/archived-lists")" = 401
admin "UPDATE boards SET visibility='PRIVATE' WHERE id='$board';" >/dev/null
test "$(get owner '/boards/00000000-0000-0000-0000-000000000000/archived-lists')" = 404
test "$(get owner "/boards/$board")" = 200
jq -e '(.lists|length)==1 and .lists[0].list.name=="Active fixture" and (.lists[0].cards|length)==0' "$scratch/response.json" >/dev/null
test "$before" = "$(state)"
hold() {
  mkfifo "$scratch/gate.in"
  docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 & gate_pid=$!
  exec 3> "$scratch/gate.in"
  printf 'BEGIN;\nSELECT id FROM boards WHERE id=\047%s\047 FOR UPDATE;\n\\echo archive_locked\n' "$board" >&3
  for ((attempt=0;attempt<100;attempt++)); do if grep -q '^archive_locked$' "$scratch/gate.log"; then return; fi; kill -0 "$gate_pid" || return 1; sleep 0.05; done
  return 1
}
blocked() {
  for ((attempt=0;attempt<100;attempt++)); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%FOR UPDATE%';")" -ge 1; then return; fi
    sleep 0.05
  done
  echo 'Expected archive read Board lock wait was not observed.' >&2; return 1
}
release() { printf '%s\nCOMMIT;\n\\q\n' "$1" >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''; rm "$scratch/gate.in" "$scratch/gate.log"; }
# Authority must be re-read after the Board wait, including for a later page.
hold; get editor "/boards/$board/archived-lists?after=$cursor" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED',version=version+1 WHERE board_id='$board' AND user_id='$editor';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh 'Archived fixture|Contained fixture|containedCardCount' "$scratch/response.json"
admin "UPDATE board_members SET status='ACTIVE',role='MEMBER' WHERE board_id='$board' AND user_id='$editor';" >/dev/null
test "$(get editor "/boards/$board/archived-lists")" = 404
test "$(get editor "/boards/$board/archived-cards")" = 200
# Contributor discovery follows Card archive/restore rights; removed membership does not.
hold; get editor "/boards/$board/archived-cards?after=$card_cursor" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE board_members SET status='REMOVED',version=version+1 WHERE board_id='$board' AND user_id='$editor';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
scripts/ci/assert-file-excludes.sh 'Archived fixture|Contained fixture|"items"' "$scratch/response.json"
admin "UPDATE board_members SET status='ACTIVE' WHERE board_id='$board' AND user_id='$editor';" >/dev/null
hold; get owner "/boards/$board/archived-lists" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE sessions SET revoked_at=now() WHERE user_id='$owner' AND revoked_at IS NULL;"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
login owner
hold; get owner "/boards/$board/archived-cards?after=$card_cursor" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE sessions SET revoked_at=now() WHERE user_id='$owner' AND revoked_at IS NULL;"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 401
login owner
hold; get owner "/boards/$board/archived-lists" > "$scratch/status" & request_pid=$!
blocked; release "UPDATE boards SET lifecycle_state='ARCHIVED',version=version+1 WHERE id='$board';"
wait "$request_pid"; request_pid=''; test "$(cat "$scratch/status")" = 404
test "$(get owner "/boards/$board/archived-cards")" = 404
admin "UPDATE boards SET lifecycle_state='ACTIVE' WHERE id='$board';" >/dev/null
test "$(get owner "/boards/$board/archived-lists")" = 200
test "$before" = "$(state)"
echo 'Archived Lists: bounded UUID paging, complete contained-card counts, active/deleted exclusion, unchanged state/receipts, current admin authority, post-wait membership/session/parent rejection passed.'

# PRD-07-FR-010/011 / PRD-18-FR-007/009: explicit current deletion consent.
target=$(head -n 1 "$scratch/expected.ids")
delete_key=$(cat /proc/sys/kernel/random/uuid)
delete_list() {
  curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' \
    -H "Idempotency-Key: $delete_key" -X DELETE -o "$scratch/deleted.json" -w '%{http_code}' "$base/lists/$target?version=1$1"
}
for query in '' '&confirmed=false&containedCardCount=2' '&confirmed=true' '&confirmed=true&containedCardCount=-1'; do
  test "$(delete_list "$query")" = 400
  test "$before" = "$(state)"
done
test "$(delete_list '&confirmed=true&containedCardCount=1')" = 409
jq -e '.code=="deletion_impact_changed"' "$scratch/deleted.json" >/dev/null
test "$before" = "$(state)"
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(delete_list '&confirmed=true&containedCardCount=2')" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$before" = "$(state)"
test "$(delete_list '&confirmed=true&containedCardCount=2')" = 200
jq -e --arg target "$target" '.id==$target and .lifecycleState=="deleted" and .version==2' "$scratch/deleted.json" >/dev/null
cp "$scratch/deleted.json" "$scratch/receipt.json"
deleted_state=$(state)
test "$(delete_list '&confirmed=true&containedCardCount=2')" = 200
cmp "$scratch/receipt.json" "$scratch/deleted.json"
test "$deleted_state" = "$(state)"
test "$(get owner "/boards/$board/archived-cards" cards-deleted)" = 200
jq -e --arg target "$target" 'all(.items[];.list.id!=$target and .card.listId!=$target)' "$scratch/cards-deleted.json" >/dev/null
test "$(delete_list '&confirmed=true&containedCardCount=1')" = 409
jq -e '.code=="idempotency_key_reused"' "$scratch/deleted.json" >/dev/null
test "$deleted_state" = "$(state)"
admin "UPDATE organization_members SET status='REMOVED' WHERE tenant_id='$org' AND user_id='$owner';" >/dev/null
test "$(delete_list '&confirmed=true&containedCardCount=2')" = 404
scripts/ci/assert-file-excludes.sh 'Archived fixture|Contained fixture|lifecycleState|version' "$scratch/deleted.json"
admin "UPDATE organization_members SET status='ACTIVE' WHERE tenant_id='$org' AND user_id='$owner';" >/dev/null
test "$deleted_state" = "$(state)"
test "$(get owner "/boards/$board/archived-lists")" = 200
jq -e --arg target "$target" 'all(.items[];.list.id!=$target)' "$scratch/response.json" >/dev/null
admin "UPDATE boards SET lifecycle_state='ARCHIVED' WHERE id='$board';" >/dev/null
test "$(delete_list '&confirmed=true&containedCardCount=2')" = 404
admin "UPDATE boards SET lifecycle_state='ACTIVE' WHERE id='$board';" >/dev/null
test "$(delete_list '&confirmed=true&containedCardCount=2')" = 200
cmp "$scratch/receipt.json" "$scratch/deleted.json"
test "$deleted_state" = "$(state)"
echo 'List deletion: explicit confirmation/current impact, full rollback, non-reapplying historical receipt, changed-intent rejection, fresh authority and tombstone exclusion passed.'

# Card deletion uses a distinct explicit consent fingerprint and tombstone receipt recovery.
card_target=$(admin "SELECT c.id FROM cards c JOIN board_lists l ON l.tenant_id=c.tenant_id AND l.board_id=c.board_id AND l.id=c.list_id
  WHERE c.tenant_id='$org' AND c.board_id='$board' AND c.lifecycle_state='ARCHIVED' AND l.lifecycle_state<>'DELETED' ORDER BY c.id LIMIT 1;")
card_parent=$(admin "SELECT list_id FROM cards WHERE tenant_id='$org' AND id='$card_target';")
card_key=$(cat /proc/sys/kernel/random/uuid)
delete_card() {
  curl --max-time 60 --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' \
    -H "Idempotency-Key: $card_key" -X DELETE -o "$scratch/card-deleted.json" -w '%{http_code}' "$base/cards/$card_target?version=1$1"
}
card_before=$(state)
for query in '' '&confirmed=false'; do
  test "$(delete_card "$query")" = 400
  jq -e '.code=="delete_confirmation_required"' "$scratch/card-deleted.json" >/dev/null
  test "$card_before" = "$(state)"
done
admin 'REVOKE INSERT ON audit_events FROM strataai_api_runtime;' >/dev/null
test "$(delete_card '&confirmed=true')" = 503
admin 'GRANT INSERT ON audit_events TO strataai_api_runtime;' >/dev/null
test "$card_before" = "$(state)"
test "$(delete_card '&confirmed=true')" = 200
jq -e --arg id "$card_target" '.id==$id and .lifecycleState=="deleted" and .version==2' "$scratch/card-deleted.json" >/dev/null
cp "$scratch/card-deleted.json" "$scratch/card-receipt.json"
card_after=$(state)
test "$(delete_card '&confirmed=true')" = 200
cmp "$scratch/card-receipt.json" "$scratch/card-deleted.json"
test "$card_after" = "$(state)"
test "$(delete_card '&confirmed=false')" = 409
jq -e '.code=="idempotency_key_reused"' "$scratch/card-deleted.json" >/dev/null
test "$card_after" = "$(state)"
admin "UPDATE organization_members SET status='REMOVED' WHERE tenant_id='$org' AND user_id='$owner';" >/dev/null
test "$(delete_card '&confirmed=true')" = 404
scripts/ci/assert-file-excludes.sh 'Contained fixture|lifecycleState|version' "$scratch/card-deleted.json"
admin "UPDATE organization_members SET status='ACTIVE' WHERE tenant_id='$org' AND user_id='$owner';" >/dev/null
admin "UPDATE boards SET lifecycle_state='ARCHIVED' WHERE id='$board';" >/dev/null
test "$(delete_card '&confirmed=true')" = 404
admin "UPDATE boards SET lifecycle_state='ACTIVE' WHERE id='$board';" >/dev/null
test "$(delete_card '&confirmed=true')" = 200
cmp "$scratch/card-receipt.json" "$scratch/card-deleted.json"
test "$card_after" = "$(state)"
test "$(get owner "/boards/$board/archived-cards" card-hidden)" = 200
jq -e --arg id "$card_target" 'all(.items[];.card.id!=$id)' "$scratch/card-hidden.json" >/dev/null
admin "UPDATE board_lists SET lifecycle_state='DELETED' WHERE id='$card_parent';" >/dev/null
test "$(delete_card '&confirmed=true')" = 404
scripts/ci/assert-file-excludes.sh 'Contained fixture|lifecycleState|version' "$scratch/card-deleted.json"
echo 'Card deletion: explicit archived consent, rollback, identical non-reapplying receipts, changed intent, current authority and deleted-parent denial passed.'
