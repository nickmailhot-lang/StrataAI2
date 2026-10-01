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
