#!/usr/bin/env bash
set -euo pipefail
export STRATAAI_API_DB_PASSWORD='ci-api-runtime-password'
export STRATAAI_WORKER_DB_PASSWORD='ci-worker-runtime-password'
api() { PGUSER=strataai_api_runtime PGPASSWORD="$STRATAAI_API_DB_PASSWORD" psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
worker() { PGUSER=strataai_worker_runtime PGPASSWORD="$STRATAAI_WORKER_DB_PASSWORD" psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
tenant=02300000-0000-0000-0000-000000000001
actor=02300000-0000-0000-0000-000000000002
invitation=02300000-0000-0000-0000-000000000003
job=02300000-0000-0000-0000-000000000004
worker_id=02300000-0000-0000-0000-000000000005
lease=02300000-0000-0000-0000-000000000006
other=02300000-0000-0000-0000-000000000007
psql -X -v ON_ERROR_STOP=1 <<SQL >/dev/null
BEGIN;
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
VALUES('$actor','mail-issuer@example.test','MAIL-ISSUER@EXAMPLE.TEST','Mail issuer','ACTIVE',true,'unusable-ci-fixture',now(),now());
INSERT INTO organizations(id,name,created_at,updated_at) VALUES('$tenant','Mail capability fixture',now(),now());
INSERT INTO organization_members(id,user_id,tenant_id,role,status) VALUES('$actor','$actor','$tenant','OWNER','ACTIVE');
INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at)
VALUES('$invitation','$tenant','invited@example.test','INVITED@EXAMPLE.TEST',repeat('a',64),'INTERNAL','MEMBER','$actor',now(),now()+interval '1 day');
INSERT INTO background_jobs(id,tenant_id,job_type,idempotency_key,actor_id,service_identity,correlation_id,safe_metadata,state,attempt_count,worker_id,lease_id,lease_expires_at)
VALUES('$job','$tenant','INVITATION_EMAIL','mail-fixture','$actor','invitation-email-delivery','mail-fixture',jsonb_build_object('invitationId','$invitation'::uuid),'RUNNING',1,'$worker_id','$lease',now()+interval '5 minutes');
INSERT INTO invitation_mail_intents(job_id,tenant_id,invitation_id,issuer_id,recipient_email,target_surface,target_role,expires_at,key_id,sender_address,public_origin,provider_account,template_version)
SELECT '$job',tenant_id,id,created_by_user_id,invited_email,target_surface,target_role,expires_at,'ci-key','sender@example.test','https://example.test','ci-mail',1 FROM invitations WHERE id='$invitation';
COMMIT;
SQL
load="public.load_invitation_mail('$job','$tenant','$actor','$worker_id','$lease',true)"
scoped() { worker "BEGIN; SET LOCAL app.tenant_id='$tenant'; $1; ROLLBACK;" | grep -E '^(t|f|[0-9]+|PENDING|SENT)$'; }
test "$(scoped "SELECT is_usable FROM $load")" = t
psql -X -v ON_ERROR_STOP=1 -c "INSERT INTO boards(id,tenant_id,name,created_at,updated_at)
 VALUES('02500000-0000-0000-0000-000000000010','$tenant','Mail target fixture',now(),now());
 UPDATE invitations SET target_board_id='02500000-0000-0000-0000-000000000010',target_board_role='MEMBER'
 WHERE id='$invitation';" >/dev/null
test "$(scoped "SELECT is_usable FROM $load")" = f
psql -X -v ON_ERROR_STOP=1 -c "UPDATE invitation_mail_intents
 SET target_board_id='02500000-0000-0000-0000-000000000010',target_board_role='MEMBER' WHERE job_id='$job';
 INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('02600000-0000-0000-0000-000000000011','invited@example.test','INVITED@EXAMPLE.TEST','Board recipient','ACTIVE',true,'unusable-ci-fixture',now(),now());
 INSERT INTO organization_members(id,user_id,tenant_id,role,status)
 VALUES('02600000-0000-0000-0000-000000000012','02600000-0000-0000-0000-000000000011','$tenant','MEMBER','ACTIVE');
 INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 VALUES('02600000-0000-0000-0000-000000000013','$tenant','02500000-0000-0000-0000-000000000010','$actor','ADMIN','ACTIVE',now(),now());" >/dev/null
test "$(scoped "SELECT is_usable FROM $load")" = t
board_reset() {
 psql -X -v ON_ERROR_STOP=1 -c "UPDATE boards SET lifecycle_state='ACTIVE' WHERE id='02500000-0000-0000-0000-000000000010';
 UPDATE board_members SET status='ACTIVE',role='ADMIN' WHERE id='02600000-0000-0000-0000-000000000013';
 UPDATE organization_members SET role='MEMBER',status='ACTIVE',version=version+1,updated_at=clock_timestamp() WHERE tenant_id='$tenant' AND user_id='$actor';
 UPDATE organization_members SET status='ACTIVE',version=version+1,updated_at=clock_timestamp() WHERE id='02600000-0000-0000-0000-000000000012';
 UPDATE users SET status='ACTIVE',email_verified=true WHERE id='02600000-0000-0000-0000-000000000011';
 UPDATE invitation_mail_intents SET target_board_role='MEMBER' WHERE job_id='$job';" >/dev/null
}
board_reset
test "$(scoped "SELECT is_usable FROM $load")" = t
for invalid_target in "target_board_id=NULL" "target_board_role=NULL" "target_board_role='OWNER'" "target_surface='PORTAL'"; do
 if psql -X -v ON_ERROR_STOP=1 -c "UPDATE invitation_mail_intents SET $invalid_target WHERE job_id='$job';" >/dev/null; then
  echo 'Invalid Board mail snapshot was admitted'; exit 1
 fi
done
for mutation in \
 "UPDATE boards SET lifecycle_state='ARCHIVED' WHERE id='02500000-0000-0000-0000-000000000010'" \
 "UPDATE board_members SET status='REMOVED' WHERE id='02600000-0000-0000-0000-000000000013'" \
 "UPDATE board_members SET role='MEMBER' WHERE id='02600000-0000-0000-0000-000000000013'" \
 "UPDATE users SET status='SUSPENDED' WHERE id='02600000-0000-0000-0000-000000000011'" \
 "UPDATE users SET email_verified=false WHERE id='02600000-0000-0000-0000-000000000011'" \
 "UPDATE organization_members SET status='REMOVED' WHERE id='02600000-0000-0000-0000-000000000012'" \
 "UPDATE invitation_mail_intents SET target_board_role='ADMIN' WHERE job_id='$job'"; do
 board_reset
 psql -X -v ON_ERROR_STOP=1 -c "$mutation" >/dev/null
 test "$(scoped "SELECT is_usable FROM $load")" = f
done
board_reset
test "$(scoped "SELECT is_usable FROM $load")" = t
psql -X -v ON_ERROR_STOP=1 -c "UPDATE organization_members SET role='OWNER' WHERE tenant_id='$tenant' AND user_id='$actor';
 DELETE FROM organization_members WHERE id='02600000-0000-0000-0000-000000000012';
 UPDATE invitation_mail_intents SET target_board_id=NULL,target_board_role=NULL WHERE job_id='$job';" >/dev/null
psql -X -v ON_ERROR_STOP=1 -c "UPDATE invitations SET target_board_id=NULL,target_board_role=NULL WHERE id='$invitation';" >/dev/null
test "$(scoped "SELECT is_usable FROM $load")" = t
test "$(worker "SELECT count(*) FROM $load")" = 0
test "$(worker "BEGIN; SET LOCAL app.tenant_id='$other'; SELECT count(*) FROM $load; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
for wrong in job tenant actor worker_id lease; do
  bad_job=$job; bad_tenant=$tenant; bad_actor=$actor; bad_worker=$worker_id; bad_lease=$lease
  case "$wrong" in job) bad_job=$other;; tenant) bad_tenant=$other;; actor) bad_actor=$other;; worker_id) bad_worker=$other;; lease) bad_lease=$other;; esac
  test "$(scoped "SELECT count(*) FROM public.load_invitation_mail('$bad_job','$bad_tenant','$bad_actor','$bad_worker','$bad_lease',true)")" = 0
