#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable moved-notification fixture requires CI.' >&2; exit 1; }
org=$1; source=$2; destination=$3; recipient=$4; recipient_cookies=$5; owner_cookies=$6
for id in "$org" "$source" "$destination" "$recipient"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$recipient_cookies"; test -f "$owner_cookies"
scratch=$(mktemp -d); gate_pid=''; request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
 if test -n "$gate_pid"; then printf 'ROLLBACK;\n\\q\n' >&3 || true; exec 3>&-; wait "$gate_pid" || true; fi
 if test -n "$request_pid"; then kill "$request_pid" 2>/dev/null || true; wait "$request_pid" 2>/dev/null || true; fi
 rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Moved-notification admission fixture failed at line $LINENO" >&2' ERR
effects() { admin "SELECT md5(jsonb_build_object(
 'notifications',(SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n WHERE tenant_id='$org'),
 'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'))::text);"; }
before=$(effects)
# Hold the first canonical gate so no destination membership lock has yet been
# taken. Observe a real blocked API request before withdrawing its destination.
gate=$(printf '%s\n%s\n' "$source" "$destination" | LC_ALL=C sort | head -n 1)
mkfifo "$scratch/gate.in"
docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' < "$scratch/gate.in" > "$scratch/gate.log" 2>&1 & gate_pid=$!
exec 3> "$scratch/gate.in"
printf 'BEGIN;\nSELECT id FROM boards WHERE tenant_id=%s AND id=%s FOR UPDATE;\n\\echo gate_locked\n' "'$org'" "'$gate'" >&3
locked=false
for ((attempt=0;attempt<100;attempt++)); do
 if grep -q '^gate_locked$' "$scratch/gate.log"; then locked=true; break; fi
 kill -0 "$gate_pid"; sleep 0.05
done
$locked
curl --max-time 60 --silent --show-error -b "$recipient_cookies" -o "$scratch/response.json" -w '%{http_code}' \
 "http://localhost:8088/organizations/$org/notifications" > "$scratch/code" & request_pid=$!
blocked=false
for ((attempt=0;attempt<100;attempt++)); do
 count=$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event_type='Lock' AND query LIKE '%SELECT id FROM boards%';")
 if test "$count" -gt 0; then blocked=true; break; fi
 kill -0 "$request_pid"; sleep 0.05
done
$blocked
admin "UPDATE board_members SET status='REMOVED',version=version+1,updated_at=GREATEST(updated_at,now())
 WHERE tenant_id='$org' AND board_id='$destination' AND user_id='$recipient';" >/dev/null
printf 'COMMIT;\n\\q\n' >&3; exec 3>&-; wait "$gate_pid"; gate_pid=''
wait "$request_pid"; request_pid=''
test "$(cat "$scratch/code")" = 404
jq -e 'has("items")|not' "$scratch/response.json" >/dev/null
test "$(effects)" = "$before"
# Restore only through the authorized release command, before continuing the
# independent historical read-receipt/revocation cases in the parent fixture.
test "$(curl --max-time 60 --silent --show-error -b "$owner_cookies" -X PATCH -H 'X-StrataAI-Request: 1' \
 -H 'Content-Type: application/json' -H "Idempotency-Key: $(cat /proc/sys/kernel/random/uuid)" -d '{"role":"MEMBER"}' \
 -o "$scratch/restored.json" -w '%{http_code}' "http://localhost:8088/boards/$destination/members/$recipient")" = 200
echo 'Moved notification destination revocation after an observed live canonical Board gate wait was refused without disclosure or command effects.'
