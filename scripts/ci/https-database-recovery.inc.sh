# Sourced by test-release-https-startup.sh after its one-Card HTTPS fixture exists.
# Runtime commands use only the assembled bundle and its already loaded images.
capture_recovery_activity_cursor() {
  local token
  request GET "/boards/$board/activity" 200
  jq -e --arg org "$organization" --arg board "$board" '
    .organizationId==$org and .targetId==$board and (.items|length)==50 and
    (.nextCursor|type=="string" and length>20 and length<=2048 and test("^[A-Za-z0-9_-]+$"))' "$scratch/response" >/dev/null
  cp "$scratch/response" "$scratch/activity-first-page.json"
  token=$(jq -er '.nextCursor' "$scratch/response")
  activity_path="/boards/$board/activity?after=$token"
  request GET "$activity_path" 200
  jq -e --arg org "$organization" --arg board "$board" --slurpfile first "$scratch/activity-first-page.json" '
    .organizationId==$org and .targetId==$board and .nextCursor==null and
    (.items|length>0 and length<=50) and
    ([$first[0].items[].eventId] as $seen |
      all(.items[]; .organizationId==$org and .boardId==$board and
        (.eventId as $id | ($seen|index($id))==null)))' "$scratch/response" >/dev/null
  cp "$scratch/response" "$scratch/activity-continuation.json"
}
check_recovery_activity_cursor() {
  request GET "$activity_path" 200
  jq -e --slurpfile before "$scratch/activity-continuation.json" '.==$before[0]' "$scratch/response" >/dev/null
  # Same key material must not permit rebinding a Board cursor to its Card.
  request GET "/cards/$card/activity?after=${activity_path#*after=}" 400
  jq -e '.code=="invalid_activity_cursor"' "$scratch/response" >/dev/null
}
recovery_source_fingerprint() {
  {
    docker exec "$1" pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" --format plain \
      | python3 "$(host_path "$verification_dir/normalize-recovery-dump.py")" || return 1
    docker exec "$1" psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -v ON_ERROR_STOP=1 \
      -c "SELECT jsonb_build_array(conrelid::regclass::text,contypid::regtype::text,conname,pg_get_constraintdef(oid,true)) FROM pg_constraint WHERE contype='c' AND connamespace='public'::regnamespace ORDER BY 1" || return 1
  } | sha256sum
}
check_https_database_recovery() {
  local source_origin=$origin source_port=$STRATAAI_HTTPS_PORT
  local source_api_password=$STRATAAI_API_DB_PASSWORD source_worker_password=$STRATAAI_WORKER_DB_PASSWORD
  local source_scope=${STRATAAI_WORKER_ORGANIZATION_IDS-}
  local source_database target_database source_identity target_identity key_volume source_fingerprint after_fingerprint
  local recovery_binding recovery_port archive_digest host container
  local activity_path source_api target_api target_key_volume key_digest actual_key_digest key_fingerprint recovered_key_fingerprint
  source_database=$("${compose[@]}" ps -q postgres)
  source_api=$("${compose[@]}" ps -q api)
  key_volume=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/strataai/activity-keys"}}{{.Name}}{{end}}{{end}}' "$source_api")
  test -n "$key_volume"
  # One extra history page, not a capacity/performance fixture. Synthetic rows
  # exercise the real protected continuation without changing the task version.
  docker exec -i "$source_database" psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 > "$scratch/recovery-history.log" 2>&1 <<SQL
BEGIN;
WITH stream AS (
 UPDATE work_event_streams SET last_sequence=last_sequence+51,updated_at=clock_timestamp()
 WHERE tenant_id='$organization' AND board_id='$board' RETURNING last_sequence)
INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at,ready_at)
 SELECT '$organization',gen_random_uuid(),'$board',s.last_sequence-51+n,'$user','CARD_UPDATED','Card','$card',1,
 'https-recovery-history',timestamptz '2025-01-01 00:00:00+00'+n*interval '1 second',
 timestamptz '2025-01-01 00:00:00+00'+n*interval '1 second'
 FROM stream s CROSS JOIN generate_series(1,51) n;
