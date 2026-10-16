# Sourced after core-mail-outage.inc.sh with its verified owner and helpers.
[[ "${CI:-}" == true && "${GITHUB_ACTIONS:-}" == true ]] || return 1
[[ "${STRATAAI_IMAGE_TAG:-}" =~ ^[a-f0-9]{40}$|^[a-f0-9]{64}$ ]] || return 1
test -n "${STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE:-}"
for helper in core_call core_uuid query core_mail_process_identity; do declare -F "$helper" >/dev/null; done
for id in "$mail_owner_id" "$signup_org"; do
  [[ "$id" =~ ^[a-fA-F0-9]{8}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{4}-[a-fA-F0-9]{12}$ ]]
done
core_call GET "$base/api/runtime" '' none; test "$core_status" = 200
jq -e --arg revision "$STRATAAI_IMAGE_TAG" '.service=="strataai-api" and .mode=="production" and .revision==$revision' <<< "$core_body" >/dev/null
core_call GET "$fixture/health" '' none; test "$core_status" = 200
jq -e --arg instance "$STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE" '.ready==true and .instance==$instance' <<< "$core_body" >/dev/null
test "$(query "SELECT count(*) FROM users WHERE id='$mail_owner_id' AND email_verified AND status='ACTIVE';")" = 1
core_call GET "$fixture/outage" '' none; test "$core_status" = 200
jq -e '.active==false' <<< "$core_body" >/dev/null
core_call GET "$fixture/conflicts" '' none; test "$core_status" = 200
jq -e '.active==false' <<< "$core_body" >/dev/null
conflict_processes=$(core_mail_process_identity); test -n "$conflict_processes"
conflict_before=$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$mail_owner_id' AND purpose='RESET_PASSWORD';")
[[ "$conflict_before" =~ ^[0-9]+$ ]]
mail_conflict_owned=1
core_call POST "$fixture/control" '{"conflict":true}' none; test "$core_status" = 200
conflict_request=$(core_uuid)
conflict_data=$(jq -c '{email:.email}' <<< "$mail_owner_body")
core_call POST "$base/auth/password/forgot" "$conflict_data" none "$conflict_request"
test "$core_status" = 202; jq -e '.accepted==true and .resetToken==null' <<< "$core_body" >/dev/null
conflict_ack=$core_body
core_call POST "$base/auth/password/forgot" "$conflict_data" none "$conflict_request"
test "$core_status" = 202; test "$core_body" = "$conflict_ack"
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$mail_owner_id' AND purpose='RESET_PASSWORD';")" = "$((conflict_before+1))"
conflict_job=$(query "SELECT id FROM identity_delivery_jobs WHERE user_id='$mail_owner_id' AND purpose='RESET_PASSWORD' ORDER BY created_at DESC,id DESC LIMIT 1;")
[[ "$conflict_job" =~ ^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$ ]]
conflict_key="strataai-identity/reset/${conflict_job//-/}"
echo 'Identity mail conflict: waiting for the real Worker retry decision.'
for attempt in $(seq 1 60); do
  if [ "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE id='$conflict_job' AND state='PENDING' AND attempt_count>=1 AND last_error_code='identity_provider_unavailable' AND provider_message_id IS NULL AND worker_id IS NULL AND lease_id IS NULL AND lease_expires_at IS NULL AND available_at>clock_timestamp();")" = 1 ]; then break; fi
  sleep 1
done
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE id='$conflict_job' AND state='PENDING' AND attempt_count>=1 AND last_error_code='identity_provider_unavailable' AND provider_message_id IS NULL AND worker_id IS NULL AND lease_id IS NULL AND lease_expires_at IS NULL AND available_at>clock_timestamp();")" = 1
core_call GET "$fixture/conflicts" '' none; test "$core_status" = 200
jq -e --arg key "$conflict_key" '.active==true and ([.attempts[]|select(.key==$key)]|length==1 and .[0].attempts>=1 and .[0].mismatches==0)' <<< "$core_body" >/dev/null
core_call GET "$fixture/messages" '' none; test "$core_status" = 200
jq -e --arg key "$conflict_key" '[.[]|select(.key==$key)]|length==0' <<< "$core_body" >/dev/null
core_call GET "$base/readyz" '' none; test "$core_status" = 200
core_call GET "$base/me"; test "$core_status" = 200
core_call GET "$fixture/health" '' none; test "$core_status" = 200
jq -e --arg instance "$STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE" '.ready==true and .instance==$instance' <<< "$core_body" >/dev/null
core_call POST "$fixture/control" '{"conflict":false}' none; test "$core_status" = 200
mail_conflict_owned=0
for attempt in $(seq 1 240); do
  if [ "$(query "SELECT state FROM identity_delivery_jobs WHERE id='$conflict_job';")" = SENT ]; then break; fi
  sleep 1
done
test "$(query "SELECT state FROM identity_delivery_jobs WHERE id='$conflict_job';")" = SENT
core_call GET "$fixture/messages" '' none; test "$core_status" = 200
jq -e --arg key "$conflict_key" '[.[]|select(.key==$key)]|length==1 and .[0].attempts>=2' <<< "$core_body" >/dev/null
conflict_receipt=$(jq -r --arg key "$conflict_key" '.[]|select(.key==$key)|.id' <<< "$core_body")
[[ "$conflict_receipt" =~ ^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$ ]]
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE id='$conflict_job' AND state='SENT' AND attempt_count>=3 AND provider_message_id='$conflict_receipt' AND last_error_code IS NULL AND worker_id IS NULL AND lease_id IS NULL AND lease_expires_at IS NULL;")" = 1
core_call GET "$fixture/conflicts" '' none; test "$core_status" = 200
jq -e --arg key "$conflict_key" '.active==false and ([.attempts[]|select(.key==$key)]|length==1 and .[0].attempts>=1 and .[0].mismatches==0)' <<< "$core_body" >/dev/null
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$mail_owner_id' AND purpose='RESET_PASSWORD';")" = "$((conflict_before+1))"
core_call GET "$base/readyz" '' none; test "$core_status" = 200
core_call GET "$base/me"; test "$core_status" = 200
test "$conflict_processes" = "$(core_mail_process_identity)"
unset conflict_data conflict_request conflict_ack conflict_receipt core_body
echo 'Identity mail conflict passed: one durable intent, real 409 retry/backoff, unchanged key/body, one recovered effect/receipt, cleared lease/error and unchanged API/Worker.'
