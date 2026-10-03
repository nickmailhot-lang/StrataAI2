#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable attachment fixtures may run only in CI.' >&2; exit 1; }
umask 077
base=http://localhost:8088
scratch=$(mktemp -d)
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  admin 'GRANT INSERT ON attachments,audit_events,work_events,background_jobs,work_command_replays TO strataai_api_runtime;' >/dev/null || true
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Attachment API check failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
for actor in owner member outsider; do
  jq -nc --arg email "attachments-$actor-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"attachment-fixture-battery-horse",displayName:"Attachment fixture"}' > "$scratch/$actor.credentials"
  curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/register" > "$scratch/$actor.user"
  curl --fail --silent --show-error -c "$scratch/$actor.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/$actor.credentials")" "$base/auth/login" >/dev/null
done
owner=$(jq -r '.user.id' "$scratch/owner.user")
member=$(jq -r '.user.id' "$scratch/member.user")
org=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Attachment transactions"}' "$base/organizations" | jq -r '.organization.id')
board=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Private attachment Board",visibility:"PRIVATE"}')" "$base/boards" | jq -r '.id')
list=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"name":"Attachment List"}' "$base/boards/$board/lists" | jq -r '.id')
card=$(curl --fail --silent --show-error -b "$scratch/owner.cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d '{"title":"Attachment Card"}' "$base/lists/$list/cards" | jq -r '.id')
for id in "$owner" "$member" "$org" "$board" "$list" "$card"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
admin "INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES(gen_random_uuid(),'$org','$member','MEMBER','ACTIVE');
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at) VALUES(gen_random_uuid(),'$org','$board','$member','MEMBER','ACTIVE',now(),now());" >/dev/null
request() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -X "$2" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: $4" -d "$5" -o "$scratch/response.json" -w '%{http_code}' "$base$3"; }
read_page() { curl --max-time 60 --silent --show-error -b "$scratch/$1.cookies" -o "$scratch/page.json" -w '%{http_code}' "$base$2"; }
read_anonymous() { curl --max-time 60 --silent --show-error -o "$scratch/page.json" -w '%{http_code}' "$base$1"; }
state() { admin "SELECT md5(jsonb_build_object(
 'card',(SELECT to_jsonb(c) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'metadata',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM attachments a WHERE tenant_id='$org'),
 'audits',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
path="/cards/$card/attachments"; key=$(uuid)
# Default release binary delivery remains disabled, but both file paths must
# reach the exact API image rather than succeeding with the SPA index.
download_id=$(uuid)
test "$(read_page owner "$path/$download_id/download")" = 404
test "$(read_page owner "/attachments/$download_id/download?cardId=$card")" = 404
payload='{"title":" Link ","url":"https://example.test/private-attachment?q=1#section","cardVersion":1}'
test "$(request owner POST "$path/url" "$key" "$payload")" = 200
cp "$scratch/response.json" "$scratch/receipt.json"
jq -e --arg card "$card" --arg org "$org" --arg actor "$owner" '.cardId==$card and .organizationId==$org and .cardVersion==2 and .attachment.cardId==$card and .attachment.uploaderId==$actor and .attachment.displayName=="Link" and .attachment.url=="https://example.test/private-attachment?q=1#section" and .attachment.mimeType==null and .attachment.sizeBytes==null and .attachment.scannedAt==null and (.attachment|has("storageKey")|not)' "$scratch/response.json" >/dev/null
attachment=$(jq -r '.attachment.id' "$scratch/response.json"); [[ "$attachment" =~ ^[0-9a-fA-F-]{36}$ ]]
test "$(admin "SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND event_type='ATTACHMENT_ADDED' AND entity_id='$attachment'")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND event_type='ATTACHMENT_ADDED' AND entity_id='$card' AND entity_version=2")" = 1
test "$(admin "SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND job_type='WORK_EVENT_READY' AND safe_metadata->>'eventId' IN(SELECT event_id::text FROM work_events WHERE tenant_id='$org' AND event_type='ATTACHMENT_ADDED')")" = 1
before=$(state)
test "$(request owner POST "$path/url" "$key" "$payload")" = 200
cmp "$scratch/receipt.json" "$scratch/response.json"
test "$before" = "$(state)"
test "$(request owner POST "$path/url" "$key" '{"title":"Changed","url":"https://example.test/","cardVersion":1}')" = 409
test "$(request owner POST "$path/url" "$(uuid)" "$payload")" = 409
test "$before" = "$(state)"
for url in 'javascript:alert(1)' 'data:text/html,bad' 'file:///private' 'https://user:secret@example.test/'; do
  body=$(jq -nc --arg url "$url" '{title:"Unsafe",url:$url,cardVersion:2}')
  test "$(request owner POST "$path/url" "$(uuid)" "$body")" = 400
  test "$before" = "$(state)"