COMMIT;
SQL
  capture_recovery_activity_cursor
  cp "$scratch/cookies" "$scratch/source-cookies"
  "${compose[@]}" stop --timeout 20 api worker >/dev/null
  source_fingerprint=$(recovery_source_fingerprint "$source_database")
  source_identity=$(docker exec "$source_database" psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -v ON_ERROR_STOP=1 -c 'SELECT system_identifier FROM pg_control_system()')
  MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -sha256 -days 1 \
    -subj /CN=strataai-https-recovery -keyout "$(host_path "$scratch/recovery.key")" \
    -out "$(host_path "$scratch/recovery.crt")" > "$scratch/recovery-certificate.log" 2>&1
  STRATAAI_BACKUP_DB_USER="$POSTGRES_USER" bash backup-postgres.sh "$source_database" "$POSTGRES_DB" \
    "$scratch/recovery.crt" "$scratch/recovery.cms" > "$scratch/recovery-backup.log" 2>&1
  archive_digest=$(sha256sum < "$scratch/recovery.cms"); archive_digest=${archive_digest%% *}
  # Export only the stopped fixture's keys. Plaintext is never a release asset;
  # authenticate a private encrypted snapshot before validating/importing it.
  docker cp "$source_api:/var/lib/strataai/activity-keys/." - 2> "$scratch/key-export.log" \
    | openssl cms -encrypt -binary -stream -aes-256-gcm -outform DER \
        -recip "$(host_path "$scratch/recovery.crt")" -out "$(host_path "$scratch/keys.cms")" 2> "$scratch/key-encrypt.log"
  key_digest=$(sha256sum < "$scratch/keys.cms"); key_digest=${key_digest%% *}
  cp "$scratch/keys.cms" "$scratch/key-snapshot.cms"
  actual_key_digest=$(sha256sum < "$scratch/key-snapshot.cms"); actual_key_digest=${actual_key_digest%% *}
  test "$actual_key_digest" = "$key_digest"
  if openssl cms -decrypt -binary -inform DER -in "$(host_path "$scratch/key-snapshot.cms")" \
      -recip "$(host_path "$scratch/recovery.crt")" -inkey "$(host_path "$scratch/wrong.key")" \
      -out "$(host_path "$scratch/refused-keys.tar")" > "$scratch/key-refusal.log" 2>&1; then return 1; fi
  rm -f "$scratch/refused-keys.tar"
  openssl cms -decrypt -binary -inform DER -in "$(host_path "$scratch/key-snapshot.cms")" \
    -recip "$(host_path "$scratch/recovery.crt")" -inkey "$(host_path "$scratch/recovery.key")" \
    -out "$(host_path "$scratch/restored-keys.tar")" > "$scratch/key-decrypt.log" 2>&1
  key_fingerprint=$(python3 "$(host_path "$verification_dir/verify-key-recovery-archive.py")" \
    "$(host_path "$scratch/restored-keys.tar")" "$(host_path "$scratch/canonical-keys.tar")")
  recovery_compose=(docker compose --project-name "$project-recovery" --env-file "$scratch/fixture.env" \
    -f compose.release.yml -f compose.https.yml)
  export STRATAAI_HTTPS_PORT=0
  STRATAAI_API_DB_PASSWORD=$(openssl rand -hex 24); STRATAAI_WORKER_DB_PASSWORD=$(openssl rand -hex 24)
  export STRATAAI_API_DB_PASSWORD STRATAAI_WORKER_DB_PASSWORD
  if [ "${GITHUB_ACTIONS:-}" = true ]; then
    for secret in "$STRATAAI_API_DB_PASSWORD" "$STRATAAI_WORKER_DB_PASSWORD"; do echo "::add-mask::$secret"; done
  fi
  export STRATAAI_WORKER_ORGANIZATION_IDS="$organization"
  recovery_started=true
  "${recovery_compose[@]}" up -d --no-build --pull never --wait --wait-timeout 120 postgres >/dev/null
  target_database=$("${recovery_compose[@]}" ps -q postgres)
  test "$target_database" != "$source_database"
  test "$(docker inspect --format '{{.Image}}' "$target_database")" = "$(docker inspect --format '{{.Image}}' "$source_database")"
  target_identity=$(docker exec "$target_database" psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -v ON_ERROR_STOP=1 -c 'SELECT system_identifier FROM pg_control_system()')
  [[ "$source_identity" =~ ^[0-9]+$ && "$target_identity" =~ ^[0-9]+$ && "$source_identity" != "$target_identity" ]]
  "${recovery_compose[@]}" exec -T postgres psql -X -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1 > "$scratch/recovery-roles.log" 2>&1 <<'SQL'
