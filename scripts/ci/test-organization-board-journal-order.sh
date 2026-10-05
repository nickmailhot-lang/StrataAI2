#!/usr/bin/env bash
set -euo pipefail
scratch=$(mktemp -d)
gate_pid=''; first_pid=''; second_pid=''; gate_fd=''; seeded=false
cleanup() {
  for task_pid in "$first_pid" "$second_pid" "$gate_pid"; do
    if [[ -n "$task_pid" ]]; then kill "$task_pid" 2>/dev/null || true; wait "$task_pid" 2>/dev/null || true; fi
  done
  if $seeded; then
    psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
BEGIN;
ALTER TABLE organization_board_events DISABLE TRIGGER organization_board_event_history;
DELETE FROM organization_board_events WHERE tenant_id='07310000-0000-0000-0000-000000000001';
ALTER TABLE organization_board_events ENABLE TRIGGER organization_board_event_history;
DELETE FROM organization_board_event_streams WHERE tenant_id='07310000-0000-0000-0000-000000000001';
DELETE FROM work_events WHERE tenant_id='07310000-0000-0000-0000-000000000001';
DELETE FROM work_event_streams WHERE tenant_id='07310000-0000-0000-0000-000000000001';
DELETE FROM boards WHERE tenant_id='07310000-0000-0000-0000-000000000001';
DELETE FROM organization_members WHERE tenant_id='07310000-0000-0000-0000-000000000001';
DELETE FROM organizations WHERE id='07310000-0000-0000-0000-000000000001';
DELETE FROM users WHERE id='07310000-0000-0000-0000-000000000041';
COMMIT;
SQL
  fi
  rm -rf "$scratch"
}
trap cleanup EXIT
psql -X -v ON_ERROR_STOP=1 <<'SQL' >/dev/null
BEGIN;
INSERT INTO organizations(id,name,created_at,updated_at) VALUES('07310000-0000-0000-0000-000000000001','Ordered journal fixture',now(),now());
INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('07310000-0000-0000-0000-000000000041','journal-order@example.test','JOURNAL-ORDER@EXAMPLE.TEST','Ordered actor','ACTIVE',true,'fixture',now(),now());
INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 VALUES(gen_random_uuid(),'07310000-0000-0000-0000-000000000001','07310000-0000-0000-0000-000000000041','OWNER','ACTIVE');
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('07310000-0000-0000-0000-000000000011','07310000-0000-0000-0000-000000000001','Ordered Board one',now(),now()),
 ('07310000-0000-0000-0000-000000000012','07310000-0000-0000-0000-000000000001','Ordered Board two',now(),now());
INSERT INTO work_event_streams(tenant_id,board_id,last_sequence) SELECT tenant_id,id,1 FROM boards WHERE tenant_id='07310000-0000-0000-0000-000000000001';
COMMIT;
SQL
seeded=true
query() { psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
observe() {
  for ((attempt=0;attempt<200;attempt++)); do
    if [[ $(query "$1") == t ]]; then return; fi
    sleep 0.05
  done
  echo "Required journal lock observation did not occur: $2" >&2; return 1
}
# A held advisory capability releases the first transaction only after the
# second is observed waiting on its real Organization counter transaction.
mkfifo "$scratch/gate"
PGAPPNAME=strataai-journal-order-gate psql -X -v ON_ERROR_STOP=1 < "$scratch/gate" > "$scratch/gate.log" 2>&1 & gate_pid=$!
exec {gate_fd}> "$scratch/gate"
printf 'SELECT pg_advisory_lock(731000001);\n' >&"$gate_fd"
observe "SELECT EXISTS(SELECT 1 FROM pg_stat_activity a JOIN pg_locks l ON l.pid=a.pid WHERE a.application_name='strataai-journal-order-gate' AND l.locktype='advisory' AND l.granted);" gate-held
PGAPPNAME=strataai-journal-order-first psql -X -v ON_ERROR_STOP=1 <<'SQL' > "$scratch/first.log" 2>&1 & first_pid=$!
BEGIN;
SET LOCAL ROLE strataai_api_runtime;
SET LOCAL app.tenant_id='07310000-0000-0000-0000-000000000001';
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('07310000-0000-0000-0000-000000000001','07310000-0000-0000-0000-000000000051','07310000-0000-0000-0000-000000000011',1,
 '07310000-0000-0000-0000-000000000041','BOARD_CREATED','Board','07310000-0000-0000-0000-000000000011',1,'journal-order-first',now()+interval '1 day');
SELECT pg_advisory_xact_lock(731000001);
COMMIT;
SQL
observe "SELECT EXISTS(SELECT 1 FROM pg_stat_activity a JOIN pg_locks l ON l.pid=a.pid WHERE a.application_name='strataai-journal-order-first' AND l.locktype='advisory' AND NOT l.granted);" first-source-held
PGAPPNAME=strataai-journal-order-second psql -X -v ON_ERROR_STOP=1 <<'SQL' > "$scratch/second.log" 2>&1 & second_pid=$!
BEGIN;
SET LOCAL ROLE strataai_api_runtime;
SET LOCAL app.tenant_id='07310000-0000-0000-0000-000000000001';
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('07310000-0000-0000-0000-000000000001','07310000-0000-0000-0000-000000000052','07310000-0000-0000-0000-000000000012',1,
 '07310000-0000-0000-0000-000000000041','BOARD_CREATED','Board','07310000-0000-0000-0000-000000000012',1,'journal-order-second',now()-interval '1 day');
COMMIT;
SQL
observe "SELECT EXISTS(SELECT 1 FROM pg_stat_activity a JOIN pg_locks l ON l.pid=a.pid WHERE a.application_name='strataai-journal-order-second' AND l.locktype='transactionid' AND NOT l.granted);" second-counter-wait
# Nothing from either transaction can be read as committed before this release.
test "$(query "SELECT count(*) FROM organization_board_events WHERE tenant_id='07310000-0000-0000-0000-000000000001'")" = 0
printf 'SELECT pg_advisory_unlock(731000001);\n\\q\n' >&"$gate_fd"
exec {gate_fd}>&-
wait "$gate_pid"; gate_pid=''
wait "$first_pid"; first_pid=''
wait "$second_pid"; second_pid=''
test "$(query "SELECT string_agg(event_id::text,',' ORDER BY sequence) FROM organization_board_events WHERE tenant_id='07310000-0000-0000-0000-000000000001'")" = '07310000-0000-0000-0000-000000000051,07310000-0000-0000-0000-000000000052'
test "$(query "SELECT last_sequence FROM organization_board_event_streams WHERE tenant_id='07310000-0000-0000-0000-000000000001'")" = 2
test "$(query "SELECT count(*) FROM organization_board_events j JOIN work_events e USING(tenant_id,event_id) WHERE j.tenant_id='07310000-0000-0000-0000-000000000001' AND e.ready_at IS NOT NULL")" = 0
echo 'Restricted cross-Board Organization journal: observed counter wait, no uncommitted disclosure, committed order independent of source clocks, and unchanged pending delivery passed.'
