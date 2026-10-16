# Run with the identity fixture's verified owner, private cookie jar and helpers.
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
test "$(query "SELECT count(*) FROM organization_members WHERE tenant_id='$signup_org' AND user_id='$mail_owner_id' AND role='OWNER' AND status='ACTIVE';")" = 1
for state in outage conflicts; do
  core_call GET "$fixture/$state" '' none; test "$core_status" = 200
  jq -e '.active==false' <<< "$core_body" >/dev/null
done
inv_conflict_processes=$(core_mail_process_identity); test -n "$inv_conflict_processes"
inv_closed_body=$(jq -nc --arg email "closed-conflict-$(core_uuid)@example.test" '{email:$email,password:"conflict-signup-correct-horse",displayName:"Closed signup"}')
core_call POST "$base/auth/register" "$inv_closed_body" none "$(core_uuid)"
test "$core_status" = 403; jq -e '.code=="self_registration_disabled"' <<< "$core_body" >/dev/null
# Obtain a genuine delivered invitation so registration also works when public
# self-registration is closed. This bootstrap is outside the conflict interval.
inv_conflict_email="conflict-signup-$(core_uuid)@example.test"
inv_conflict_payload=$(jq -nc --arg email "$inv_conflict_email" '{email:$email,surface:"INTERNAL",targetRole:"MEMBER"}')
core_call POST "$base/organizations/$signup_org/invitations" "$inv_conflict_payload" owner "$(core_uuid)"
test "$core_status" = 201
inv_bootstrap=$(jq -er '.id' <<< "$core_body"); [[ "$inv_bootstrap" =~ ^[a-f0-9-]{36}$ ]]
inv_bootstrap_key="strataai-invitation/${signup_org//-/}/${inv_bootstrap//-/}"
for attempt in $(seq 1 90); do
  if [ "$(query "SELECT count(*) FROM invitation_mail_intents WHERE invitation_id='$inv_bootstrap' AND tenant_id='$signup_org' AND state='SENT';")" = 1 ]; then break; fi
  sleep 1
