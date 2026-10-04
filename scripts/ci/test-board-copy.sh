#!/usr/bin/env bash
set -euo pipefail
test "${CI:-}" = true || { echo 'Disposable Board copy fixture requires CI.' >&2; exit 1; }
umask 077
scratch=$(mktemp -d); revoked=false; base=http://localhost:8088
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -qAt -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1"; }
cleanup() {
  if $revoked; then admin 'GRANT INSERT ON checklist_items TO strataai_api_runtime;' >/dev/null || true; fi
  admin 'DROP TRIGGER IF EXISTS ci_board_copy_publication_refusal ON work_events; DROP FUNCTION IF EXISTS ci_board_copy_publication_refusal(); DROP SEQUENCE IF EXISTS ci_board_copy_publication_reached;' >/dev/null || true
  rm -rf "$scratch"
}
trap cleanup EXIT
trap 'echo "Board copy fixture failed at line $LINENO; status=${code:-none}" >&2' ERR
uuid() { cat /proc/sys/kernel/random/uuid; }
request() {
  local method=$1 path=$2 body=$3 key=${4:-$(uuid)}
  curl --max-time 60 --silent --show-error -b "$scratch/cookies" -X "$method" -H 'X-StrataAI-Request: 1' \
    -H 'Content-Type: application/json' -H "Idempotency-Key: $key" -d "$body" -o "$scratch/response" -w '%{http_code}' "$base$path"
}
jq -nc --arg email "board-copy-${RANDOM}-${RANDOM}@example.test" '{email:$email,password:"board-copy-correct-horse-battery",displayName:"Board copy fixture"}' > "$scratch/credentials"
curl --fail --silent --show-error -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/credentials")" "$base/auth/register" > "$scratch/user"
curl --fail --silent --show-error -c "$scratch/cookies" -H 'X-StrataAI-Request: 1' -H 'Content-Type: application/json' -d "$(cat "$scratch/credentials")" "$base/auth/login" >/dev/null
actor=$(jq -r '.user.id' "$scratch/user")
test "$(request POST /organizations '{"name":"Board copy release"}')" = 201
org=$(jq -r '.organization.id' "$scratch/response")
test "$(request POST /boards "$(jq -nc --arg org "$org" '{organizationId:$org,name:"Capacity source",visibility:"PRIVATE"}')")" = 201
source=$(jq -r '.id' "$scratch/response")
for id in "$actor" "$org" "$source"; do [[ "$id" =~ ^[0-9a-f-]{36}$ ]]; done
# Trusted disposable capacity data. The copy itself always uses the authenticated
# release API and its restricted role, and must never truncate to UI page sizes.
admin "INSERT INTO board_lists(id,tenant_id,board_id,name,rank,lifecycle_state,archived_at,created_at,updated_at)
  SELECT gen_random_uuid(),'$org','$source','List '||n,lpad(n::text,30,'0'),
    CASE WHEN n=200 THEN 'ARCHIVED' ELSE 'ACTIVE' END,CASE WHEN n=200 THEN now() ELSE NULL END,now(),now() FROM generate_series(1,200) n;
  INSERT INTO cards(id,tenant_id,board_id,list_id,title,description,rank,lifecycle_state,archived_at,created_at,updated_at)
  SELECT gen_random_uuid(),'$org','$source',l.id,'Card '||n,'Copied description',lpad(n::text,30,'0'),
    CASE WHEN n=25 THEN 'ARCHIVED' ELSE 'ACTIVE' END,CASE WHEN n=25 THEN now() ELSE NULL END,now(),now()
    FROM board_lists l CROSS JOIN generate_series(1,25) n WHERE l.tenant_id='$org' AND l.board_id='$source';
  INSERT INTO board_labels(id,tenant_id,board_id,name,color,rank)
    SELECT gen_random_uuid(),'$org','$source','Distinct shared label','blue',lpad(n::text,30,'0') FROM generate_series(1,2) n;
  INSERT INTO card_labels(tenant_id,board_id,card_id,label_id)
    SELECT '$org','$source',c.id,l.id FROM cards c CROSS JOIN board_labels l WHERE c.tenant_id='$org' AND c.board_id='$source' AND l.tenant_id='$org' AND l.board_id='$source';
  INSERT INTO checklists(id,tenant_id,card_id,title,rank,created_at,updated_at)
    SELECT gen_random_uuid(),'$org',c.id,'Independent checklist',lpad('1',30,'0'),now(),now() FROM cards c JOIN board_lists l ON l.id=c.list_id AND l.tenant_id=c.tenant_id
    WHERE c.tenant_id='$org' AND c.board_id='$source' AND c.rank=lpad('1',30,'0') AND l.rank IN(lpad('1',30,'0'),lpad('2',30,'0'));
  INSERT INTO checklist_items(id,tenant_id,checklist_id,text,rank,created_at,updated_at)
    SELECT gen_random_uuid(),'$org',c.id,'Work '||n,lpad(n::text,30,'0'),now(),now() FROM checklists c CROSS JOIN generate_series(1,63) n WHERE c.tenant_id='$org';
  UPDATE checklist_items SET completed=true,completed_at=now(),completed_by='$actor',version=2,updated_at=now()
    WHERE tenant_id='$org' AND rank=lpad('1',30,'0');" >/dev/null
