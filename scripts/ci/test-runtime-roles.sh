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