done
if api "SELECT count(*) FROM $load"; then echo 'API executed Worker mail capability'; exit 1; fi
if worker 'SELECT email FROM users'; then echo 'Tenant Worker enumerated global accounts'; exit 1; fi
if worker "UPDATE invitation_mail_intents SET recipient_email='attacker@example.test'"; then echo 'Worker changed protected recipient'; exit 1; fi
if api "UPDATE invitation_mail_intents SET state='FAILED'"; then echo 'API changed mail delivery state'; exit 1; fi
test "$(api 'SELECT count(*) FROM invitation_mail_intents')" = 0
test "$(api "BEGIN; SET LOCAL app.tenant_id='$tenant'; SELECT count(*) FROM invitation_mail_intents; ROLLBACK;" | grep -E '^[0-9]+$')" = 1
if api "BEGIN; SET LOCAL app.tenant_id='$tenant'; INSERT INTO invitation_mail_intents SELECT '$other',tenant_id,invitation_id,issuer_id,recipient_email,target_surface,target_role,expires_at,key_id,sender_address,public_origin,provider_account,template_version,state,provider_receipt_id,finished_at,safe_error_code,created_at,version FROM invitation_mail_intents; COMMIT;"; then echo 'Unbound mail job accepted'; exit 1; fi
for change in "service_identity='other-service'" "safe_metadata='{}'::jsonb" "state='FAILED',worker_id=NULL,lease_id=NULL,lease_expires_at=NULL" "lease_expires_at=now()-interval '1 second'"; do
  psql -X -v ON_ERROR_STOP=1 -c "UPDATE background_jobs SET $change WHERE id='$job';" >/dev/null
  test "$(scoped "SELECT count(*) FROM $load")" = 0
  test "$(scoped "SELECT public.finish_invitation_mail('$job','$tenant','$actor','$worker_id','$lease','SENT',NULL,'$other')")" = f
  psql -X -v ON_ERROR_STOP=1 -c "UPDATE background_jobs SET service_identity='invitation-email-delivery',safe_metadata=jsonb_build_object('invitationId','$invitation'::uuid),state='RUNNING',worker_id='$worker_id',lease_id='$lease',lease_expires_at=now()+interval '5 minutes' WHERE id='$job';" >/dev/null