state() { admin "SELECT md5(jsonb_build_object(
  'boards',(SELECT jsonb_agg(to_jsonb(b) ORDER BY id) FROM boards b WHERE tenant_id='$org'),
  'lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id='$org'),
  'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c WHERE tenant_id='$org'),
  'labels',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_labels l WHERE tenant_id='$org'),
  'associations',(SELECT jsonb_agg(to_jsonb(a) ORDER BY card_id,label_id) FROM card_labels a WHERE tenant_id='$org'),
  'checklists',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM checklists c WHERE tenant_id='$org'),
  'items',(SELECT jsonb_agg(to_jsonb(i) ORDER BY id) FROM checklist_items i WHERE tenant_id='$org'),
  'members',(SELECT jsonb_agg(to_jsonb(m) ORDER BY board_id,user_id) FROM board_members m WHERE tenant_id='$org'),
  'list_routes',(SELECT jsonb_agg(to_jsonb(r) ORDER BY list_id) FROM list_routes r WHERE tenant_id='$org'),
  'card_routes',(SELECT jsonb_agg(to_jsonb(r) ORDER BY card_id) FROM card_routes r WHERE tenant_id='$org'),
  'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY board_id) FROM work_event_streams s WHERE tenant_id='$org'),
  'events',(SELECT jsonb_agg(to_jsonb(e)-'ready_at' ORDER BY event_id) FROM work_events e WHERE tenant_id='$org'),
  'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a WHERE tenant_id='$org'),
  'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY actor_id,key_id) FROM work_command_replays r WHERE tenant_id='$org'),
  'jobs',(SELECT count(*) FROM background_jobs WHERE tenant_id='$org'))::text);"; }
