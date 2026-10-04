#!/usr/bin/env bash
set -euo pipefail
export STRATAAI_API_DB_PASSWORD='ci-api-runtime-password'
export STRATAAI_WORKER_DB_PASSWORD='ci-worker-runtime-password'
# Provisioning must configure existing roles, including roles created without
# LOGIN by a migration/upgrade fixture. Do not depend on CREATE ROLE defaults.
psql -X -v ON_ERROR_STOP=1 -c "DO \$\$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN CREATE ROLE strataai_api_runtime; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN CREATE ROLE strataai_worker_runtime; END IF;
 END \$\$; ALTER ROLE strataai_api_runtime NOLOGIN; ALTER ROLE strataai_worker_runtime NOLOGIN;" >/dev/null
psql -X -v ON_ERROR_STOP=1 -f db/provision-runtime-roles.sql
api() { PGUSER=strataai_api_runtime PGPASSWORD="$STRATAAI_API_DB_PASSWORD" psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
worker() { PGUSER=strataai_worker_runtime PGPASSWORD="$STRATAAI_WORKER_DB_PASSWORD" psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
for mention_table in comment_mention_snapshots comment_mention_recipients; do
 test "$(api "SELECT has_table_privilege(current_user,'$mention_table','SELECT') AND has_table_privilege(current_user,'$mention_table','INSERT') AND NOT has_table_privilege(current_user,'$mention_table','UPDATE') AND NOT has_table_privilege(current_user,'$mention_table','DELETE')")" = t
 test "$(worker "SELECT has_table_privilege(current_user,'$mention_table','SELECT') OR has_table_privilege(current_user,'$mention_table','INSERT') OR has_table_privilege(current_user,'$mention_table','UPDATE') OR has_table_privilege(current_user,'$mention_table','DELETE')")" = f
done
test "$(api 'SELECT public.runtime_database_role_is_safe()')" = t
test "$(worker 'SELECT public.runtime_database_role_is_safe()')" = t
test "$(api "SELECT has_table_privilege(current_user,'card_comments','SELECT') AND has_table_privilege(current_user,'card_comments','INSERT') AND has_table_privilege(current_user,'card_comments','UPDATE') AND NOT has_table_privilege(current_user,'card_comments','DELETE')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'card_comments','SELECT') OR has_table_privilege(current_user,'card_comments','INSERT') OR has_table_privilege(current_user,'card_comments','UPDATE') OR has_table_privilege(current_user,'card_comments','DELETE')")" = f
test "$(api "SELECT has_table_privilege(current_user,'user_mention_handles','SELECT') AND has_column_privilege(current_user,'user_mention_handles','handle','UPDATE') AND has_column_privilege(current_user,'user_mention_handles','version','UPDATE') AND has_column_privilege(current_user,'user_mention_handles','updated_at','UPDATE') AND NOT has_column_privilege(current_user,'user_mention_handles','user_id','UPDATE') AND NOT has_column_privilege(current_user,'user_mention_handles','created_at','UPDATE') AND NOT has_table_privilege(current_user,'user_mention_handles','INSERT') AND NOT has_table_privilege(current_user,'user_mention_handles','DELETE')")" = t

test "$(api "SELECT has_table_privilege(current_user,'mention_handle_reservations','SELECT') OR has_table_privilege(current_user,'mention_handle_reservations','INSERT') OR has_table_privilege(current_user,'mention_handle_reservations','UPDATE') OR has_table_privilege(current_user,'mention_handle_reservations','DELETE') OR has_function_privilege(current_user,'public.seed_user_mention_handle()','EXECUTE') OR has_function_privilege(current_user,'public.enforce_user_mention_handle_revision()','EXECUTE')")" = f

test "$(worker "SELECT has_table_privilege(current_user,'user_mention_handles','SELECT') OR has_any_column_privilege(current_user,'user_mention_handles','UPDATE') OR has_table_privilege(current_user,'mention_handle_reservations','SELECT') OR has_table_privilege(current_user,'mention_handle_reservations','INSERT') OR has_function_privilege(current_user,'public.seed_user_mention_handle()','EXECUTE') OR has_function_privilege(current_user,'public.enforce_user_mention_handle_revision()','EXECUTE')")" = f
test "$(api "SELECT has_table_privilege(current_user,'identity_handle_claim_replays','SELECT') AND has_table_privilege(current_user,'identity_handle_claim_replays','INSERT') AND NOT has_any_column_privilege(current_user,'identity_handle_claim_replays','UPDATE') AND NOT has_table_privilege(current_user,'identity_handle_claim_replays','DELETE') AND NOT has_function_privilege(current_user,'purge_expired_identity_handle_claim_replays()','EXECUTE')")" = t

test "$(worker "SELECT has_column_privilege(current_user,'identity_handle_claim_replays','user_id','SELECT') AND has_column_privilege(current_user,'identity_handle_claim_replays','key_id','SELECT') AND has_column_privilege(current_user,'identity_handle_claim_replays','expires_at','SELECT') AND has_table_privilege(current_user,'identity_handle_claim_replays','DELETE') AND has_function_privilege(current_user,'purge_expired_identity_handle_claim_replays()','EXECUTE') AND NOT has_column_privilege(current_user,'identity_handle_claim_replays','fingerprint','SELECT') AND NOT has_column_privilege(current_user,'identity_handle_claim_replays','user_version','SELECT') AND NOT has_column_privilege(current_user,'identity_handle_claim_replays','handle_version','SELECT') AND NOT has_column_privilege(current_user,'identity_handle_claim_replays','changed','SELECT') AND NOT has_any_column_privilege(current_user,'identity_handle_claim_replays','UPDATE') AND NOT has_table_privilege(current_user,'identity_handle_claim_replays','INSERT')")" = t
test "$(api 'SELECT count(*) FROM boards')" = 0
test "$(api "BEGIN; SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SELECT count(*) FROM boards WHERE tenant_id='22222222-2222-2222-2222-222222222222'; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(api "BEGIN; SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SELECT count(*) FROM boards WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'; ROLLBACK;" | grep -E '^[0-9]+$')" = 1
if api "BEGIN; SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES ('cccccccc-1234-1234-1234-cccccccccccc','22222222-2222-2222-2222-222222222222','Denied',now(),now()); COMMIT;"; then echo 'Cross-tenant write escaped RLS'; exit 1; fi
test "$(psql -X -At -c 'SELECT public.runtime_database_role_is_safe()')" = f
test "$(api "SELECT has_table_privilege(current_user,'audit_events','UPDATE') OR has_table_privilege(current_user,'identity_delivery_jobs','SELECT')")" = f
test "$(worker "SELECT has_table_privilege(current_user,'users','SELECT') OR has_table_privilege(current_user,'organizations','SELECT') OR has_table_privilege(current_user,'identity_delivery_jobs','INSERT')")" = f
test "$(api "SELECT has_table_privilege(current_user,'board_labels','SELECT') AND has_table_privilege(current_user,'card_labels','INSERT') AND NOT has_table_privilege(current_user,'board_labels','DELETE')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'board_labels','SELECT') OR has_table_privilege(current_user,'card_labels','SELECT') OR has_table_privilege(current_user,'label_routes','SELECT')")" = f
test "$(api "SELECT has_table_privilege(current_user,'checklists','SELECT') AND has_table_privilege(current_user,'checklist_items','INSERT') AND has_table_privilege(current_user,'checklist_items','UPDATE') AND NOT has_table_privilege(current_user,'checklists','DELETE') AND NOT has_table_privilege(current_user,'checklist_items','DELETE')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'checklists','SELECT') OR has_table_privilege(current_user,'checklist_items','SELECT') OR has_table_privilege(current_user,'checklist_items','UPDATE')")" = f
test "$(api "SELECT has_table_privilege(current_user,'card_members','SELECT') AND has_table_privilege(current_user,'card_members','INSERT') AND has_table_privilege(current_user,'card_members','UPDATE') AND has_table_privilege(current_user,'card_members','DELETE')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'card_members','SELECT') OR has_table_privilege(current_user,'card_members','INSERT') OR has_table_privilege(current_user,'card_members','UPDATE') OR has_table_privilege(current_user,'card_members','DELETE')")" = f
test "$(api "SELECT has_table_privilege(current_user,'card_assignment_notifications','SELECT') AND has_table_privilege(current_user,'card_assignment_notifications','INSERT') AND NOT has_table_privilege(current_user,'card_assignment_notifications','UPDATE') AND NOT has_table_privilege(current_user,'card_assignment_notifications','DELETE')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'card_assignment_notifications','SELECT') OR has_table_privilege(current_user,'card_assignment_notifications','INSERT') OR has_table_privilege(current_user,'card_assignment_notifications','UPDATE') OR has_table_privilege(current_user,'card_assignment_notifications','DELETE')")" = f
test "$(api "SELECT has_column_privilege(current_user,'card_assignment_notifications','read_at','UPDATE') AND NOT has_column_privilege(current_user,'card_assignment_notifications','recipient_id','UPDATE') AND NOT has_column_privilege(current_user,'card_assignment_notifications','actor_id','UPDATE') AND NOT has_column_privilege(current_user,'card_assignment_notifications','id','UPDATE')")" = t
test "$(api "SELECT has_table_privilege(current_user,'watch_subscriptions','SELECT') AND has_table_privilege(current_user,'watch_subscriptions','INSERT') AND NOT has_table_privilege(current_user,'watch_subscriptions','UPDATE') AND NOT has_table_privilege(current_user,'watch_subscriptions','DELETE') AND has_column_privilege(current_user,'watch_subscriptions','watching','UPDATE') AND has_column_privilege(current_user,'watch_subscriptions','updated_at','UPDATE') AND has_column_privilege(current_user,'watch_subscriptions','version','UPDATE') AND NOT has_column_privilege(current_user,'watch_subscriptions','user_id','UPDATE') AND NOT has_column_privilege(current_user,'watch_subscriptions','entity_id','UPDATE')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'watch_subscriptions','SELECT') OR has_table_privilege(current_user,'watch_subscriptions','INSERT') OR has_table_privilege(current_user,'watch_subscriptions','UPDATE') OR has_table_privilege(current_user,'watch_subscriptions','DELETE')")" = f
test "$(api "SELECT has_table_privilege(current_user,'attachment_upload_intents','SELECT') AND has_table_privilege(current_user,'attachment_upload_intents','INSERT') AND has_table_privilege(current_user,'attachment_upload_intents','UPDATE') AND NOT has_table_privilege(current_user,'attachment_upload_intents','DELETE')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'attachment_upload_intents','SELECT') OR has_table_privilege(current_user,'attachment_upload_intents','INSERT') OR has_table_privilege(current_user,'attachment_upload_intents','UPDATE') OR has_table_privilege(current_user,'attachment_upload_intents','DELETE')")" = f
test "$(worker "SELECT has_function_privilege(current_user,'public.load_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE') AND has_function_privilege(current_user,'public.finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text)','EXECUTE') AND NOT has_function_privilege(current_user,'public.attachment_scan_claim_is_live(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE') AND NOT has_table_privilege(current_user,'attachments','SELECT') AND NOT has_table_privilege(current_user,'attachments','UPDATE') AND NOT has_table_privilege(current_user,'cards','UPDATE')")" = t
test "$(api "SELECT has_function_privilege(current_user,'public.load_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE') OR has_function_privilege(current_user,'public.finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text)','EXECUTE')")" = f
if worker 'SELECT password_hash FROM users'; then echo 'Worker read identity secrets'; exit 1; fi
test "$(api 'SELECT count(*) FROM identity_events')" = 0
test "$(api "SELECT has_table_privilege(current_user,'identity_events','UPDATE') OR has_table_privilege(current_user,'identity_events','DELETE')")" = f
if worker 'SELECT event_id FROM identity_events'; then echo 'Tenant Worker read global identity events'; exit 1; fi
test "$(api 'SELECT count(*) FROM identity_profile_replays')" = 0
test "$(api "SELECT has_table_privilege(current_user,'identity_profile_replays','DELETE')")" = f
if worker 'SELECT result_json FROM identity_profile_replays'; then echo 'Tenant Worker read global profile retries'; exit 1; fi
# Disposable global subjects test real-login RLS, independently of tenant/mail GUCs.
psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
VALUES ('01200000-0000-0000-0000-000000000001','event-role-1@example.test','EVENT-ROLE-1@EXAMPLE.TEST','Role fixture','ACTIVE','unusable-ci-fixture',now(),now()),
       ('01200000-0000-0000-0000-000000000002','event-role-2@example.test','EVENT-ROLE-2@EXAMPLE.TEST','Role fixture','ACTIVE','unusable-ci-fixture',now(),now());
