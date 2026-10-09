#!/usr/bin/env bash
set -euo pipefail
# ARCH-04-AC-001: route discovery remains bounded even without WHERE predicates.
scratch=$(mktemp -d)
org_a=02700000-0000-0000-0000-000000000001
org_b=02700000-0000-0000-0000-000000000002
user_a=02700000-0000-0000-0000-000000000003
user_b=02700000-0000-0000-0000-000000000004
board_a=02700000-0000-0000-0000-000000000005
board_b=02700000-0000-0000-0000-000000000006
list_a=02700000-0000-0000-0000-000000000007
list_b=02700000-0000-0000-0000-000000000008
card_a=02700000-0000-0000-0000-000000000009
card_b=02700000-0000-0000-0000-000000000010
inv_a=02700000-0000-0000-0000-000000000011
inv_b=02700000-0000-0000-0000-000000000012
admin() { psql -X -qAt -v ON_ERROR_STOP=1 -c "$1"; }
api() { PGUSER=strataai_api_runtime PGPASSWORD='ci-api-runtime-password' psql -X -qAt -v ON_ERROR_STOP=1 -v VERBOSITY=verbose -c "$1"; }
lookup() { api "BEGIN; SET LOCAL app.route_kind='$1'; SET LOCAL app.route_key='$2'; $3; ROLLBACK;"; }
cleanup() {
  admin "DELETE FROM cards WHERE tenant_id IN ('$org_a','$org_b');
    DELETE FROM board_lists WHERE tenant_id IN ('$org_a','$org_b');
    DELETE FROM boards WHERE tenant_id IN ('$org_a','$org_b');
    DELETE FROM invitations WHERE tenant_id IN ('$org_a','$org_b');
    DELETE FROM organization_members WHERE tenant_id IN ('$org_a','$org_b');
    DELETE FROM organizations WHERE id IN ('$org_a','$org_b');
    DELETE FROM users WHERE id IN ('$user_a','$user_b');" >/dev/null
  rm -rf "$scratch"
}
trap cleanup EXIT
admin "INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at) VALUES
  ('$user_a','route-a@example.test','ROUTE-A@EXAMPLE.TEST','Route A','ACTIVE','unusable-ci-fixture',now(),now()),
  ('$user_b','route-b@example.test','ROUTE-B@EXAMPLE.TEST','Route B','ACTIVE','unusable-ci-fixture',now(),now());
  INSERT INTO organizations(id,name,owner_user_id,created_at,updated_at) VALUES
  ('$org_a','Route A','$user_a',now(),now()),('$org_b','Route B','$user_b',now(),now());
  INSERT INTO organization_members(id,tenant_id,user_id,role,status) VALUES
  (gen_random_uuid(),'$org_a','$user_a','OWNER','ACTIVE'),(gen_random_uuid(),'$org_b','$user_b','OWNER','ACTIVE');
  INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
  ('$board_a','$org_a','Route A',now(),now()),('$board_b','$org_b','Route B',now(),now());
  INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
  ('$list_a','$org_a','$board_a','Route A',lpad('1',30,'0'),now(),now()),
  ('$list_b','$org_b','$board_b','Route B',lpad('1',30,'0'),now(),now());
  INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
  ('$card_a','$org_a','$board_a','$list_a','Route A',lpad('1',30,'0'),now(),now()),
  ('$card_b','$org_b','$board_b','$list_b','Route B',lpad('1',30,'0'),now(),now());
  INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,created_by_user_id,created_at,expires_at) VALUES
  ('$inv_a','$org_a','route-a@example.test','ROUTE-A@EXAMPLE.TEST',encode(sha256('route-a-fixture'::bytea),'hex'),'INTERNAL','MEMBER','$user_a',now(),now()+interval '1 day'),
  ('$inv_b','$org_b','route-b@example.test','ROUTE-B@EXAMPLE.TEST',encode(sha256('route-b-fixture'::bytea),'hex'),'PORTAL','OWNER','$user_b',now(),now()+interval '1 day');" >/dev/null