done
test "$(query "SELECT count(*) FROM invitation_mail_intents WHERE invitation_id='$inv_bootstrap' AND tenant_id='$signup_org' AND state='SENT';")" = 1
core_call GET "$fixture/messages" '' none; test "$core_status" = 200
inv_bootstrap_proof=$(jq -er --arg key "$inv_bootstrap_key" '.[]|select(.key==$key)|.payload.text|capture("/invitation#token=(?<token>[A-Za-z0-9_-]+)").token' <<< "$core_body")
[[ "$inv_bootstrap_proof" =~ ^[A-Za-z0-9_-]+$ ]]
mail_conflict_owned=1
core_call POST "$fixture/control" '{"conflict":true}' none; test "$core_status" = 200
inv_signup_body=$(jq -nc --arg email "$inv_conflict_email" --arg token "$inv_bootstrap_proof" '{email:$email,password:"conflict-signup-correct-horse",displayName:"Conflict recipient",invitationToken:$token}')
inv_signup_key=$(core_uuid)
core_call POST "$base/auth/register" "$inv_signup_body" none "$inv_signup_key"
test "$core_status" = 201; jq -e '.user.emailVerified==false and .verificationToken==null' <<< "$core_body" >/dev/null
inv_signup_user=$(jq -er '.user.id' <<< "$core_body"); [[ "$inv_signup_user" =~ ^[a-f0-9-]{36}$ ]]
inv_signup_ack=$core_body
core_call POST "$base/auth/register" "$inv_signup_body" none "$inv_signup_key"
test "$core_status" = 201; test "$core_body" = "$inv_signup_ack"
test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE user_id='$inv_signup_user' AND purpose='VERIFY_EMAIL';")" = 1
inv_verify_job=$(query "SELECT id FROM identity_delivery_jobs WHERE user_id='$inv_signup_user' AND purpose='VERIFY_EMAIL';")
[[ "$inv_verify_job" =~ ^[a-f0-9-]{36}$ ]]
inv_verify_key="strataai-identity/verify/${inv_verify_job//-/}"
core_call POST "$base/auth/login" "$inv_signup_body" none
test "$core_status" = 403; jq -e '.code=="email_verification_required"' <<< "$core_body" >/dev/null
inv_conflict_payload=$(jq -nc --arg email "conflict-invite-$(core_uuid)@example.test" '{email:$email,surface:"INTERNAL",targetRole:"MEMBER"}')
inv_request_key=$(core_uuid)
core_call POST "$base/organizations/$signup_org/invitations" "$inv_conflict_payload" owner "$inv_request_key"
test "$core_status" = 201; jq -e '.invitationToken==null' <<< "$core_body" >/dev/null
inv_conflict_id=$(jq -er '.id' <<< "$core_body"); [[ "$inv_conflict_id" =~ ^[a-f0-9-]{36}$ ]]
inv_conflict_ack=$core_body
core_call POST "$base/organizations/$signup_org/invitations" "$inv_conflict_payload" owner "$inv_request_key"
test "$core_status" = 201; test "$core_body" = "$inv_conflict_ack"
test "$(query "SELECT count(*) FROM invitation_mail_intents WHERE invitation_id='$inv_conflict_id' AND tenant_id='$signup_org';")" = 1
inv_delivery_job=$(query "SELECT job_id FROM invitation_mail_intents WHERE invitation_id='$inv_conflict_id' AND tenant_id='$signup_org';")
[[ "$inv_delivery_job" =~ ^[a-f0-9-]{36}$ ]]
inv_provider_key="strataai-invitation/${signup_org//-/}/${inv_conflict_id//-/}"
echo 'Verification/invitation conflict: waiting for both real Worker retry decisions.'
inv_retry_sql="SELECT count(*) FROM background_jobs j JOIN invitation_mail_intents i ON i.job_id=j.id AND i.tenant_id=j.tenant_id WHERE j.id='$inv_delivery_job' AND j.tenant_id='$signup_org' AND j.state='PENDING' AND j.attempt_count>=1 AND j.available_at>clock_timestamp() AND j.last_error_code='job_handler_failed' AND j.worker_id IS NULL AND j.lease_id IS NULL AND j.lease_expires_at IS NULL AND i.state='PENDING' AND i.provider_receipt_id IS NULL AND i.safe_error_code IS NULL;"
verify_retry_sql="SELECT count(*) FROM identity_delivery_jobs WHERE id='$inv_verify_job' AND state='PENDING' AND attempt_count>=1 AND available_at>clock_timestamp() AND last_error_code='identity_provider_unavailable' AND provider_message_id IS NULL AND worker_id IS NULL AND lease_id IS NULL AND lease_expires_at IS NULL;"
for attempt in $(seq 1 60); do
  if [ "$(query "$inv_retry_sql")" = 1 ] && [ "$(query "$verify_retry_sql")" = 1 ]; then break; fi
  sleep 1
done
test "$(query "$inv_retry_sql")" = 1; test "$(query "$verify_retry_sql")" = 1
for key in "$inv_verify_key" "$inv_provider_key"; do
  core_call GET "$fixture/conflicts" '' none; test "$core_status" = 200
  jq -e --arg key "$key" '.active==true and ([.attempts[]|select(.key==$key)]|length==1 and .[0].attempts>=1 and .[0].mismatches==0)' <<< "$core_body" >/dev/null
  core_call GET "$fixture/messages" '' none; test "$core_status" = 200
  jq -e --arg key "$key" '[.[]|select(.key==$key)]|length==0' <<< "$core_body" >/dev/null