INSERT INTO identity_event_streams(user_id,last_sequence) SELECT id,1 FROM users WHERE id IN
('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
INSERT INTO identity_events(event_id,user_id,sequence,actor_id,event_type,entity_id,entity_version,correlation_id)
SELECT id,id,1,id,'USER_REGISTERED',id,1,'role-fixture' FROM users WHERE id IN
('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
INSERT INTO identity_profile_replays(user_id,key_id,fingerprint,result_json)
SELECT id,id,repeat('0',64),jsonb_build_object('Id',id,'Email',email,'DisplayName',display_name,
    'AvatarUrl',avatar_url,'Locale',locale,'Timezone',timezone,'Status',1,'EmailVerified',email_verified,
    'CreatedAt',created_at,'UpdatedAt',updated_at,'Version',version)
FROM users WHERE id IN ('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
SQL
test "$(api 'SELECT count(*) FROM identity_profile_replays')" = 0
test "$(api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(*) FROM identity_profile_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 1
if api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; INSERT INTO identity_profile_replays(user_id,key_id,fingerprint,result_json) SELECT user_id,'01300000-0000-0000-0000-000000000004',fingerprint,result_json||jsonb_build_object('SessionToken','forbidden-fixture') FROM identity_profile_replays WHERE user_id='01200000-0000-0000-0000-000000000001'; ROLLBACK;"; then echo 'Credential-bearing result escaped profile schema'; exit 1; fi
test "$(api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(*) FROM identity_profile_replays WHERE user_id='01200000-0000-0000-0000-000000000002'; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
if api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; INSERT INTO identity_profile_replays(user_id,key_id,fingerprint,result_json) SELECT '01200000-0000-0000-0000-000000000002','01300000-0000-0000-0000-000000000003',fingerprint,jsonb_set(result_json,'{Id}',to_jsonb('01200000-0000-0000-0000-000000000002'::text)) FROM identity_profile_replays WHERE user_id='01200000-0000-0000-0000-000000000001'; ROLLBACK;"; then echo 'Cross-subject profile retry write escaped RLS'; exit 1; fi
# Maintenance gets only expired key metadata and deletion, with no cached profile/fingerprint reads.
if worker 'SELECT fingerprint FROM identity_profile_replays'; then echo 'Maintenance read retry input fingerprints'; exit 1; fi
if api 'SELECT public.purge_expired_identity_profile_replays()'; then echo 'API obtained global maintenance execution'; exit 1; fi
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(user_id) FROM identity_profile_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; WITH d AS (DELETE FROM identity_profile_replays RETURNING user_id) SELECT count(*) FROM d; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
INSERT INTO identity_profile_replays(user_id,key_id,fingerprint,result_json,created_at,expires_at)
SELECT r.user_id,gen_random_uuid(),r.fingerprint,r.result_json,clock_timestamp()-interval '2 days',clock_timestamp()-interval '1 day'
FROM identity_profile_replays r CROSS JOIN generate_series(1,150)
WHERE r.user_id='01200000-0000-0000-0000-000000000001';
SQL
test "$(api "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT count(*) FROM identity_profile_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(worker 'SELECT public.purge_expired_identity_profile_replays()')" = 0
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_profile_replays(); ROLLBACK;" | grep -E '^[0-9]+$')" = 100
test "$(psql -X -At -c 'SELECT count(*) FROM identity_profile_replays WHERE expires_at<=clock_timestamp()')" = 150
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_profile_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 100
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_profile_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 50
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_profile_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 0
test "$(psql -X -At -c 'SELECT count(*) FROM identity_profile_replays')" = 2
# Revocation receipts have immutable session binding and an expired-only maintenance capability.
psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
INSERT INTO sessions(id,user_id,token_hash,created_at,expires_at,revoked_at)
SELECT id,id,encode(sha256(id::text::bytea),'hex'),now(),now()+interval '1 day',now()
FROM users WHERE id IN ('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
INSERT INTO identity_revocation_replays(user_id,key_id,session_id,operation,expires_at)
SELECT id,id,id,'LOGOUT',now()+interval '1 hour' FROM sessions
WHERE id IN ('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
INSERT INTO identity_revocation_replays(user_id,key_id,session_id,operation,created_at,expires_at)
SELECT '01200000-0000-0000-0000-000000000001',gen_random_uuid(),'01200000-0000-0000-0000-000000000001',
 'LOGOUT',now()-interval '2 days',now()-interval '1 day' FROM generate_series(1,150);
SQL
test "$(api 'SELECT count(*) FROM identity_revocation_replays')" = 0
test "$(api "SELECT has_table_privilege(current_user,'identity_revocation_replays','UPDATE')")" = f
if worker 'SELECT session_id FROM identity_revocation_replays'; then echo 'Maintenance read original session binding'; exit 1; fi
if worker 'SELECT operation FROM identity_revocation_replays'; then echo 'Maintenance read receipt operation'; exit 1; fi
if api 'SELECT public.purge_expired_identity_revocation_replays()'; then echo 'API invoked global receipt purge'; exit 1; fi
test "$(api "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT count(*) FROM identity_revocation_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; WITH d AS (DELETE FROM identity_revocation_replays WHERE expires_at>clock_timestamp() RETURNING user_id) SELECT count(*) FROM d; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
if api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; INSERT INTO identity_revocation_replays(user_id,key_id,session_id,operation,expires_at) VALUES ('01200000-0000-0000-0000-000000000001',gen_random_uuid(),'01200000-0000-0000-0000-000000000002','LOGOUT',now()+interval '1 hour'); ROLLBACK;"; then echo 'Receipt bound a different subject session'; exit 1; fi
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(user_id) FROM identity_revocation_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(worker 'SELECT public.purge_expired_identity_revocation_replays()')" = 0
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_revocation_replays(); ROLLBACK;" | grep -E '^[0-9]+$')" = 100
test "$(psql -X -At -c 'SELECT count(*) FROM identity_revocation_replays WHERE expires_at<=clock_timestamp()')" = 150
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_revocation_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 100
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_revocation_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 50
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_revocation_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 0
test "$(psql -X -At -c 'SELECT count(*) FROM identity_revocation_replays')" = 2
test "$(psql -X -At -c 'SELECT count(*) FROM sessions WHERE id IN (SELECT session_id FROM identity_revocation_replays)')" = 2
# Sign-in receipts expose only expired key metadata to maintenance.
psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
INSERT INTO identity_login_replays(user_id,key_id,session_id,key_version,fingerprint,expires_at)
SELECT id,id,id,'role-v1',repeat('0',64),now()+interval '1 hour' FROM sessions
WHERE id IN ('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
INSERT INTO identity_login_replays(user_id,key_id,session_id,key_version,fingerprint,created_at,expires_at)
SELECT '01200000-0000-0000-0000-000000000001',gen_random_uuid(),'01200000-0000-0000-0000-000000000001',
 'role-v1',repeat('0',64),now()-interval '2 days',now()-interval '1 day' FROM generate_series(1,150);
SQL
test "$(api 'SELECT count(*) FROM identity_login_replays')" = 0
test "$(api "SELECT has_table_privilege(current_user,'identity_login_replays','UPDATE') OR has_table_privilege(current_user,'identity_login_replays','DELETE')")" = f
for column in session_id key_version fingerprint; do
  if worker "SELECT $column FROM identity_login_replays"; then echo 'Maintenance read sign-in proof'; exit 1; fi
done
if api 'SELECT public.purge_expired_identity_login_replays()'; then echo 'API invoked global sign-in purge'; exit 1; fi
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(user_id) FROM identity_login_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(worker 'SELECT public.purge_expired_identity_login_replays()')" = 0
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_login_replays(); ROLLBACK;" | grep -E '^[0-9]+$')" = 100
test "$(psql -X -At -c 'SELECT count(*) FROM identity_login_replays WHERE expires_at<=clock_timestamp()')" = 150
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_login_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 100
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_login_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 50
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_login_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 0
test "$(psql -X -At -c 'SELECT count(*) FROM identity_login_replays')" = 2
# Registration retry maintenance cannot read credential intent or verification coordinates.
psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
INSERT INTO identity_registration_replays(user_id,key_id,key_version,fingerprint,verification_source,expires_at)
SELECT id,id,'role-v1',repeat('0',64),'NONE',now()+interval '1 hour' FROM users
WHERE id IN ('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
INSERT INTO identity_registration_replays(user_id,key_id,key_version,fingerprint,verification_source,created_at,expires_at)
SELECT '01200000-0000-0000-0000-000000000001',gen_random_uuid(),'role-v1',repeat('0',64),'NONE',
 now()-interval '2 days',now()-interval '1 day' FROM generate_series(1,150);
INSERT INTO email_verification_tokens(id,user_id,token_hash,created_at,expires_at)
SELECT id,id,encode(sha256(('registration-role-'||id)::bytea),'hex'),now(),now()+interval '1 hour' FROM users
WHERE id IN ('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
SQL
test "$(api 'SELECT count(*) FROM identity_registration_replays')" = 0
test "$(api "SELECT has_table_privilege(current_user,'identity_registration_replays','UPDATE') OR has_table_privilege(current_user,'identity_registration_replays','DELETE')")" = f
for column in fingerprint key_version verification_token_id verification_source verification_key_version; do
  if worker "SELECT $column FROM identity_registration_replays"; then echo 'Maintenance read registration proof'; exit 1; fi
done
if api 'SELECT public.purge_expired_identity_registration_replays()'; then echo 'API invoked global registration purge'; exit 1; fi
test "$(api "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT count(*) FROM identity_registration_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(*) FROM identity_registration_replays WHERE user_id='01200000-0000-0000-0000-000000000002'; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
if api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; INSERT INTO identity_registration_replays(user_id,key_id,key_version,fingerprint,verification_token_id,verification_source,verification_key_version,expires_at) VALUES ('01200000-0000-0000-0000-000000000001',gen_random_uuid(),'role-v1',repeat('0',64),'01200000-0000-0000-0000-000000000002','EMAIL_DELIVERY','role-v1',now()+interval '1 hour'); ROLLBACK;"; then echo 'Registration bound another account verification token'; exit 1; fi
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(user_id) FROM identity_registration_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(worker 'SELECT public.purge_expired_identity_registration_replays()')" = 0
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_registration_replays(); ROLLBACK;" | grep -E '^[0-9]+$')" = 100
test "$(psql -X -At -c 'SELECT count(*) FROM identity_registration_replays WHERE expires_at<=clock_timestamp()')" = 150
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_registration_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 100
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_registration_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 50
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_registration_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 0
test "$(psql -X -At -c 'SELECT count(*) FROM identity_registration_replays')" = 2
psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
INSERT INTO identity_recovery_request_replays(user_id,key_id,operation,key_version,fingerprint,verification_token_id,token_source,token_key_version,expires_at)
SELECT user_id,id,'VERIFY_EMAIL','role-v1',repeat('0',64),id,'API_REQUEST','role-v1',now()+interval '1 hour'
FROM email_verification_tokens WHERE id IN ('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
INSERT INTO identity_recovery_request_replays(user_id,key_id,operation,key_version,fingerprint,verification_token_id,token_source,token_key_version,created_at,expires_at)
SELECT '01200000-0000-0000-0000-000000000001',gen_random_uuid(),'VERIFY_EMAIL','role-v1',repeat('0',64),
 '01200000-0000-0000-0000-000000000001','API_REQUEST','role-v1',now()-interval '2 days',now()-interval '1 day' FROM generate_series(1,150);
SQL
test "$(api 'SELECT count(*) FROM identity_recovery_request_replays')" = 0
test "$(api "SELECT has_table_privilege(current_user,'identity_recovery_request_replays','UPDATE') OR has_table_privilege(current_user,'identity_recovery_request_replays','DELETE')")" = f
for column in fingerprint key_version password_reset_token_id verification_token_id token_source token_key_version; do
  if worker "SELECT $column FROM identity_recovery_request_replays"; then echo 'Maintenance read recovery proof'; exit 1; fi
done
if api 'SELECT public.purge_expired_identity_recovery_request_replays()'; then echo 'API invoked global recovery purge'; exit 1; fi
test "$(api "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT count(*) FROM identity_recovery_request_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(*) FROM identity_recovery_request_replays WHERE user_id='01200000-0000-0000-0000-000000000002'; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
if api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; INSERT INTO identity_recovery_request_replays(user_id,key_id,operation,key_version,fingerprint,verification_token_id,token_source,token_key_version,expires_at) VALUES ('01200000-0000-0000-0000-000000000001',gen_random_uuid(),'VERIFY_EMAIL','role-v1',repeat('0',64),'01200000-0000-0000-0000-000000000002','API_REQUEST','role-v1',now()+interval '1 hour'); ROLLBACK;"; then echo 'Recovery bound another account token'; exit 1; fi
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(user_id) FROM identity_recovery_request_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(worker 'SELECT public.purge_expired_identity_recovery_request_replays()')" = 0
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_recovery_request_replays(); ROLLBACK;" | grep -E '^[0-9]+$')" = 100
test "$(psql -X -At -c 'SELECT count(*) FROM identity_recovery_request_replays WHERE expires_at<=clock_timestamp()')" = 150
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_recovery_request_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 100
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_recovery_request_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 50
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_recovery_request_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 0
test "$(psql -X -At -c 'SELECT count(*) FROM identity_recovery_request_replays')" = 2
psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
INSERT INTO identity_token_consumption_replays(user_id,key_id,operation,key_version,fingerprint,verification_token_id,consumed_at,expires_at)
SELECT user_id,id,'VERIFY_EMAIL','role-v1',repeat('0',64),id,now(),now()+interval '1 hour'
FROM email_verification_tokens WHERE id IN ('01200000-0000-0000-0000-000000000001','01200000-0000-0000-0000-000000000002');
INSERT INTO identity_token_consumption_replays(user_id,key_id,operation,key_version,fingerprint,verification_token_id,consumed_at,created_at,expires_at)
SELECT '01200000-0000-0000-0000-000000000001',gen_random_uuid(),'VERIFY_EMAIL','role-v1',repeat('0',64),
 '01200000-0000-0000-0000-000000000001',now()-interval '3 days',now()-interval '2 days',now()-interval '1 day' FROM generate_series(1,150);
SQL
test "$(api 'SELECT count(*) FROM identity_token_consumption_replays')" = 0
test "$(api "SELECT has_table_privilege(current_user,'identity_token_consumption_replays','UPDATE') OR has_table_privilege(current_user,'identity_token_consumption_replays','DELETE')")" = f
for column in fingerprint key_version password_reset_token_id verification_token_id consumed_at; do
  if worker "SELECT $column FROM identity_token_consumption_replays"; then echo 'Maintenance read token consumption proof'; exit 1; fi
done
if api 'SELECT public.purge_expired_identity_token_consumption_replays()'; then echo 'API invoked global token consumption purge'; exit 1; fi
test "$(api "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT count(*) FROM identity_token_consumption_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(*) FROM identity_token_consumption_replays WHERE user_id='01200000-0000-0000-0000-000000000002'; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
if api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; INSERT INTO identity_token_consumption_replays(user_id,key_id,operation,key_version,fingerprint,verification_token_id,consumed_at,expires_at) VALUES ('01200000-0000-0000-0000-000000000001',gen_random_uuid(),'VERIFY_EMAIL','role-v1',repeat('0',64),'01200000-0000-0000-0000-000000000002',now(),now()+interval '1 hour'); ROLLBACK;"; then echo 'Consumption bound another account token'; exit 1; fi
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(user_id) FROM identity_token_consumption_replays; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(worker 'SELECT public.purge_expired_identity_token_consumption_replays()')" = 0
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_token_consumption_replays(); ROLLBACK;" | grep -E '^[0-9]+$')" = 100
test "$(psql -X -At -c 'SELECT count(*) FROM identity_token_consumption_replays WHERE expires_at<=clock_timestamp()')" = 150
test "$(worker "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_token_consumption_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 100
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_token_consumption_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 50
test "$(worker "BEGIN; SET LOCAL app.service_scope='GLOBAL_IDENTITY_RETRY_CLEANUP'; SELECT public.purge_expired_identity_token_consumption_replays(); COMMIT;" | grep -E '^[0-9]+$')" = 0
test "$(psql -X -At -c 'SELECT count(*) FROM identity_token_consumption_replays')" = 2
test "$(psql -X -At -c "SELECT count(*) FROM identity_events WHERE correlation_id='role-fixture'")" = 2
test "$(api 'SELECT count(*) FROM identity_events')" = 0
test "$(api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(*) FROM identity_events; ROLLBACK;" | grep -E '^[0-9]+$')" = 1
test "$(api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; SELECT count(*) FROM identity_events WHERE user_id='01200000-0000-0000-0000-000000000002'; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
if api "BEGIN; SET LOCAL app.identity_subject='01200000-0000-0000-0000-000000000001'; INSERT INTO identity_events(event_id,user_id,sequence,actor_id,event_type,entity_id,entity_version,correlation_id) VALUES ('01200000-0000-0000-0000-000000000003','01200000-0000-0000-0000-000000000002',2,'01200000-0000-0000-0000-000000000002','USER_PROFILE_UPDATED','01200000-0000-0000-0000-000000000002',1,'cross-subject'); ROLLBACK;"; then echo 'Cross-subject event write escaped RLS'; exit 1; fi
if api 'CREATE TABLE public.forbidden_runtime_ddl(id integer)'; then echo 'Runtime created a table'; exit 1; fi
restore() { psql -X -v ON_ERROR_STOP=1 -c 'ALTER ROLE strataai_api_runtime NOBYPASSRLS; REVOKE pg_read_all_data FROM strataai_api_runtime; REVOKE CREATE ON SCHEMA public FROM strataai_api_runtime;' >/dev/null; }
trap restore EXIT
psql -X -v ON_ERROR_STOP=1 -c 'ALTER ROLE strataai_api_runtime BYPASSRLS' >/dev/null
test "$(api 'SELECT public.runtime_database_role_is_safe()')" = f
restore
psql -X -v ON_ERROR_STOP=1 -c 'GRANT pg_read_all_data TO strataai_api_runtime' >/dev/null
test "$(api 'SELECT public.runtime_database_role_is_safe()')" = f
restore
psql -X -v ON_ERROR_STOP=1 -c 'GRANT CREATE ON SCHEMA public TO strataai_api_runtime' >/dev/null
test "$(api 'SELECT public.runtime_database_role_is_safe()')" = f
restore
test "$(api 'SELECT public.runtime_database_role_is_safe()')" = t


test "$(api "SELECT has_table_privilege(current_user,'attachments','SELECT') AND has_table_privilege(current_user,'attachments','INSERT') AND has_table_privilege(current_user,'attachments','UPDATE') AND NOT has_table_privilege(current_user,'attachments','DELETE')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'attachments','SELECT') OR has_table_privilege(current_user,'attachments','UPDATE')")" = f

# Preview declarations are Worker functions. Neither runtime can rewrite the
# private ledger directly. Read-only API access enforces RLS; helpers stay private.
test "$(worker "SELECT has_function_privilege(current_user,'load_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE') AND has_function_privilege(current_user,'declare_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,bigint,text,integer,integer)','EXECUTE') AND NOT has_function_privilege(current_user,'attachment_preview_claim_is_live(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE')")" = t
test "$(api "SELECT has_function_privilege(current_user,'load_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE') OR has_function_privilege(current_user,'declare_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,bigint,text,integer,integer)','EXECUTE')")" = f
for role in api worker; do
 test "$("$role" "SELECT has_table_privilege(current_user,'attachment_previews','INSERT') OR has_table_privilege(current_user,'attachment_previews','UPDATE') OR has_table_privilege(current_user,'attachment_previews','DELETE')")" = f
done

# Publication has its own fenced Worker capability. The renamed source helper
# must not retain the old Worker grant across an upgrade.
test "$(worker "SELECT has_function_privilege(current_user,'finish_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,integer,integer)','EXECUTE') AND NOT has_function_privilege(current_user,'load_attachment_preview_source(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE')")" = t
test "$(api "SELECT has_function_privilege(current_user,'finish_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,integer,integer)','EXECUTE') OR has_function_privilege(current_user,'load_attachment_preview_source(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE')")" = f
for role in api worker; do
 test "$("$role" "SELECT has_table_privilege(current_user,'attachment_preview_publications','INSERT') OR has_table_privilege(current_user,'attachment_preview_publications','UPDATE') OR has_table_privilege(current_user,'attachment_preview_publications','DELETE')")" = f
done
test "$(worker "SELECT has_function_privilege(current_user,'finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,boolean)','EXECUTE')")" = t
test "$(api "SELECT has_function_privilege(current_user,'finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,boolean)','EXECUTE')")" = f
echo 'Real runtime logins reject elevation and enforce service-specific grants.'

test "$(api "SELECT has_table_privilege(current_user,'attachment_previews','SELECT') AND has_table_privilege(current_user,'attachment_preview_publications','SELECT')")" = t
test "$(worker "SELECT has_table_privilege(current_user,'attachment_previews','SELECT') OR has_table_privilege(current_user,'attachment_preview_publications','SELECT')")" = f
test "$(worker "SELECT has_function_privilege(current_user,'enqueue_attachment_preview_backfill(uuid,integer)','EXECUTE')")" = t
test "$(api "SELECT has_function_privilege(current_user,'enqueue_attachment_preview_backfill(uuid,integer)','EXECUTE')")" = f
test "$(worker "SELECT has_function_privilege(current_user,'recover_attachment_scan_page(uuid,integer)','EXECUTE')")" = t
test "$(api "SELECT has_function_privilege(current_user,'recover_attachment_scan_page(uuid,integer)','EXECUTE')")" = f
for role in api worker; do
 test "$("$role" "SELECT has_table_privilege(current_user,'attachment_scan_sweeps','SELECT') OR has_table_privilege(current_user,'attachment_scan_sweeps','INSERT') OR has_table_privilege(current_user,'attachment_scan_sweeps','UPDATE') OR has_table_privilege(current_user,'attachment_scan_sweeps','DELETE')")" = f
done
for role in api worker; do
 test "$("$role" "SELECT has_table_privilege(current_user,'attachment_preview_sweeps','SELECT') OR has_table_privilege(current_user,'attachment_preview_sweeps','INSERT') OR has_table_privilege(current_user,'attachment_preview_sweeps','UPDATE') OR has_table_privilege(current_user,'attachment_preview_sweeps','DELETE')")" = f
done
