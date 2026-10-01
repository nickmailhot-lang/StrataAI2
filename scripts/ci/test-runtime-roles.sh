#!/usr/bin/env bash
set -euo pipefail
export STRATAAI_API_DB_PASSWORD='ci-api-runtime-password'
export STRATAAI_WORKER_DB_PASSWORD='ci-worker-runtime-password'
psql -X -v ON_ERROR_STOP=1 -f db/provision-runtime-roles.sql
api() { PGUSER=strataai_api_runtime PGPASSWORD="$STRATAAI_API_DB_PASSWORD" psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
worker() { PGUSER=strataai_worker_runtime PGPASSWORD="$STRATAAI_WORKER_DB_PASSWORD" psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
test "$(api 'SELECT public.runtime_database_role_is_safe()')" = t
test "$(worker 'SELECT public.runtime_database_role_is_safe()')" = t
test "$(api 'SELECT count(*) FROM boards')" = 0
test "$(api "BEGIN; SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SELECT count(*) FROM boards WHERE tenant_id='22222222-2222-2222-2222-222222222222'; ROLLBACK;" | grep -E '^[0-9]+$')" = 0
test "$(api "BEGIN; SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; SELECT count(*) FROM boards WHERE id='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'; ROLLBACK;" | grep -E '^[0-9]+$')" = 1
if api "BEGIN; SET LOCAL app.tenant_id='11111111-1111-1111-1111-111111111111'; INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES ('cccccccc-1234-1234-1234-cccccccccccc','22222222-2222-2222-2222-222222222222','Denied',now(),now()); COMMIT;"; then echo 'Cross-tenant write escaped RLS'; exit 1; fi
test "$(psql -X -At -c 'SELECT public.runtime_database_role_is_safe()')" = f
test "$(api "SELECT has_table_privilege(current_user,'audit_events','UPDATE') OR has_table_privilege(current_user,'identity_delivery_jobs','SELECT')")" = f
test "$(worker "SELECT has_table_privilege(current_user,'users','SELECT') OR has_table_privilege(current_user,'organizations','SELECT') OR has_table_privilege(current_user,'identity_delivery_jobs','INSERT')")" = f
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
echo 'Real runtime logins reject elevation and enforce service-specific grants.'