key=$(uuid); before=$(state)
admin 'REVOKE INSERT ON checklist_items FROM strataai_api_runtime;' >/dev/null; revoked=true
code=$(request POST "/boards/$source/copy" '{"name":"Independent Board","version":1}' "$key")
test "$code" = 503; jq -e '.code=="work_storage_unavailable"' "$scratch/response" >/dev/null
test "$(state)" = "$before"
test "$(admin "SELECT count(*) FROM list_routes WHERE tenant_id='$org';")" = 200
test "$(admin "SELECT count(*) FROM card_routes WHERE tenant_id='$org';")" = 5000
admin 'GRANT INSERT ON checklist_items TO strataai_api_runtime;' >/dev/null; revoked=false
# Refuse only the final copied-Board event, after the whole graph exists.
# BOARD_CREATED remains writable, so this proves rollback beyond creation.
admin "CREATE SEQUENCE ci_board_copy_publication_reached;
  GRANT USAGE ON SEQUENCE ci_board_copy_publication_reached TO strataai_api_runtime;
  CREATE FUNCTION ci_board_copy_publication_refusal() RETURNS trigger LANGUAGE plpgsql AS \$\$
  BEGIN
    IF NEW.tenant_id='$org'::uuid AND NEW.event_type='BOARD_COPIED' THEN
      IF (SELECT count(*) FROM cards WHERE tenant_id=NEW.tenant_id AND board_id=NEW.board_id)<>5000 THEN
        RAISE EXCEPTION 'Board copy publication fixture did not reach the complete graph';
      END IF;
      PERFORM nextval('ci_board_copy_publication_reached');
      RAISE EXCEPTION 'Disposable Board copy publication refusal' USING ERRCODE='42501';
    END IF;
    RETURN NEW;
  END; \$\$;
  CREATE TRIGGER ci_board_copy_publication_refusal BEFORE INSERT ON work_events
    FOR EACH ROW EXECUTE FUNCTION ci_board_copy_publication_refusal();" >/dev/null
code=$(request POST "/boards/$source/copy" '{"name":"Independent Board","version":1}' "$key")
test "$code" = 503; jq -e '.code=="work_storage_unavailable"' "$scratch/response" >/dev/null
test "$(admin 'SELECT last_value=1 AND is_called FROM ci_board_copy_publication_reached;')" = t
test "$(state)" = "$before"
admin 'DROP TRIGGER ci_board_copy_publication_refusal ON work_events; DROP FUNCTION ci_board_copy_publication_refusal(); DROP SEQUENCE ci_board_copy_publication_reached;' >/dev/null
test "$(request POST "/boards/$source/copy" '{"name":"Independent Board","version":1}' "$key")" = 201
cp "$scratch/response" "$scratch/receipt"; copy=$(jq -r '.id' "$scratch/response"); [[ "$copy" =~ ^[0-9a-f-]{36}$ ]]
jq -e --arg org "$org" --arg source "$source" '.id!=$source and .organizationId==$org and .visibility=="PRIVATE" and .version==1' "$scratch/response" >/dev/null
test "$(admin "SELECT count(*) FROM board_lists WHERE tenant_id='$org' AND board_id='$copy';")" = 200
test "$(admin "SELECT count(*) FROM cards WHERE tenant_id='$org' AND board_id='$copy';")" = 5000
test "$(admin "SELECT count(*) FROM card_labels WHERE tenant_id='$org' AND board_id='$copy';")" = 10000
test "$(admin "SELECT count(*) FROM board_labels WHERE tenant_id='$org' AND board_id='$copy';")" = 2
test "$(admin "SELECT count(*) FROM checklist_items i JOIN checklists l ON l.tenant_id=i.tenant_id AND l.id=i.checklist_id JOIN cards c ON c.tenant_id=l.tenant_id AND c.id=l.card_id WHERE c.tenant_id='$org' AND c.board_id='$copy' AND NOT i.completed AND i.version=1;")" = 126
test "$(admin "SELECT count(*) FROM board_members WHERE tenant_id='$org' AND board_id='$copy' AND user_id='$actor';")" = 1
test "$(admin "SELECT count(*) FROM work_events WHERE tenant_id='$org' AND board_id='$copy' AND event_type IN('BOARD_CREATED','BOARD_COPIED');")" = 2
after=$(state)
test "$(request POST "/boards/$source/copy" '{"name":"Independent Board","version":1}' "$key")" = 201
cmp "$scratch/receipt" "$scratch/response"; test "$(state)" = "$after"
test "$(request POST "/boards/$source/copy" '{"name":"Changed intent","version":1}' "$key")" = 409
test "$(request POST "/boards/$source/archive" '{"version":1}')" = 200
test "$(request POST "/boards/$source/copy" '{"name":"Independent Board","version":1}' "$key")" = 404
echo 'Exact-image Board copy retains full capacity, distinct labels, fresh content, complete-graph publication rollback and atomic retry recovery.'