done
test "$(query "SELECT count(*) FROM organization_members WHERE tenant_id='$signup_org' AND user_id='$inv_signup_user';")" = 0
core_call GET "$base/readyz" '' none; test "$core_status" = 200
core_call GET "$fixture/health" '' none; test "$core_status" = 200
jq -e --arg instance "$STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE" '.ready==true and .instance==$instance' <<< "$core_body" >/dev/null
core_call POST "$fixture/control" '{"conflict":false}' none; test "$core_status" = 200
mail_conflict_owned=0
inv_complete_sql="SELECT count(*) FROM background_jobs j JOIN invitation_mail_intents i ON i.job_id=j.id AND i.tenant_id=j.tenant_id WHERE j.id='$inv_delivery_job' AND j.tenant_id='$signup_org' AND j.state='SUCCEEDED' AND j.attempt_count>=3 AND j.last_error_code IS NULL AND j.worker_id IS NULL AND j.lease_id IS NULL AND j.lease_expires_at IS NULL AND i.state='SENT' AND i.safe_error_code IS NULL;"
verify_complete_sql="SELECT count(*) FROM identity_delivery_jobs WHERE id='$inv_verify_job' AND state='SENT' AND attempt_count>=3 AND last_error_code IS NULL AND worker_id IS NULL AND lease_id IS NULL AND lease_expires_at IS NULL;"
for attempt in $(seq 1 240); do
  if [ "$(query "$inv_complete_sql")" = 1 ] && [ "$(query "$verify_complete_sql")" = 1 ]; then break; fi
  sleep 1
done
test "$(query "$inv_complete_sql")" = 1; test "$(query "$verify_complete_sql")" = 1
for key in "$inv_verify_key" "$inv_provider_key"; do
  core_call GET "$fixture/messages" '' none; test "$core_status" = 200
  jq -e --arg key "$key" '[.[]|select(.key==$key)]|length==1 and .[0].attempts>=2' <<< "$core_body" >/dev/null
  inv_receipt=$(jq -er --arg key "$key" '.[]|select(.key==$key)|.id' <<< "$core_body")
  [[ "$inv_receipt" =~ ^[a-f0-9-]{36}$ ]]
  if [ "$key" = "$inv_verify_key" ]; then
    test "$(query "SELECT count(*) FROM identity_delivery_jobs WHERE id='$inv_verify_job' AND provider_message_id='$inv_receipt';")" = 1
    inv_verify_proof=$(jq -er --arg key "$key" '.[]|select(.key==$key)|.payload.text|capture("/verify-email#token=(?<token>[A-Za-z0-9_-]+)").token' <<< "$core_body")
  else
    test "$(query "SELECT count(*) FROM invitation_mail_intents WHERE job_id='$inv_delivery_job' AND tenant_id='$signup_org' AND provider_receipt_id='$inv_receipt';")" = 1
  fi
  core_call GET "$fixture/conflicts" '' none; test "$core_status" = 200
  jq -e --arg key "$key" '.active==false and ([.attempts[]|select(.key==$key)]|length==1 and .[0].mismatches==0)' <<< "$core_body" >/dev/null
done
core_call POST "$base/auth/verify-email" "$(jq -nc --arg token "$inv_verify_proof" '{token:$token}')" none "$(core_uuid)"
test "$core_status" = 200
core_call POST "$base/auth/login" "$inv_signup_body" none
test "$core_status" = 200; jq -e '.user.emailVerified==true' <<< "$core_body" >/dev/null
test "$(query "SELECT count(*) FROM organization_members WHERE tenant_id='$signup_org' AND user_id='$inv_signup_user';")" = 0
core_call GET "$base/me"; test "$core_status" = 200
core_call GET "$base/readyz" '' none; test "$core_status" = 200
test "$inv_conflict_processes" = "$(core_mail_process_identity)"
unset inv_closed_body inv_signup_body inv_signup_ack inv_conflict_payload inv_conflict_ack inv_bootstrap_proof inv_verify_proof inv_receipt core_body
echo 'Verification/invitation conflict passed: closed-signup proof, one intent each, pending real retries, original payloads, single recovered receipts, strict verification, no premature membership and unchanged processes.'
