#!/usr/bin/env bash
set -euo pipefail
admin() { docker compose -f compose.release.yml exec -T postgres sh -c 'psql -X -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<< "$1" >/dev/null; }
restore() { admin "ALTER ROLE strataai_api_runtime NOBYPASSRLS; ALTER ROLE strataai_worker_runtime NOBYPASSRLS; GRANT EXECUTE ON FUNCTION public.runtime_database_role_is_safe() TO strataai_api_runtime; INSERT INTO schema_migrations(version) VALUES ('009_runtime_role_guard'),('010_work_command_replays'),('011_work_events'),('012_identity_events'),('013_identity_profile_replays'),('014_identity_retry_retention'),('015_identity_revocation_replays'),('016_invitation_discovery'),('017_identity_login_replays'),('018_identity_registration_replays'),('019_identity_recovery_request_replays'),('020_identity_token_consumption_replays'),('021_organization_access_integrity'),('022_invitation_creation_replays'),('023_invitation_mail_intents'),('024_invitation_history'),('025_board_invitation_targets'),('026_board_invitation_mail'),('027_routing_isolation'),('028_board_labels'),('029_label_routing'),('030_card_members'),('031_card_assignment_notifications'),('032_notification_inbox'),('033_watch_subscriptions'),('034_watch_events'),('035_watch_activity_notifications'),('036_card_dates'),('037_card_reminders'),('038_card_reminder_delivery'),('039_board_date_policy'),('040_checklists'),('041_attachments') ON CONFLICT DO NOTHING;"; }
trap restore EXIT
status() { curl --silent --show-error --output /dev/null --write-out '%{http_code}' "$1"; }
test "$(status http://127.0.0.1:8080/readyz)" = 200
test "$(status http://127.0.0.1:8081/readyz)" = 200
admin 'ALTER ROLE strataai_api_runtime BYPASSRLS; ALTER ROLE strataai_worker_runtime BYPASSRLS;'
test "$(status http://127.0.0.1:8080/readyz)" = 503
test "$(status http://127.0.0.1:8081/readyz)" = 503
test "$(status http://127.0.0.1:8080/api/health)" = 200
body=$(mktemp)
trap 'rm -f "$body"; restore' EXIT
code=$(curl --silent --show-error --output "$body" --write-out '%{http_code}' -H 'Content-Type: application/json' -H 'X-StrataAI-Request: 1' -d '{"email":"guard-check@example.test","password":"not-a-real-password"}' http://127.0.0.1:8080/auth/login)
test "$code" = 503
jq -e '.code == "runtime_database_role_unsafe" and .status == 503' "$body" >/dev/null
code=$(curl --silent --show-error --output "$body" --write-out '%{http_code}' http://127.0.0.1:8080/boards/11111111-1111-1111-1111-111111111111/sync)
test "$code" = 503
jq -e '.code == "runtime_database_role_unsafe"' "$body" >/dev/null
restore
test "$(status http://127.0.0.1:8080/readyz)" = 200
test "$(status http://127.0.0.1:8081/readyz)" = 200
admin 'REVOKE EXECUTE ON FUNCTION public.runtime_database_role_is_safe() FROM strataai_api_runtime;'
test "$(status http://127.0.0.1:8080/readyz)" = 503
restore
test "$(status http://127.0.0.1:8080/readyz)" = 200
for version in 009_runtime_role_guard 010_work_command_replays 011_work_events 012_identity_events 013_identity_profile_replays 014_identity_retry_retention 015_identity_revocation_replays 016_invitation_discovery 017_identity_login_replays 018_identity_registration_replays 019_identity_recovery_request_replays 020_identity_token_consumption_replays 021_organization_access_integrity 022_invitation_creation_replays 023_invitation_mail_intents 024_invitation_history 025_board_invitation_targets 026_board_invitation_mail 027_routing_isolation 028_board_labels 029_label_routing 030_card_members 031_card_assignment_notifications 032_notification_inbox 033_watch_subscriptions 034_watch_events 035_watch_activity_notifications 036_card_dates 037_card_reminders 038_card_reminder_delivery 039_board_date_policy 040_checklists 041_attachments; do
admin "DELETE FROM schema_migrations WHERE version='$version';"
test "$(status http://127.0.0.1:8080/readyz)" = 503
test "$(status http://127.0.0.1:8081/readyz)" = 503
code=$(curl --silent --show-error --output "$body" --write-out '%{http_code}' -H 'Content-Type: application/json' -H 'X-StrataAI-Request: 1' -d '{"email":"schema-check@example.test","password":"not-a-real-password"}' http://127.0.0.1:8080/auth/login)
test "$code" = 503
jq -e '.code == "runtime_database_schema_incompatible"' "$body" >/dev/null
code=$(curl --silent --show-error --output "$body" --write-out '%{http_code}' http://127.0.0.1:8080/boards/11111111-1111-1111-1111-111111111111/sync)
test "$code" = 503
jq -e '.code == "runtime_database_schema_incompatible"' "$body" >/dev/null
restore
test "$(status http://127.0.0.1:8080/readyz)" = 200
done

echo 'Exact images reject unsafe roles and recover after permissions are restored.'