for table in board_routes list_routes card_routes user_organization_access invitation_routes; do
  test "$(api "SELECT count(*) FROM $table")" = 0
  test "$(api "BEGIN; SET LOCAL app.tenant_id='$org_a'; SELECT count(*) FROM $table WHERE tenant_id='$org_b'; ROLLBACK;")" = 0
done
for entry in "board_routes:BOARD:$board_a" "list_routes:LIST:$list_a" "card_routes:CARD:$card_a" "user_organization_access:ORGANIZATION_USER:$user_a" "invitation_routes:INVITATION_ID:$inv_a"; do
  IFS=: read -r table kind key <<< "$entry"
  test "$(lookup "$kind" "$key" "SELECT count(*) FROM $table")" = 1
  test "$(lookup "$kind" "$key" "SELECT count(*) FROM $table WHERE tenant_id='$org_b'")" = 0
  test "$(lookup "$kind" 'malformed-fixture' "SELECT count(*) FROM $table")" = 0
done
token=$(admin "SELECT encode(sha256('route-a-fixture'::bytea),'hex')")
for entry in "INVITATION_TOKEN:$token" 'INVITATION_RECIPIENT:ROUTE-A@EXAMPLE.TEST'; do
  IFS=: read -r kind key <<< "$entry"
  test "$(lookup "$kind" "$key" 'SELECT count(*) FROM invitation_routes')" = 1
  test "$(lookup "$kind" "$key" "SELECT count(*) FROM invitation_routes WHERE invitation_id='$inv_b'")" = 0
done
test "$(lookup LIST "$board_a" 'SELECT count(*) FROM board_routes')" = 0
test "$(lookup BOARD "$board_a" "WITH changed AS (UPDATE board_routes SET visibility='PUBLIC' WHERE board_id='$board_a' RETURNING board_id) SELECT count(*) FROM changed")" = 0
test "$(admin "SELECT visibility FROM board_routes WHERE board_id='$board_a'")" = PRIVATE
if lookup BOARD "$board_a" "INSERT INTO board_routes(board_id,tenant_id,visibility,lifecycle_state,updated_at) VALUES (gen_random_uuid(),'$org_b','PUBLIC','ACTIVE',now())" >"$scratch/denied" 2>&1; then
  echo 'Read-only discovery context admitted a route write' >&2; exit 1
fi
grep -q 'ERROR:  42501:' "$scratch/denied"
# Clock synchronization must not replace the authorization error for any
# projection. These fresh IDs have no canonical source and no owning tenant
# write context; a discovery capability still cannot create routing metadata.
for entry in \
  "LIST|INSERT INTO list_routes(list_id,tenant_id,board_id,lifecycle_state,updated_at) VALUES(gen_random_uuid(),'$org_b','$board_b','ACTIVE',now())" \
  "CARD|INSERT INTO card_routes(card_id,tenant_id,board_id,list_id,lifecycle_state,updated_at) VALUES(gen_random_uuid(),'$org_b','$board_b','$list_b','ACTIVE',now())" \
  "LABEL|INSERT INTO label_routes(label_id,tenant_id,board_id,status) VALUES(gen_random_uuid(),'$org_b','$board_b','ACTIVE')"; do
  IFS='|' read -r kind statement <<< "$entry"
  if lookup "$kind" "$board_a" "$statement" >"$scratch/denied" 2>&1; then
    echo 'Read-only discovery context admitted a clocked route write' >&2; exit 1
  fi
  grep -q 'ERROR:  42501:' "$scratch/denied"
done
# Transaction-local lookup state must disappear even after a committed read.
test "$(api "BEGIN; SET LOCAL app.route_kind='BOARD'; SET LOCAL app.route_key='$board_a'; SELECT count(*) FROM board_routes; COMMIT; SELECT count(*) FROM board_routes;")" = $'1\n0'
echo 'Forced route RLS, bounded widened queries, wrong/missing/malformed context, tenant isolation, read-only discovery and context reset passed.'
