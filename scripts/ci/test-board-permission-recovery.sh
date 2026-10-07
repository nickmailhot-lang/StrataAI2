#!/usr/bin/env bash
set -euo pipefail
# PERM-FR-002/010; PRD-05-TC-05/06/07/10: current authority/lifecycle gates receipts.
test "${CI:-}" = true || { echo 'Disposable permission recovery fixtures may run only in CI.' >&2; exit 1; }
base=${1:-http://localhost:8080}
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
trap 'echo "Board permission recovery failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
key() { python3 -c 'import uuid; print(uuid.uuid4(), end="")'; }
request() {
  curl --max-time 30 --silent --show-error -b "$scratch/$1.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
    -H "Idempotency-Key: ${5:-$(key)}" -X "$2" -d "$4" -o "$scratch/response" -w '%{http_code}' "$base$3"
}
for actor in owner member; do
  data=$(jq -nc --arg email "permission-recovery-$actor-$(key)@example.test" '{email:$email,password:"permission-recovery-correct-horse",displayName:"Recovery fixture"}')
  curl --max-time 30 --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$data" "$base/auth/register" > "$scratch/$actor.user"
  curl --max-time 30 --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$data" "$base/auth/login" >/dev/null
done
member=$(jq -r '.user.id' "$scratch/member.user")
test "$(request owner POST /organizations '{"name":"Permission receipt recovery"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response")
for id in "$member" "$org"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');" >/dev/null
effects() {
  admin "SELECT jsonb_build_object(
   'board',(SELECT to_jsonb(b) FROM boards b WHERE id='$board'),
   'list',(SELECT to_jsonb(l) FROM board_lists l WHERE id='$list'),
   'card',(SELECT to_jsonb(c) FROM cards c WHERE id='$card'),
   'checklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE card_id='$card'),
   'attachments',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE card_id='$card'),
   'comments',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM card_comments c WHERE card_id='$card'),
   'member',(SELECT to_jsonb(m) FROM board_members m WHERE board_id='$board' AND user_id='$member'),
   'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
   'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
   'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY board_id) FROM work_event_streams s WHERE tenant_id='$org'),
   'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
   'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text;"
}
for visibility in PRIVATE ORGANIZATION PUBLIC; do
  test "$(request owner POST /boards "$(jq -nc --arg org "$org" --arg visibility "$visibility" '{organizationId:$org,name:("Receipt "+$visibility),visibility:$visibility}')")" = 201
  board=$(jq -r '.id' "$scratch/response")
  test "$(request owner POST "/boards/$board/lists" '{"name":"Receipt list"}')" = 201
  list=$(jq -r '.id' "$scratch/response")
  test "$(request owner POST "/lists/$list/cards" '{"title":"Original receipt Card"}')" = 201
  card=$(jq -r '.id' "$scratch/response")
  original=$(key); body='{"title":"Private recovered receipt title","version":1}'
  before=$(effects)
  for attempt in 1 2; do
    test "$(request member PATCH "/cards/$card" "$body" "$original")" = 404
    jq -e '.code=="card_not_found"' "$scratch/response" >/dev/null
    test "$before" = "$(effects)"
  done
  test "$(request owner PATCH "/boards/$board/members/$member" '{"role":"MEMBER"}')" = 200
  before=$(effects)
  test "$(request member PATCH "/cards/$card" "$body" "$original")" = 200
  jq -e '.title=="Private recovered receipt title" and .version==2' "$scratch/response" >/dev/null
  cp "$scratch/response" "$scratch/original.receipt"
  committed=$(effects); test "$before" != "$committed"
  test "$(request member PATCH "/cards/$card" "$body" "$original")" = 200
  cmp "$scratch/original.receipt" "$scratch/response"; test "$committed" = "$(effects)"
  # A later committed revision must survive recovery of an earlier acknowledgment.
  test "$(request owner PATCH "/cards/$card" '{"title":"Later authoritative title","version":2}')" = 200
  jq -e '.version==3' "$scratch/response" >/dev/null
  later=$(effects)
  test "$(request member PATCH "/cards/$card" "$body" "$original")" = 200
  cmp "$scratch/original.receipt" "$scratch/response"; test "$later" = "$(effects)"
  test "$(request owner DELETE "/boards/$board/members/$member" '{}')" = 204
  withdrawn=$(effects)
  test "$(request member PATCH "/cards/$card" "$body" "$original")" = 404
  jq -e '.code=="card_not_found"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Private recovered receipt title' "$scratch/response"
  test "$withdrawn" = "$(effects)"
  test "$(request owner PATCH "/boards/$board/members/$member" '{"role":"MEMBER"}')" = 200
  regranted=$(effects)
  test "$(request member PATCH "/cards/$card" "$body" "$original")" = 200
  cmp "$scratch/original.receipt" "$scratch/response"; test "$regranted" = "$(effects)"
  test "$(admin "SELECT title||':'||version FROM cards WHERE id='$card';")" = 'Later authoritative title:3'
  # An archived List/Card permits read-only recovery but freezes new edits.
  for container in list card; do
    path="/lists/$list"; version=1
    if test "$container" = card; then path="/cards/$card"; version=3; fi
    test "$(request owner POST "$path/archive" "{\"version\":$version}")" = 200
    jq -e --argjson v "$((version + 1))" '.version==$v' "$scratch/response" >/dev/null
    frozen=$(effects)
    test "$(request member PATCH "/cards/$card" "$body" "$original")" = 200
    cmp "$scratch/original.receipt" "$scratch/response"; test "$frozen" = "$(effects)"
    current=$(admin "SELECT version FROM cards WHERE id='$card';")
    test "$(request member PATCH "/cards/$card" "{\"title\":\"Forbidden archived edit\",\"version\":$current}")" = 404
    jq -e '.code=="card_not_found"' "$scratch/response" >/dev/null
    test "$frozen" = "$(effects)"
    restore_actor=owner
    if test "$container" = list; then
      test "$(request member POST "$path/restore" "{\"version\":$((version + 1))}")" = 404
    else
      # A contributor may restore a Card on an active List/Board, but permanent
      # deletion remains elevated even when the Card is already archived.
      test "$(request member DELETE "$path?version=$((version + 1))&confirmed=true" '{}')" = 404
      restore_actor=member
    fi
    test "$frozen" = "$(effects)"
    test "$(request "$restore_actor" POST "$path/restore" "{\"version\":$((version + 1))}")" = 200
    jq -e --argjson v "$((version + 2))" '.version==$v' "$scratch/response" >/dev/null
    restored=$(effects)
    test "$(request member PATCH "/cards/$card" "$body" "$original")" = 200
    cmp "$scratch/original.receipt" "$scratch/response"; test "$restored" = "$(effects)"
  done

  # Archived Board authority withholds old receipts until elevated restoration.
  test "$(request owner POST "/boards/$board/archive" '{"version":1}')" = 200
  frozen=$(effects)
  for retry in "$original" "$(key)"; do
    test "$(request member PATCH "/cards/$card" "$body" "$retry")" = 404
    jq -e '.code=="card_not_found"' "$scratch/response" >/dev/null
    scripts/ci/assert-file-excludes.sh 'Private recovered receipt title' "$scratch/response"
    test "$frozen" = "$(effects)"
  done
  test "$(request member POST "/boards/$board/restore" '{"version":2}')" = 404
  test "$frozen" = "$(effects)"
  test "$(request owner POST "/boards/$board/restore" '{"version":2}')" = 200
  restored=$(effects)
  test "$(request member PATCH "/cards/$card" "$body" "$original")" = 200
  cmp "$scratch/original.receipt" "$scratch/response"; test "$restored" = "$(effects)"
  test "$(admin "SELECT title||':'||version FROM cards WHERE id='$card';")" = 'Later authoritative title:5'

  # A permanent Card tombstone must withhold the earlier edit receipt.
  test "$(request owner POST "/cards/$card/archive" '{"version":5}')" = 200
  test "$(request owner DELETE "/cards/$card?version=6&confirmed=true" '{}')" = 200
  deleted=$(effects)
  test "$(request member PATCH "/cards/$card" "$body" "$original")" = 404
  jq -e '.code=="card_not_found"' "$scratch/response" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Private recovered receipt title' "$scratch/response"
  test "$deleted" = "$(effects)"
  # List tombstones retain their child records, but must withhold all ordinary
  # child receipts, including a previously committed restore acknowledgment.
  test "$(request owner POST "/boards/$board/lists" '{"name":"Deleted parent receipt List"}')" = 201
  list=$(jq -r '.id' "$scratch/response")
  test "$(request owner POST "/lists/$list/cards" '{"title":"Deleted parent receipt Card"}')" = 201
  card=$(jq -r '.id' "$scratch/response")
  original=$(key); body='{"title":"Private deleted parent receipt","version":1}'
  test "$(request member PATCH "/cards/$card" "$body" "$original")" = 200
  test "$(request owner POST "/cards/$card/archive" '{"version":2}')" = 200
  restore_key=$(key)
  test "$(request member POST "/cards/$card/restore" '{"version":3}' "$restore_key")" = 200
  # Each sibling receipt first commits and replays under current admission.
  # Retain its exact original intent for replay after parent deletion.
  declare -A child_paths child_bodies child_keys child_codes
  for child in comment checklist attachment; do
    version=$(admin "SELECT version FROM cards WHERE id='$card';")
    case "$child" in
      comment) path="/cards/$card/comments"; child_body=$(jq -nc --argjson v "$version" '{content:"Private deleted-parent sibling",cardVersion:$v}'); code=comment_not_found;;
      checklist) path="/cards/$card/checklists"; child_body=$(jq -nc --argjson v "$version" '{title:"Private deleted-parent sibling",cardVersion:$v}'); code=card_not_found;;
      attachment) path="/cards/$card/attachments/url"; child_body=$(jq -nc --argjson v "$version" '{title:"Private deleted-parent sibling",url:"https://example.test/deleted-parent",cardVersion:$v}'); code=card_not_found;;
    esac
    child_paths[$child]=$path; child_bodies[$child]=$child_body; child_keys[$child]=$(key); child_codes[$child]=$code
    test "$(request member POST "$path" "$child_body" "${child_keys[$child]}")" = 200
    cp "$scratch/response" "$scratch/$child.receipt"
    committed=$(effects)
    test "$(request member POST "$path" "$child_body" "${child_keys[$child]}")" = 200
    cmp "$scratch/$child.receipt" "$scratch/response"; test "$committed" = "$(effects)"
  done
  current=$(admin "SELECT version FROM cards WHERE id='$card';")
  test "$(request owner POST "/lists/$list/archive" '{"version":1}')" = 200
  test "$(request owner DELETE "/lists/$list?version=2&confirmed=true&containedCardCount=1" '{}')" = 200
  deleted=$(effects)
  jq -e --argjson v "$current" '.list.lifecycle_state=="DELETED" and .list.version==3 and .card.lifecycle_state=="ACTIVE" and .card.version==$v and (.comments|length)==1 and (.checklists|length)==1 and (.attachments|length)==1' <<< "$deleted" >/dev/null
  for child in comment checklist attachment; do
    fresh_body=$(jq -c --argjson v "$current" '.cardVersion=$v' <<< "${child_bodies[$child]}")
    for retry in "${child_keys[$child]}" "$(key)"; do
      denied_body=${child_bodies[$child]}
      if test "$retry" != "${child_keys[$child]}"; then denied_body=$fresh_body; fi
      test "$(request member POST "${child_paths[$child]}" "$denied_body" "$retry")" = 404
      jq -e --arg code "${child_codes[$child]}" '.code==$code' "$scratch/response" >/dev/null
      scripts/ci/assert-file-excludes.sh 'Private deleted-parent sibling' "$scratch/response"
      test "$deleted" = "$(effects)"
    done
  done
  for command in edit restore; do
    method=PATCH; path="/cards/$card"; denied_body=$body; retry=$original
    if test "$command" = restore; then method=POST; path="/cards/$card/restore"; denied_body='{"version":3}'; retry=$restore_key; fi
    test "$(request member "$method" "$path" "$denied_body" "$retry")" = 404
    jq -e '.code=="card_not_found"' "$scratch/response" >/dev/null
    scripts/ci/assert-file-excludes.sh 'Private deleted parent receipt' "$scratch/response"
    test "$deleted" = "$(effects)"
  done
  test "$(request member PATCH "/cards/$card" "{\"title\":\"Forbidden deleted-parent edit\",\"version\":$current}")" = 404
  test "$deleted" = "$(effects)"
  test "$(request member POST "/cards/$card/restore" "{\"version\":$current}")" = 404
  test "$deleted" = "$(effects)"
done
echo 'Board permission recovery: all three visibilities preserve refused-key recovery, original receipts, later state, revoked admission, archived-parent freezes, elevated restoration and deleted Card/parent receipt withholding.'