CREATE ROLE strataai_api_runtime NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT;
CREATE ROLE strataai_worker_runtime NOLOGIN NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT;
SQL
  STRATAAI_RESTORE_DB_USER="$POSTGRES_USER" STRATAAI_RESTORE_TMPDIR="$scratch" \
    bash restore-postgres.sh "$target_database" "$POSTGRES_DB" "$scratch/recovery.crt" "$scratch/recovery.key" \
      "$scratch/recovery.cms" "$archive_digest" > "$scratch/recovery-restore.log" 2>&1
  COMPOSE_PROJECT_NAME="$project-recovery" COMPOSE_FILE=compose.release.yml bash provision-runtime-roles.sh > "$scratch/recovery-provision.log" 2>&1
  "${recovery_compose[@]}" create --no-recreate --no-build --pull never api >/dev/null
  test "$("${recovery_compose[@]}" ps -q postgres)" = "$target_database"
  target_api=$("${recovery_compose[@]}" ps --all -q api)
  test "$(docker inspect --format '{{.State.Status}}' "$target_api")" = created
  target_key_volume=$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/strataai/activity-keys"}}{{.Name}}{{end}}{{end}}' "$target_api")
  test -n "$target_key_volume" && test "$target_key_volume" != "$key_volume"
  docker cp "$target_api:/var/lib/strataai/activity-keys/." - > "$scratch/empty-keys.tar" 2> "$scratch/key-target.log"
  python3 "$(host_path "$verification_dir/verify-key-recovery-archive.py")" --empty "$(host_path "$scratch/empty-keys.tar")" >/dev/null
  docker cp - "$target_api:/var/lib/strataai/activity-keys/" < "$scratch/canonical-keys.tar" > "$scratch/key-import.log" 2>&1
  docker cp "$target_api:/var/lib/strataai/activity-keys/." - > "$scratch/target-keys.tar" 2> "$scratch/key-target.log"
  recovered_key_fingerprint=$(python3 "$(host_path "$verification_dir/verify-key-recovery-archive.py")" \
    "$(host_path "$scratch/target-keys.tar")" "$(host_path "$scratch/target-canonical-keys.tar")")
  test "$recovered_key_fingerprint" = "$key_fingerprint"
  rm -f "$scratch/restored-keys.tar" "$scratch/canonical-keys.tar" "$scratch/empty-keys.tar" "$scratch/target-keys.tar" "$scratch/target-canonical-keys.tar"
  "${recovery_compose[@]}" up -d --no-build --pull never --wait --wait-timeout 180 >/dev/null
  recovery_binding=$("${recovery_compose[@]}" port web 443); recovery_port=${recovery_binding##*:}
  [[ "$recovery_port" =~ ^[0-9]+$ ]]
  origin="https://localhost:$recovery_port"
  export STRATAAI_HTTPS_PORT="$recovery_port" STRATAAI_PUBLIC_ORIGIN="$origin" STRATAAI_REALTIME_PUBLIC_ORIGIN="$origin"
  "${recovery_compose[@]}" up -d --no-deps --no-build --pull never --force-recreate --wait --wait-timeout 120 api worker web >/dev/null
  test "$("${recovery_compose[@]}" port web 443)" = "$recovery_binding"
  test "$(docker inspect --format '{{range .Mounts}}{{if eq .Destination "/var/lib/strataai/activity-keys"}}{{.Name}}{{end}}{{end}}' "$("${recovery_compose[@]}" ps -q api)")" = "$target_key_volume"
  for host in api worker web; do
    container=$("${recovery_compose[@]}" ps -q "$host")
    test "$(docker inspect --format '{{.Image}}' "$container")" = "${image_ids[$host]}"
    test "$(docker inspect --format '{{.State.Health.Status}}' "$container")" = healthy
  done
  request GET /api/runtime 200
  jq -e --arg revision "$revision" --arg version "$version" '.mode=="production" and .revision==$revision and .version==$version' "$scratch/response" >/dev/null
  "${recovery_compose[@]}" exec -T web wget -q -T 15 -O - http://worker:8081/runtime > "$scratch/recovery-worker-runtime.json"
  jq -e --arg revision "$revision" --arg version "$version" '.mode=="production" and .revision==$revision and .version==$version' "$scratch/recovery-worker-runtime.json" >/dev/null
  "${recovery_compose[@]}" exec -T web wget -q -T 15 -O - http://worker:8081/readyz > "$scratch/recovery-worker-ready.json"
  jq -e '.status=="ready" and .mode=="production"' "$scratch/recovery-worker-ready.json" >/dev/null
  check_saved_task
  check_recovery_activity_cursor
  # The saved session is checked before creating a separate new session.
  rm -f "$scratch/cookies"
  request POST /auth/login 200 "$credentials"
  grep -Eiq '^Set-Cookie: strataai_session=.*; secure' "$scratch/headers"
  check_saved_task
  check_recovery_activity_cursor
  echo 'HTTPS activity cursor recovery passed: source-issued encrypted continuation, exact restored page and Card rebinding refusal under saved and fresh sessions.'
  NODE_TLS_REJECT_UNAUTHORIZED=1 NODE_USE_ENV_PROXY=0 NODE_EXTRA_CA_CERTS="$(host_path "$scratch/server.crt")" \
    node --dns-result-order=ipv4first "$(host_path "$verification_dir/check-https-live-board.mjs")" \
    "$origin" "$(host_path "$scratch/cookies")" "$organization" "$board" "$card" "$user" > "$scratch/recovery-live-result.log"
  grep -Fxq 'HTTPS WSS Board passed: Worker-delivered edit, persisted cursor recovery, anonymous/origin refusal, content-free frames and logout withdrawal.' "$scratch/recovery-live-result.log"
  after_fingerprint=$(recovery_source_fingerprint "$source_database")
  test "$after_fingerprint" = "$source_fingerprint"
  "${recovery_compose[@]}" down --volumes --remove-orphans >/dev/null
  # Target owns its recovered keys; the source volume is independent and intact.
  origin=$source_origin
  export STRATAAI_HTTPS_PORT="$source_port" STRATAAI_PUBLIC_ORIGIN="$origin" STRATAAI_REALTIME_PUBLIC_ORIGIN="$origin"
  export STRATAAI_API_DB_PASSWORD="$source_api_password" STRATAAI_WORKER_DB_PASSWORD="$source_worker_password"
  export STRATAAI_WORKER_ORGANIZATION_IDS="$source_scope"
  cp "$scratch/source-cookies" "$scratch/cookies"
  "${compose[@]}" up -d --no-deps --no-recreate --no-build --pull never --wait --wait-timeout 120 api worker >/dev/null
  check_saved_task
  echo 'HTTPS database recovery passed: distinct restored cluster and independently recovered encrypted key ring, saved session and fresh Secure sign-in, exact task graph, target Worker/WSS edit and logout, unchanged source and original images.'
}
