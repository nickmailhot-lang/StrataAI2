# Sourced only after the identity fixture has verified its owner through mail.
# The caller supplies its private cookie jar, query and process-identity helpers.
[[ "${CI:-}" == true && "${GITHUB_ACTIONS:-}" == true ]] || { echo 'Core mail outage requires the owned CI fixture.' >&2; return 1; }
[[ "${STRATAAI_IMAGE_TAG:-}" =~ ^[a-f0-9]{40}$|^[a-f0-9]{64}$ ]] || return 1
test -n "${STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE:-}"
for helper in query core_mail_process_identity; do declare -F "$helper" >/dev/null; done
for id in "$mail_owner_id" "$signup_org" "$mail_board"; do
  [[ "$id" =~ ^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$ ]]
done
core_uuid() {
  local raw; raw=$(openssl rand -hex 16)
  printf '%s-%s-%s-%s-%s' "${raw:0:8}" "${raw:8:4}" "${raw:12:4}" "${raw:16:4}" "${raw:20:12}"
}
core_call() {
  local method=$1 url=$2 data=${3:-} actor=${4:-owner} key=${5:-} reply
  local args=(--max-time 20 --silent --show-error --write-out $'\n%{http_code}' -X "$method" -H 'X-StrataAI-Request: 1')
  if [ "$actor" = owner ]; then args+=(-b "$scratch/signup-owner.cookies"); fi
  if [ -n "$data" ]; then args+=(-H 'Content-Type: application/json' --data "$data"); fi
  if [ -n "$key" ]; then args+=(-H "Idempotency-Key: $key"); fi
  reply=$(curl "${args[@]}" "$url")
  core_status=${reply##*$'\n'}; core_body=${reply%$'\n'*}
}
# Admit both process build and provider instance before any fixture mutation.
echo 'Core mail outage: checking owned Production API and provider admission.'
core_call GET "$base/api/runtime" '' none
test "$core_status" = 200
jq -e --arg revision "$STRATAAI_IMAGE_TAG" '.service=="strataai-api" and .mode=="production" and .revision==$revision' <<< "$core_body" >/dev/null
core_call GET "$fixture/health" '' none
test "$core_status" = 200
jq -e --arg instance "$STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE" '.ready==true and .instance==$instance' <<< "$core_body" >/dev/null
test "$(query "SELECT count(*) FROM users WHERE id='$mail_owner_id' AND email_verified AND status='ACTIVE';")" = 1
test "$(query "SELECT count(*) FROM organization_members WHERE tenant_id='$signup_org' AND user_id='$mail_owner_id' AND role='OWNER' AND status='ACTIVE';")" = 1
core_call GET "$base/boards/$mail_board"
test "$core_status" = 200
jq -e --arg board "$mail_board" --arg org "$signup_org" '.board.id==$board and .board.organizationId==$org and .board.visibility=="PRIVATE"' <<< "$core_body" >/dev/null
core_processes=$(core_mail_process_identity); test -n "$core_processes"
core_jobs_before=$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$mail_owner_id' AND purpose='RESET_PASSWORD';")
core_audit_before=$(query "SELECT count(*) FROM audit_events WHERE tenant_id='$signup_org' AND actor_id='$mail_owner_id' AND event_type IN ('LIST_CREATED','CARD_CREATED','CARD_UPDATED');")
[[ "$core_jobs_before" =~ ^[0-9]+$ && "$core_audit_before" =~ ^[0-9]+$ ]]
core_mail_outage_owned=1
echo 'Core mail outage: admitted verified owner; enabling controlled provider failure.'
core_call POST "$fixture/control" '{"outage":true}' none
test "$core_status" = 200; jq -e '.outage==true' <<< "$core_body" >/dev/null
core_reset_body=$(jq -c '{email:.email}' <<< "$mail_owner_body"); core_reset_key=$(core_uuid)
core_call POST "$base/auth/password/forgot" "$core_reset_body" none "$core_reset_key"
test "$core_status" = 202; jq -e '.accepted==true and .resetToken==null' <<< "$core_body" >/dev/null
core_ack="$core_body"
core_call POST "$base/auth/password/forgot" "$core_reset_body" none "$core_reset_key"
test "$core_status" = 202; test "$core_body" = "$core_ack"
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$mail_owner_id' AND purpose='RESET_PASSWORD';")" = "$((core_jobs_before+1))"
core_job=$(query "SELECT id FROM identity_delivery_jobs WHERE user_id='$mail_owner_id' AND purpose='RESET_PASSWORD' ORDER BY created_at DESC,id DESC LIMIT 1;")
[[ "$core_job" =~ ^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$ ]]
core_provider_key="strataai-identity/reset/${core_job//-/}"
for attempt in $(seq 1 60); do
  if [ "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE id='$core_job' AND state='PENDING' AND attempt_count>=1 AND last_error_code='identity_provider_unavailable';")" = 1 ]; then break; fi
  sleep 1
done
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE id='$core_job' AND state='PENDING' AND attempt_count>=1 AND last_error_code='identity_provider_unavailable';")" = 1
core_call GET "$fixture/outage" '' none
test "$core_status" = 200
jq -e --arg key "$core_provider_key" '.active==true and any(.attempts[];.key==$key and .attempts>=1)' <<< "$core_body" >/dev/null
core_call GET "$fixture/messages" '' none
jq -e --arg key "$core_provider_key" '[.[]|select(.key==$key)]|length==0' <<< "$core_body" >/dev/null
echo 'Core mail outage: actual Worker retry observed without a provider delivery effect.'

# No dependency bypass, lease edit, restart or shortened backoff during outage.
core_call GET "$base/readyz" '' none; test "$core_status" = 200
core_call POST "$base/boards/$mail_board/lists" '{"name":"MVP mail outage list"}' owner "$(core_uuid)"
test "$core_status" = 201; core_list=$(jq -r '.id' <<< "$core_body")
[[ "$core_list" =~ ^[a-f0-9-]{36}$ ]]
core_call POST "$base/lists/$core_list/cards" '{"title":"MVP mail outage task"}' owner "$(core_uuid)"
test "$core_status" = 201; core_card=$(jq -r '.id' <<< "$core_body")
jq -e '.version==1' <<< "$core_body" >/dev/null
[[ "$core_card" =~ ^[a-f0-9-]{36}$ ]]
core_call PATCH "$base/cards/$core_card" '{"title":"MVP mail outage edit","version":1}' owner "$(core_uuid)"
test "$core_status" = 200
jq -e --arg card "$core_card" '.id==$card and .title=="MVP mail outage edit" and .version==2' <<< "$core_body" >/dev/null
core_call GET "$base/boards/$mail_board"
test "$core_status" = 200
jq -e --arg card "$core_card" '[.lists[].cards[]|select(.id==$card)]|length==1 and .[0].title=="MVP mail outage edit" and .[0].version==2' <<< "$core_body" >/dev/null
test "$(query "SELECT count(*) FROM cards WHERE id='$core_card' AND tenant_id='$signup_org' AND title='MVP mail outage edit' AND version=2;")" = 1
test "$(query "SELECT count(*) FROM audit_events WHERE tenant_id='$signup_org' AND actor_id='$mail_owner_id' AND event_type IN ('LIST_CREATED','CARD_CREATED','CARD_UPDATED');")" = "$((core_audit_before+3))"
core_call GET "$base/boards/$mail_board" '' none; test "$core_status" = 404
[[ "$core_body" != *"MVP mail outage"* && "$core_body" != *"$core_card"* ]]
core_call GET "$base/me"; test "$core_status" = 200
core_call GET "$fixture/outage" '' none; jq -e '.active==true' <<< "$core_body" >/dev/null
test "$core_processes" = "$(core_mail_process_identity)"

echo 'Core mail outage: task persistence and access checks passed; recovering mail.'
core_call POST "$fixture/control" '{"outage":false}' none
test "$core_status" = 200; jq -e '.outage==false' <<< "$core_body" >/dev/null
core_mail_outage_owned=0
for attempt in $(seq 1 240); do
  if [ "$(query "SELECT state FROM identity_delivery_jobs WHERE id='$core_job';")" = SENT ]; then break; fi
  sleep 1
done
test "$(query "SELECT state FROM identity_delivery_jobs WHERE id='$core_job';")" = SENT
core_call GET "$fixture/messages" '' none
test "$core_status" = 200
jq -e --arg key "$core_provider_key" '[.[]|select(.key==$key)]|length==1 and .[0].attempts>=2' <<< "$core_body" >/dev/null
core_receipt=$(jq -r --arg key "$core_provider_key" '.[]|select(.key==$key)|.id' <<< "$core_body")
[[ "$core_receipt" =~ ^[a-f0-9-]{36}$ ]]
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE id='$core_job' AND state='SENT' AND provider_message_id='$core_receipt' AND worker_id IS NULL AND lease_id IS NULL AND lease_expires_at IS NULL AND last_error_code IS NULL;")" = 1
core_call GET "$base/readyz" '' none; test "$core_status" = 200
core_call GET "$base/boards/$mail_board"
test "$core_status" = 200
jq -e --arg card "$core_card" '[.lists[].cards[]|select(.id==$card)]|length==1 and .[0].title=="MVP mail outage edit" and .[0].version==2' <<< "$core_body" >/dev/null
test "$core_processes" = "$(core_mail_process_identity)"
unset core_reset_body core_reset_key core_ack core_body core_receipt
echo 'Core task work during mail outage passed: strict verified owner, one durable reset intent, actual retry without delivery effect, ready API, persisted List/Card/edit/audits, anonymous refusal, same processes and one recovered provider receipt.'
