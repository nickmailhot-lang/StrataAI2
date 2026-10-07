#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable permission fixtures may run only in CI.' >&2; exit 1; }
base=${1:-http://localhost:8080}
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Board HTTP permission matrix failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
key() { python3 -c 'import uuid; print(uuid.uuid4(), end="")'; }
request() {
  local cookies=(); if test "$1" != anonymous; then cookies=(-b "$scratch/$1.cookies"); fi
  curl --max-time 30 --silent --show-error "${cookies[@]}" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: ${5:-$(key)}" -X "$2" -d "$4" -o "$scratch/response" -w '%{http_code}' "$base$3"
}
for actor in owner orgadmin boardadmin boardmember orgmember former; do
  data=$(jq -nc --arg email "permission-$actor-$(key)@example.test" '{email:$email,password:"permission-matrix-correct-horse",displayName:"Permission fixture"}')
  test "$(request anonymous POST /auth/register "$data")" = 201
  jq -r '.user.id' "$scratch/response" > "$scratch/$actor.id"
  curl --max-time 30 --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$data" "$base/auth/login" >/dev/null
done
test "$(request owner POST /organizations '{"name":"Restricted HTTP permission matrix"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response")
for actor in orgadmin boardadmin boardmember orgmember former; do
  role=MEMBER; if test "$actor" = orgadmin; then role=ADMIN; fi
  user=$(cat "$scratch/$actor.id")
  admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$user','$role','ACTIVE');" >/dev/null
done
for visibility in PRIVATE ORGANIZATION PUBLIC; do
  test "$(request owner POST /boards "$(jq -nc --arg org "$org" --arg visibility "$visibility" '{organizationId:$org,name:("Matrix "+$visibility),visibility:$visibility}')")" = 201
  board=$(jq -r '.id' "$scratch/response"); printf '%s' "$board" > "$scratch/$visibility.board"
  for actor in boardadmin boardmember former; do
    role=MEMBER; if test "$actor" = boardadmin; then role=ADMIN; fi
    test "$(request owner PATCH "/boards/$board/members/$(cat "$scratch/$actor.id")" "$(jq -nc --arg role "$role" '{role:$role}')")" = 200
  done
  test "$(request owner POST "/boards/$board/lists" '{"name":"Matrix list"}')" = 201
  list=$(jq -r '.id' "$scratch/response"); printf '%s' "$list" > "$scratch/$visibility.list"
  test "$(request owner POST "/lists/$list/cards" '{"title":"Private permission matrix Card"}')" = 201
  jq -r '.id' "$scratch/response" > "$scratch/$visibility.card"
done
admin "UPDATE organization_members SET status='REMOVED' WHERE tenant_id='$org' AND user_id='$(cat "$scratch/former.id")';" >/dev/null
effects() {
  admin "SELECT jsonb_build_object(
   'board',(SELECT to_jsonb(b) FROM boards b WHERE id='$board'),
   'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE board_id='$board'),
   'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE board_id='$board'),
   'checklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE card_id='$card'),
   'attachments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE card_id='$card'),
   'comments',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM card_comments c WHERE card_id='$card'),
   'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
   'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
   'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY board_id) FROM work_event_streams s WHERE tenant_id='$org'),
   'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
   'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text;"
}
cases=0
for visibility in PRIVATE ORGANIZATION PUBLIC; do
  board=$(cat "$scratch/$visibility.board"); list=$(cat "$scratch/$visibility.list"); card=$(cat "$scratch/$visibility.card")
  for actor in orgadmin boardadmin boardmember orgmember former anonymous; do
    edit=false; administer=false; view=false
    case "$actor" in orgadmin|boardadmin) edit=true; administer=true;; boardmember) edit=true;; esac
    if $edit || test "$visibility" = PUBLIC || { test "$actor" = orgmember && test "$visibility" = ORGANIZATION; }; then view=true; fi
    expected=404; if $view; then expected=200; fi
    test "$(request "$actor" GET "/boards/$board" '{}')" = "$expected"
    if $view; then
      jq -e --argjson edit "$edit" --argjson admin "$administer" '.access.canView==true and .access.canEdit==$edit and .access.canMove==$edit and .access.canAdminister==$admin' "$scratch/response" >/dev/null
    else scripts/ci/assert-file-excludes.sh 'Private permission matrix Card' "$scratch/response"; fi
    denied=404; if test "$actor" = anonymous; then denied=401; fi
    expected=$denied; if $administer; then expected=200; fi
    test "$(request "$actor" GET "/boards/$board/members" '{}')" = "$expected"
    for operation in card comment checklist attachment list; do
      version=$(admin "SELECT version FROM cards WHERE id='$card';")
      method=POST
      case "$operation" in
        card) method=PATCH; path="/cards/$card"; body=$(jq -nc --argjson v "$version" '{title:"Committed permission matrix",version:$v}');;
        comment) path="/cards/$card/comments"; body=$(jq -nc --argjson v "$version" '{content:"Private matrix comment",cardVersion:$v}');;
        checklist) path="/cards/$card/checklists"; body=$(jq -nc --argjson v "$version" '{title:"Private matrix checklist",cardVersion:$v}');;
        attachment) path="/cards/$card/attachments/url"; body=$(jq -nc --argjson v "$version" '{title:"Private matrix link",url:"https://example.test/matrix",cardVersion:$v}');;
        list) path="/boards/$board/lists"; body='{"name":"Committed matrix list"}';;
      esac
      permitted=$edit; if test "$operation" = comment && test "$actor" = orgadmin; then permitted=false; fi
      before=$(effects); retry=$(key)
      status=$(request "$actor" "$method" "$path" "$body" "$retry")
      if $permitted; then
        case "$status" in 200|201) ;; *) exit 1;; esac
        cp "$scratch/response" "$scratch/receipt"
        committed=$(effects); test "$before" != "$committed"
        test "$(request "$actor" "$method" "$path" "$body" "$retry")" = "$status"
        cmp "$scratch/receipt" "$scratch/response"; test "$committed" = "$(effects)"
      else
        test "$status" = "$denied"; test "$before" = "$(effects)"
        scripts/ci/assert-file-excludes.sh 'Private permission matrix Card' "$scratch/response"
      fi
    done
    cases=$((cases + 1))
  done
done
test "$cases" = 18
echo 'Board permission matrix: 18 visibility/access cases, actual HTTP commands, restricted PostgreSQL state, unchanged refusals and original-key receipt replay passed.'
