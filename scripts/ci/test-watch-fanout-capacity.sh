#!/usr/bin/env bash
set -euo pipefail
umask 077
test "${CI:-}" = true || { echo 'Disposable watch fan-out fixture requires CI.' >&2; exit 1; }
org=$1; board=$2; card=$3; owner=$4; cookies=$5
for id in "$org" "$board" "$card" "$owner"; do [[ "$id" =~ ^[0-9a-fA-F-]{36}$ ]]; done
test -f "$cookies"
base=${STRATAAI_TEST_BASE_URL:-http://localhost:8088}
topology=${STRATAAI_CAPACITY_TOPOLOGY:-exact-release-images}
case "$topology" in exact-release-images|local-compiled-runtime) ;; *) exit 1;; esac
revision=${GITHUB_SHA:-}; [[ "$revision" =~ ^[0-9a-f]{40,64}$ ]]
mkdir -p artifacts/capacity; rm -f artifacts/capacity/watch-fanout.json
scratch=$(mktemp -d); trap 'rm -rf "$scratch"' EXIT
trap 'echo "Watch fan-out capacity fixture failed at line $LINENO" >&2' ERR
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
uuid() { cat /proc/sys/kernel/random/uuid; }
test "$(admin "SELECT (SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$board')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND lifecycle_state='ACTIVE')||'/'||
 (SELECT count(*) FROM cards WHERE tenant_id='$org' AND lifecycle_state='ARCHIVED');")" = '200/5000/100000'
list=$(admin "SELECT list_id FROM cards WHERE tenant_id='$org' AND board_id='$board' AND id='$card' AND lifecycle_state='ACTIVE';")
[[ "$list" =~ ^[0-9a-fA-F-]{36}$ ]]
# Synthetic accounts/grants/subscriptions are scale setup, not audited commands
# or evidence of 500 authenticated recipient sessions. The issuer is real HTTP.
test "$(admin "WITH candidates AS (SELECT gen_random_uuid() id,n FROM generate_series(1,510) n),
 accounts AS (INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 SELECT id,'watch-fanout-capacity-'||id||'@example.test',upper('watch-fanout-capacity-'||id||'@example.test'),
 'Fan-out capacity fixture',CASE WHEN n<=500 THEN 'ACTIVE' WHEN n<=505 THEN 'SUSPENDED' ELSE 'DEACTIVATED' END,
 true,'unused-fixture-hash',now(),now() FROM candidates RETURNING id),
 memberships AS (INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 SELECT gen_random_uuid(),'$org',id,'MEMBER','ACTIVE' FROM accounts RETURNING user_id),
 grants AS (INSERT INTO board_members(id,tenant_id,board_id,user_id,role,status,created_at,updated_at)
 SELECT gen_random_uuid(),'$org','$board',user_id,'MEMBER','ACTIVE',now(),now() FROM memberships RETURNING user_id),
 watching AS (INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,board_id,list_id,card_id,watching,created_at,updated_at,version)
 SELECT '$org',gen_random_uuid(),g.user_id,s.kind,s.entity,
 CASE WHEN s.kind='BOARD' THEN s.entity END,CASE WHEN s.kind='LIST' THEN s.entity END,CASE WHEN s.kind='CARD' THEN s.entity END,
 true,now(),now(),1 FROM grants g CROSS JOIN (VALUES('BOARD','$board'::uuid),('LIST','$list'::uuid),('CARD','$card'::uuid)) s(kind,entity)
 RETURNING user_id) SELECT count(*) FROM watching;")" = 1530
admin "INSERT INTO watch_subscriptions(tenant_id,id,user_id,entity_type,entity_id,board_id,watching,created_at,updated_at,version)
 VALUES('$org',gen_random_uuid(),'$owner','BOARD','$board','$board',true,now(),now(),1)
 ON CONFLICT(tenant_id,user_id,entity_type,entity_id) DO NOTHING;
 ANALYZE watch_subscriptions;" >/dev/null