done
test "$(request outsider POST "$path/url" "$(uuid)" '{"title":"","url":"javascript:bad","cardVersion":0}')" = 404
test "$(read_page outsider "$path?after=bad")" = 404
test "$(read_anonymous "$path")" = 401
test "$before" = "$(state)"
# Each failure occurs after Card CAS or metadata insertion, and must roll back
# all effects including the receipt claim. Retry the same original intent/key.
retry_key=$(uuid); retry_body='{"title":"Atomic retry","url":"https://example.test/retry","cardVersion":2}'
for table in attachments audit_events work_events background_jobs; do
  admin "REVOKE INSERT ON $table FROM strataai_api_runtime;" >/dev/null
  test "$(request member POST "$path/url" "$retry_key" "$retry_body")" = 503
  jq -e '.code=="work_storage_unavailable"' "$scratch/response.json" >/dev/null
  test "$before" = "$(state)"
  admin "GRANT INSERT ON $table TO strataai_api_runtime;" >/dev/null
done
test "$(request member POST "$path/url" "$retry_key" "$retry_body")" = 200
cp "$scratch/response.json" "$scratch/member-receipt.json"
test "$(admin "SELECT version FROM cards WHERE tenant_id='$org' AND id='$card'")" = 3
test "$(admin "SELECT count(*) FROM attachments WHERE tenant_id='$org'")" = 2
before=$(state)
test "$(request member POST "$path/url" "$retry_key" "$retry_body")" = 200
cmp "$scratch/member-receipt.json" "$scratch/response.json"
test "$before" = "$(state)"
# Populate actual PostgreSQL ties to prove the adapter's newest-first seek,
# without claiming bulk fixture inserts are production commands/events.
admin "INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$card','$owner','URL','Paged link '||n,'https://example.test/page/'||n,'NOT_APPLICABLE',now(),now() FROM generate_series(1,61) n;" >/dev/null
test "$(read_page member "$path")" = 200
jq -e '.items|length==50' "$scratch/page.json" >/dev/null
cursor=$(jq -r '.nextCursor' "$scratch/page.json"); test "$cursor" != null
jq -r '.items[].id' "$scratch/page.json" > "$scratch/ids"
encoded=$(jq -nr --arg cursor "$cursor" '$cursor|@uri')
test "$(read_page member "$path?after=$encoded")" = 200
jq -e '(.items|length)==13 and .nextCursor==null and .cardVersion==3' "$scratch/page.json" >/dev/null
jq -r '.items[].id' "$scratch/page.json" >> "$scratch/ids"
test "$(sort -u "$scratch/ids" | wc -l)" = 63
test "$(read_page member "$path?after=bad")" = 400
# Revocation fences both a successful previous receipt and validation details.
test "$(request owner DELETE "/boards/$board/members/$member" "$(uuid)" '{}')" = 204
before=$(state)
test "$(request member POST "$path/url" "$retry_key" "$retry_body")" = 404
test "$(read_page member "$path?after=bad")" = 404
test "$before" = "$(state)"
test "$(request owner POST "/lists/$list/archive" "$(uuid)" '{"version":1}')" = 200
test "$(read_page owner "$path")" = 200
jq -e '.canEdit==false and (.items|length)==50' "$scratch/page.json" >/dev/null
before=$(state)
test "$(request owner POST "$path/url" "$key" "$payload")" = 404
test "$before" = "$(state)"
echo 'Exact-image URL attachments: canonical receipts, Card CAS, authorization, validation, four transactional rollback boundaries, outbox, cursor paging, revocation and archived retention passed.'