done
# Expire the lease while the acknowledgment waits on the actual job row.
# The function must check the database clock after obtaining both row locks.
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT
PGAPPNAME=invitation-mail-lock psql -X -v ON_ERROR_STOP=1 -c "BEGIN; SELECT id FROM background_jobs WHERE id='$job' FOR UPDATE; UPDATE background_jobs SET lease_expires_at=clock_timestamp()+interval '1 second' WHERE id='$job'; SELECT pg_sleep(3); COMMIT;" >"$scratch/lock" & lock_pid=$!
ready=false
for _ in $(seq 1 100); do
  if test "$(psql -X -At -c "SELECT count(*) FROM pg_stat_activity WHERE application_name='invitation-mail-lock' AND wait_event='PgSleep'")" = 1; then ready=true; break; fi
  sleep .05
done
test "$ready" = true
# Acknowledgment begins while the lease change remains uncommitted.
PGAPPNAME=invitation-mail-finish worker "BEGIN; SET LOCAL app.tenant_id='$tenant'; SELECT public.finish_invitation_mail('$job','$tenant','$actor','$worker_id','$lease','SENT',NULL,'$other'); COMMIT;" >"$scratch/finish" & finish_pid=$!
waiting=false
for _ in $(seq 1 100); do
  if test "$(psql -X -At -c "SELECT count(*) FROM pg_stat_activity WHERE application_name='invitation-mail-finish' AND wait_event_type='Lock'")" = 1; then waiting=true; break; fi
  sleep .02