test "$(admin "SELECT watching FROM watch_subscriptions WHERE tenant_id='$org' AND user_id='$owner' AND entity_type='BOARD' AND entity_id='$board';")" = t
admin "SELECT jsonb_build_object('version',version,'description',description) FROM cards WHERE tenant_id='$org' AND board_id='$board' AND id='$card';" > "$scratch/source.json"
version=$(jq -r '.version' "$scratch/source.json"); [[ "$version" =~ ^[1-9][0-9]*$ ]]; original_version=$version
# Retain only hashes/counts locally; bodies, keys, cookies and IDs never enter the report.
effects() { admin "SELECT jsonb_build_object(
 'card',(SELECT md5(to_jsonb(c)::text) FROM cards c WHERE tenant_id='$org' AND id='$card'),
 'events',(SELECT count(*) FROM work_events WHERE tenant_id='$org' AND correlation_id='watch-fanout-capacity'),
 'audit',(SELECT count(*) FROM audit_events WHERE tenant_id='$org' AND correlation_id='watch-fanout-capacity'),
 'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org' AND correlation_id='watch-fanout-capacity'),
 'receipts',(SELECT count(*) FROM work_command_replays WHERE tenant_id='$org'),
 'notifications',(SELECT md5(string_agg(md5(to_jsonb(n)::text),'' ORDER BY n.id)) FROM card_assignment_notifications n
 JOIN work_events e ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id WHERE e.tenant_id='$org' AND e.correlation_id='watch-fanout-capacity'),
 'journal',(SELECT md5(string_agg(md5(to_jsonb(j)::text),'' ORDER BY j.event_id)) FROM notification_events j
 JOIN card_assignment_notifications n ON n.tenant_id=j.tenant_id AND n.id=j.notification_id
 JOIN work_events e ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id WHERE e.tenant_id='$org' AND e.correlation_id='watch-fanout-capacity'));"; }
effects > "$scratch/before.json"; : > "$scratch/seconds"
for ((sample=0;sample<20;sample++)); do
 key=$(uuid)
 jq -nc --argjson version "$version" --arg title "Fan-out sample $sample" --slurpfile source "$scratch/source.json" \
 '{title:$title,description:$source[0].description,version:$version}' > "$scratch/body.json"
 curl --max-time 60 --fail --silent --show-error -b "$cookies" -X PATCH -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -H 'X-Correlation-ID: watch-fanout-capacity' -H "Idempotency-Key: $key" --data-binary "@$scratch/body.json" \
 -o "$scratch/ack.json" -w '%{time_total}\n' "$base/cards/$card" >> "$scratch/seconds"
 version=$((version+1))
 jq -e --arg card "$card" --arg org "$org" --arg board "$board" --argjson version "$version" --arg title "Fan-out sample $sample" \
 '.id==$card and .organizationId==$org and .boardId==$board and .version==$version and .title==$title' "$scratch/ack.json" >/dev/null
 test "$(admin "SELECT count(*) FROM card_assignment_notifications n JOIN work_events e ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id
 WHERE e.tenant_id='$org' AND e.correlation_id='watch-fanout-capacity';")" = "$(((sample+1)*500))"
 effects > "$scratch/committed.json"
 curl --max-time 60 --fail --silent --show-error -b "$cookies" -X PATCH -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' \
 -H 'X-Correlation-ID: watch-fanout-capacity' -H "Idempotency-Key: $key" --data-binary "@$scratch/body.json" "$base/cards/$card" > "$scratch/replay.json"
 jq -se '.[0]==.[1]' "$scratch/ack.json" "$scratch/replay.json" >/dev/null
 effects > "$scratch/replayed.json"
 test "$(jq -Sc . "$scratch/committed.json")" = "$(jq -Sc . "$scratch/replayed.json")"
