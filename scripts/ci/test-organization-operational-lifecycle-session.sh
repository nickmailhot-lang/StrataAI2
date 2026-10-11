#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable lifecycle session fixtures require CI.' >&2; exit 1; }
base="${1:-http://localhost:8080}"
scratch=$(mktemp -d)
trigger_installed=false
request_pid=''
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  local result=$?
  if test -n "$request_pid"; then wait "$request_pid" || result=1; fi
  if $trigger_installed; then
    admin 'DROP TRIGGER IF EXISTS ci_lifecycle_session_wait ON organization_operational_lifecycle_receipts; DROP FUNCTION IF EXISTS public.ci_lifecycle_session_wait();' >/dev/null || result=1
  fi
  rm -rf "$scratch"
  exit "$result"
}
trap cleanup EXIT
trap 'echo "Lifecycle session fence failed at line $LINENO" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
request() {
  curl --max-time 30 --silent --show-error -b "$scratch/cookies" -X "$1" \
    -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -H "Idempotency-Key: ${4:-$(uuid)}" \
    -d "$3" -D "$scratch/headers" -o "$scratch/response.json" -w '%{http_code}' "$base$2"
}
credentials=$(jq -nc --arg email "lifecycle-session-$(uuid)@example.test" '{email:$email,password:"Lifecycle-session-correct-horse!",displayName:"Lifecycle session fixture"}')
test "$(request POST /auth/register "$credentials")" = 201
actor=$(jq -r '.user.id' "$scratch/response.json")
[[ "$actor" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
admin "UPDATE users SET status='ACTIVE',email_verified=true,updated_at=GREATEST(updated_at,clock_timestamp()) WHERE id='$actor';" >/dev/null
login() { curl --max-time 30 --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$credentials" "$base/auth/login" >/dev/null; }
login
effects() {
  admin "SELECT md5(jsonb_build_object(
    'parent',(SELECT to_jsonb(o) FROM organizations o WHERE id='$org'),
    'history',(SELECT jsonb_agg(to_jsonb(h) ORDER BY version) FROM organization_operational_lifecycle_history h WHERE tenant_id='$org'),
    'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY version) FROM organization_operational_lifecycle_receipts r WHERE tenant_id='$org'),
    'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY version) FROM organization_operational_lifecycle_events e WHERE tenant_id='$org'),
    'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$org'),
    'jobs',(SELECT jsonb_agg(to_jsonb(j) ORDER BY id) FROM background_jobs j WHERE tenant_id='$org' AND job_type='ORGANIZATION_OPERATIONAL_LIFECYCLE_EVENT_READY'))::text);"
}
for target in SUSPENDED DEACTIVATED; do
  test "$(request POST /organizations '{"name":"Private lifecycle session parent"}')" = 201
  org=$(jq -r '.organization.id' "$scratch/response.json")
  [[ "$org" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
  session=$(admin "SELECT id FROM sessions WHERE user_id='$actor' AND revoked_at IS NULL ORDER BY created_at DESC,id LIMIT 1;")
  [[ "$session" =~ ^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$ ]]
  # Controlled timing on this disposable session allows actual server time to
  # expire while the restricted API transaction publishes its retry receipt.
  admin "CREATE FUNCTION public.ci_lifecycle_session_wait() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER AS \$\$
    BEGIN IF NEW.tenant_id='$org'::uuid THEN PERFORM pg_sleep(12); END IF; RETURN NEW; END; \$\$;
    CREATE TRIGGER ci_lifecycle_session_wait AFTER INSERT ON organization_operational_lifecycle_receipts
      FOR EACH ROW EXECUTE FUNCTION public.ci_lifecycle_session_wait();" >/dev/null
  trigger_installed=true
  admin "UPDATE sessions SET expires_at=clock_timestamp()+interval '10 seconds' WHERE id='$session';" >/dev/null
  before=$(effects)
  key=$(uuid)
  body=$(jq -nc --arg target "$target" '{status:$target,version:1}')
  request PATCH "/organizations/$org/operational-lifecycle" "$body" "$key" > "$scratch/status" & request_pid=$!
  for attempt in $(seq 1 100); do
    if test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_operational_lifecycle_receipts%';")" = 1; then break; fi
    sleep 0.1
  done
  test "$(admin "SELECT count(*) FROM pg_stat_activity WHERE usename='strataai_api_runtime' AND wait_event='PgSleep' AND query LIKE '%INSERT INTO organization_operational_lifecycle_receipts%';")" = 1
  wait "$request_pid"; request_pid=''
  test "$(cat "$scratch/status")" = 401
  jq -e '.code=="session_unavailable"' "$scratch/response.json" >/dev/null
  scripts/ci/assert-file-excludes.sh 'Private lifecycle session parent|Set-Cookie|Npgsql|INSERT INTO' "$scratch/response.json"
  scripts/ci/assert-file-excludes.sh '^[Ss]et-[Cc]ookie:' "$scratch/headers"
  test "$(effects)" = "$before"
  admin 'DROP TRIGGER ci_lifecycle_session_wait ON organization_operational_lifecycle_receipts; DROP FUNCTION public.ci_lifecycle_session_wait();' >/dev/null
  trigger_installed=false
  login
  test "$(request PATCH "/organizations/$org/operational-lifecycle" "$body" "$key")" = 200
  jq -e --arg target "$target" '.version==2 and .status==$target' "$scratch/response.json" >/dev/null
  test "$(admin "SELECT count(*) FROM organization_operational_lifecycle_receipts WHERE tenant_id='$org';")" = 1
done
echo 'Production lifecycle session expiry rolled back parent/history/audit/event/job/receipt; fresh sessions reused original keys.'