done
test "$waiting" = true
# The lock holder changes its own row before releasing it; acknowledgment must
# re-read that new expired lease rather than retain admission from before wait.
wait "$lock_pid"
wait "$finish_pid"
test "$(grep -E '^(t|f)$' "$scratch/finish")" = f
psql -X -v ON_ERROR_STOP=1 -c "UPDATE background_jobs SET lease_expires_at=now()+interval '5 minutes' WHERE id='$job';" >/dev/null
# PRD-60-TC-04/06/10, ONBOARD-FR-002/003/004: fresh canonical
# admission, including privileged changes after the immutable snapshot.
for mutation in \
  "UPDATE users SET status='SUSPENDED' WHERE id='$actor'" \
  "UPDATE users SET email_verified=false WHERE id='$actor'" \
  "UPDATE organizations SET status='ARCHIVED' WHERE id='$tenant'" \
  "UPDATE organization_members SET status='REMOVED' WHERE user_id='$actor'" \
  "UPDATE organization_members SET role='MEMBER' WHERE user_id='$actor'" \
  "UPDATE invitations SET revoked_at=now() WHERE id='$invitation'" \
  "UPDATE invitations SET accepted_at=now() WHERE id='$invitation'" \
  "UPDATE invitations SET expires_at=now()-interval '1 second' WHERE id='$invitation'" \
  "UPDATE invitations SET invited_email='changed@example.test' WHERE id='$invitation'" \
  "UPDATE invitations SET email_normalized='CHANGED@EXAMPLE.TEST' WHERE id='$invitation'" \
  "UPDATE invitations SET target_role='ADMIN' WHERE id='$invitation'" \
  "UPDATE invitations SET target_surface='PORTAL' WHERE id='$invitation'"; do
  psql -X -v ON_ERROR_STOP=1 -c "$mutation" >/dev/null
  test "$(scoped "SELECT is_usable FROM $load")" = f
  psql -X -v ON_ERROR_STOP=1 -c "UPDATE users SET status='ACTIVE',email_verified=true WHERE id='$actor';
    UPDATE organizations SET status='ACTIVE' WHERE id='$tenant';
    UPDATE organization_members SET status='ACTIVE',role='OWNER',version=version+1,updated_at=clock_timestamp() WHERE user_id='$actor';
    UPDATE invitations i SET revoked_at=NULL,accepted_at=NULL,invited_email=m.recipient_email,
      email_normalized=upper(m.recipient_email),expires_at=m.expires_at,target_surface=m.target_surface,target_role=m.target_role
      FROM invitation_mail_intents m WHERE i.id=m.invitation_id AND m.job_id='$job';" >/dev/null
done
# The Worker follows the explicitly configured verification policy. Administrative
# fixture changes are needed here; neither runtime role can rewrite snapshots.
psql -X -v ON_ERROR_STOP=1 -c "UPDATE users SET email_verified=false WHERE id='$actor';" >/dev/null
test "$(scoped "SELECT is_usable FROM public.load_invitation_mail('$job','$tenant','$actor','$worker_id','$lease',false)")" = t
psql -X -v ON_ERROR_STOP=1 -c "UPDATE users SET email_verified=true WHERE id='$actor';
  UPDATE invitations SET target_role='OWNER' WHERE id='$invitation';
  UPDATE invitation_mail_intents SET target_role='OWNER' WHERE job_id='$job';
  UPDATE organization_members SET role='ADMIN' WHERE user_id='$actor';" >/dev/null
test "$(scoped "SELECT is_usable FROM $load")" = f
# Portal OWNER is a portal role, so it must not inherit the internal OWNER grant
# restriction or cause an internal membership grant during mail delivery.
psql -X -v ON_ERROR_STOP=1 -c "UPDATE invitations SET target_surface='PORTAL' WHERE id='$invitation';
  UPDATE invitation_mail_intents SET target_surface='PORTAL' WHERE job_id='$job';" >/dev/null
test "$(scoped "SELECT is_usable FROM $load")" = t
test "$(psql -X -At -c "SELECT count(*) FROM organization_members WHERE tenant_id='$tenant'")" = 1
test "$(psql -X -At -c "SELECT count(*) FROM portal_access WHERE tenant_id='$tenant'")" = 0
# A wrong lease cannot change the ledger. A successful receipt is durable and
# immutable even while the same generic job still has its live lease.
test "$(scoped "SELECT public.finish_invitation_mail('$job','$tenant','$actor','$worker_id','$other','SENT',NULL,'$other')")" = f
test "$(worker "BEGIN; SET LOCAL app.tenant_id='$tenant'; SELECT public.finish_invitation_mail('$job','$tenant','$actor','$worker_id','$lease','SENT',NULL,'$other'); COMMIT;" | grep -E '^(t|f)$')" = t
test "$(scoped "SELECT state FROM $load")" = SENT
test "$(scoped "SELECT public.finish_invitation_mail('$job','$tenant','$actor','$worker_id','$lease','FAILED','job_handler_failed',NULL)")" = f
test "$(psql -X -At -c "SELECT count(*) FROM invitation_mail_intents WHERE job_id='$job' AND state='SENT' AND provider_receipt_id='$other' AND version=2")" = 1
echo 'Invitation mail capability: tenant/actor/worker/lease binding, canonical eligibility, protected snapshots, global identity isolation and durable terminal receipts passed.'