done
effects > "$scratch/after.json"
jq -se '.[1].events==.[0].events+20 and .[1].audit==.[0].audit+20 and .[1].jobs==.[0].jobs+20 and .[1].receipts==.[0].receipts+20' "$scratch/before.json" "$scratch/after.json" >/dev/null
test "$(admin "SELECT version FROM cards WHERE tenant_id='$org' AND id='$card';")" = "$((original_version+20))"
# Each exact immutable source has exactly 500 distinct eligible recipients and
# one private creation event per notification; overlap and actor do not add rows.
test "$(admin "SELECT count(*) FROM (SELECT e.event_id FROM work_events e
 JOIN card_assignment_notifications n ON n.tenant_id=e.tenant_id AND n.event_id=e.event_id
 JOIN users u ON u.id=n.recipient_id
 WHERE e.tenant_id='$org' AND e.correlation_id='watch-fanout-capacity' AND e.event_type='CARD_UPDATED'
 AND n.notification_type='CARD_UPDATED' AND n.actor_id='$owner' AND n.card_id='$card' AND n.board_id='$board'
 AND n.card_version=e.entity_version AND n.created_at=e.created_at AND u.status='ACTIVE'
 AND u.email LIKE 'watch-fanout-capacity-%@example.test'
 GROUP BY e.event_id HAVING count(*)=500 AND count(DISTINCT n.recipient_id)=500) exact;")" = 20
test "$(admin "SELECT count(*)=10000 AND count(DISTINCT j.notification_id)=10000 AND count(DISTINCT j.event_id)=10000 FROM notification_events j JOIN card_assignment_notifications n ON n.tenant_id=j.tenant_id AND n.id=j.notification_id
 JOIN work_events e ON e.tenant_id=n.tenant_id AND e.event_id=n.event_id
 WHERE e.tenant_id='$org' AND e.correlation_id='watch-fanout-capacity' AND j.recipient_id=n.recipient_id AND j.actor_id='$owner'
 AND j.event_type='NOTIFICATION_CREATED' AND j.version=1 AND j.created_at=n.created_at AND j.board_id=n.board_id;")" = t
test "$(admin "SELECT count(*) FROM (SELECT recipient_id FROM notification_event_streams WHERE tenant_id='$org'
 AND recipient_id IN (SELECT id FROM users WHERE status='ACTIVE' AND email LIKE 'watch-fanout-capacity-%@example.test')
 AND last_sequence=20) exact;")" = 500
mkdir -p artifacts/capacity
jq -nc --arg revision "$revision" --arg topology "$topology" --slurpfile samples "$scratch/seconds" \
 '{schemaVersion:1,revision:$revision,topology:$topology,status:(if ($samples|sort|.[18]*1000)<500 then "passed" else "failed" end),
 conditions:{clients:1,serial:true,intentionalNetworkLatencyMs:0,clock:"curl-total"},
 fixture:{lists:200,activeCards:5000,archivedCards:100000,eligibleRecipients:500,inactiveCandidates:10,watchTuplesPerCandidate:3,commands:20},
 verified:{canonicalRevisionPerCommand:true,originalRetryUnchanged:true,exactSourceRecipientCount:true,actorSuppressed:true,inactiveExcluded:true,
 notifications:10000,recipientJournalEvents:10000,oneJournalPerNotification:true,oneAuditEventJobReceiptPerCommand:true},
 milliseconds:{samples:($samples|map(.*1000)),mutationP95:($samples|sort|.[18]*1000)},budget:{mutationP95Ms:500}}' > artifacts/capacity/watch-fanout.json
# Failed timing evidence is retained before the mandatory gate rejects it.
jq -e '.milliseconds.mutationP95 < .budget.mutationP95Ms' artifacts/capacity/watch-fanout.json >/dev/null
echo 'Watch fan-out: 500 eligible recipients, overlap/inactive/actor suppression, 20 actual revisions, exact retries and persisted recipient journals passed within the mutation budget.'
