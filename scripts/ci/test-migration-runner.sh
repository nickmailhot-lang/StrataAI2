#!/usr/bin/env bash
set -euo pipefail
scratch=$(mktemp -d)
database=strataai_migration_runner_ci
cleanup() { rm -rf "$scratch"; dropdb --if-exists "$database" >/dev/null; }
trap cleanup EXIT
createdb "$database"
mkdir "$scratch/migrations"
cp db/migrations/00[1-8]_*.sql "$scratch/migrations/"
run() { scripts/migration-stream.sh "$scratch/migrations" | PGDATABASE="$database" psql -X -v ON_ERROR_STOP=1 >/dev/null; }
query() { PGDATABASE="$database" psql -X -At -v ON_ERROR_STOP=1 -c "$1"; }
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 8
cp db/migrations/009_runtime_role_guard.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 9
cp db/migrations/010_work_command_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 10
cp db/migrations/011_work_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 11
cp db/migrations/012_identity_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 12
cp db/migrations/013_identity_profile_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 13
cp db/migrations/014_identity_retry_retention.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 14
cp db/migrations/015_identity_revocation_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 15
cp db/migrations/016_invitation_discovery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 16
cp db/migrations/017_identity_login_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 17
cp db/migrations/018_identity_registration_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 18
cp db/migrations/019_identity_recovery_request_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 19
cp db/migrations/020_identity_token_consumption_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 20
# Upgrade populated membership data; a preexisting missing route must refuse
# the upgrade and roll back every constraint rather than inventing membership.
query "INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES ('02100000-0000-0000-0000-000000000010','migration-owner@example.test','MIGRATION-OWNER@EXAMPLE.TEST','Migration owner','ACTIVE','unusable-ci-fixture',now(),now());
 INSERT INTO organizations(id,name,created_at,updated_at) VALUES ('02100000-0000-0000-0000-000000000011','Migration fixture',now(),now());
 INSERT INTO organization_members(id,user_id,tenant_id,role,status) VALUES ('02100000-0000-0000-0000-000000000012','02100000-0000-0000-0000-000000000010','02100000-0000-0000-0000-000000000011','OWNER','ACTIVE');
 DELETE FROM user_organization_access WHERE user_id='02100000-0000-0000-0000-000000000010';" >/dev/null
cp db/migrations/021_organization_access_integrity.sql "$scratch/migrations/"
if run; then echo 'Divergent membership routing accepted during upgrade'; exit 1; fi
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='021_organization_access_integrity'")" = 0
test "$(query "SELECT count(*) FROM pg_constraint WHERE conname IN ('uq_organization_members_access_state','uq_user_organization_access_state','fk_organization_route_membership','fk_organization_membership_route')")" = 0
test "$(query "SELECT count(*) FROM organization_members WHERE id='02100000-0000-0000-0000-000000000012'")" = 1
# Explicitly repair only the disposable fixture using its existing canonical row.
query "UPDATE organization_members SET updated_at=clock_timestamp() WHERE id='02100000-0000-0000-0000-000000000012';" >/dev/null
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 21
test "$(query "SELECT count(*) FROM organization_members m JOIN user_organization_access r USING(user_id,tenant_id,role,status) WHERE m.id='02100000-0000-0000-0000-000000000012'")" = 1
cp db/migrations/022_invitation_creation_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 22
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='invitation_creation_replays'::regclass")" = t
cp db/migrations/023_invitation_mail_intents.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 23
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='invitation_mail_intents'::regclass")" = t
cp db/migrations/024_invitation_history.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 24
test "$(query "SELECT to_regclass('public.ix_invitations_tenant_cursor') IS NOT NULL")" = t
cp db/migrations/025_board_invitation_targets.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 25
test "$(query "SELECT to_regclass('public.ix_invitations_board_pending') IS NOT NULL")" = t
query "INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('02500000-0000-0000-0000-000000000001','02100000-0000-0000-0000-000000000011','Board target',now(),now());
 INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('02500000-0000-0000-0000-000000000002','Foreign target',now(),now());
 INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('02500000-0000-0000-0000-000000000003','02500000-0000-0000-0000-000000000002','Foreign Board',now(),now());
 INSERT INTO invitations(id,tenant_id,invited_email,email_normalized,token_hash,target_surface,target_role,
 created_by_user_id,created_at,expires_at,target_board_id,target_board_role) VALUES
 ('02500000-0000-0000-0000-000000000004','02100000-0000-0000-0000-000000000011','board@example.test','BOARD@EXAMPLE.TEST',
 repeat('b',64),'INTERNAL','MEMBER','02100000-0000-0000-0000-000000000010',now(),now()+interval '1 day',
 '02500000-0000-0000-0000-000000000001','ADMIN');" >/dev/null
test "$(query "SELECT target_board_id='02500000-0000-0000-0000-000000000001' AND target_board_role='ADMIN'
 FROM invitation_routes WHERE invitation_id='02500000-0000-0000-0000-000000000004'")" = t
for change in "target_board_id='02500000-0000-0000-0000-000000000003'" "target_board_role=NULL" \
 "target_board_id=NULL" "target_board_role='OWNER'" "target_surface='PORTAL'" "target_role='OWNER'"; do
 if query "UPDATE invitations SET $change WHERE id='02500000-0000-0000-0000-000000000004';" >/dev/null; then
   echo 'Invalid Board invitation target was admitted'; exit 1
 fi
done
query "UPDATE invitations SET accepted_at=now(),accepted_by_user_id='02100000-0000-0000-0000-000000000010'
 WHERE id='02500000-0000-0000-0000-000000000004';" >/dev/null
if query "UPDATE invitations SET target_board_role='MEMBER' WHERE id='02500000-0000-0000-0000-000000000004';" >/dev/null; then
 echo 'Accepted Board invitation role was rewritten'; exit 1
fi
cp db/migrations/026_board_invitation_mail.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 26
cp db/migrations/027_routing_isolation.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 27
test "$(query "SELECT count(*) FROM pg_class WHERE relname IN ('board_routes','list_routes','card_routes','user_organization_access','invitation_routes') AND relrowsecurity AND relforcerowsecurity")" = 5
cp db/migrations/028_board_labels.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 28
test "$(query "SELECT count(*) FROM pg_class WHERE relname IN ('board_labels','card_labels') AND relrowsecurity AND relforcerowsecurity")" = 2
cp db/migrations/029_label_routing.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 29
cp db/migrations/030_card_members.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 30
test "$(query "SELECT count(*) FROM pg_class WHERE relname='card_members' AND relrowsecurity AND relforcerowsecurity")" = 1
cp db/migrations/031_card_assignment_notifications.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 31
test "$(query "SELECT count(*) FROM pg_class WHERE relname='card_assignment_notifications' AND relrowsecurity AND relforcerowsecurity")" = 1
cp db/migrations/032_notification_inbox.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 32
test "$(query "SELECT count(*) FROM pg_indexes WHERE indexname='ix_card_assignment_notifications_inbox'")" = 1
cp db/migrations/033_watch_subscriptions.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 33
test "$(query "SELECT count(*) FROM pg_class WHERE relname='watch_subscriptions' AND relrowsecurity AND relforcerowsecurity")" = 1
cp db/migrations/034_watch_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 34
test "$(query "SELECT count(*) FROM pg_constraint WHERE conname='work_events_watch_scope_fk'")" = 1
cp db/migrations/035_watch_activity_notifications.sql "$scratch/migrations/"
cp db/migrations/036_card_dates.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 36
cp db/migrations/037_card_reminders.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 37
cp db/migrations/038_card_reminder_delivery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 38
cp db/migrations/039_board_date_policy.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 39
# Reproduce the old applied-column/unrecorded-ledger state with a real policy.
query "UPDATE boards SET date_timezone_override='Pacific/Honolulu' WHERE id='02500000-0000-0000-0000-000000000001';
 DELETE FROM schema_migrations WHERE version='039_board_date_policy';" >/dev/null
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 39
test "$(query "SELECT date_timezone_override='Pacific/Honolulu' FROM boards WHERE id='02500000-0000-0000-0000-000000000001'")" = t
cp db/migrations/040_checklists.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 40
test "$(query "SELECT count(*) FROM pg_class WHERE relname IN ('checklists','checklist_items') AND relrowsecurity AND relforcerowsecurity")" = 2
cp db/migrations/041_attachments.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 41
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='attachments'::regclass")" = t
# Preserve pre-integrity metadata without inventing a digest or releasing bytes.
query "INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('04200000-0000-0000-0000-000000000001','02100000-0000-0000-0000-000000000011','02500000-0000-0000-0000-000000000001','Legacy files','500000000000000000000000000000',now(),now());
 INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('04200000-0000-0000-0000-000000000002','02100000-0000-0000-0000-000000000011','02500000-0000-0000-0000-000000000001','04200000-0000-0000-0000-000000000001','Legacy file metadata','500000000000000000000000000000',now(),now());
 INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,mime_type,size_bytes,storage_key,scan_status,created_at,updated_at)
 VALUES ('04200000-0000-0000-0000-000000000003','02100000-0000-0000-0000-000000000011','04200000-0000-0000-0000-000000000002','02100000-0000-0000-0000-000000000010','FILE','Legacy pending','image/png',128,'ci/legacy-file','PENDING',now(),now());" >/dev/null
cp db/migrations/042_attachment_integrity.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 42
test "$(query "SELECT sha256 IS NULL AND scan_status='PENDING' AND version=1 FROM attachments WHERE id='04200000-0000-0000-0000-000000000003'")" = t
if query "UPDATE attachments SET scan_status='CLEAN',scanned_at=now(),updated_at=now(),version=version+1 WHERE id='04200000-0000-0000-0000-000000000003';" >/dev/null; then
 echo 'Legacy file published without measured digest'; exit 1
fi
query "UPDATE attachments SET deleted_at=now(),updated_at=now(),version=version+1 WHERE id='04200000-0000-0000-0000-000000000003';" >/dev/null
test "$(query "SELECT sha256 IS NULL AND deleted_at IS NOT NULL AND scan_status='PENDING' AND version=2 FROM attachments WHERE id='04200000-0000-0000-0000-000000000003'")" = t
cp db/migrations/043_attachment_upload_intents.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 43
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='attachment_upload_intents'::regclass")" = t
cp db/migrations/044_attachment_scan_delivery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 44
test "$(query "SELECT count(*) FROM pg_proc WHERE proname IN ('load_attachment_scan','finish_attachment_scan') AND prosecdef")" = 2
cp db/migrations/045_attachment_preview_intents.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 45
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='attachment_previews'::regclass")" = t
test "$(query "SELECT count(*) FROM pg_proc WHERE proname IN ('load_attachment_preview','declare_attachment_preview') AND prosecdef")" = 2
# Reproduce a granted pre-upgrade Worker source loader. Renaming its function
# must revoke that grant before the wrapper gains private replay behavior.
query "DO \$\$ BEGIN IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_worker_runtime') THEN CREATE ROLE strataai_worker_runtime; END IF; END \$\$;
 GRANT EXECUTE ON FUNCTION load_attachment_preview(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint) TO strataai_worker_runtime;" >/dev/null
cp db/migrations/046_attachment_preview_publication.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 46
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='attachment_preview_publications'::regclass")" = t
test "$(query "SELECT count(*) FROM pg_proc WHERE proname IN ('load_attachment_preview','finish_attachment_preview') AND prosecdef")" = 2
test "$(query "SELECT has_function_privilege('strataai_worker_runtime','load_attachment_preview_source(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint)','EXECUTE')")" = f
cp db/migrations/047_attachment_preview_activation.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 47
test "$(query "SELECT prosecdef FROM pg_proc WHERE oid='finish_attachment_scan(uuid,uuid,uuid,uuid,uuid,uuid,uuid,bigint,bigint,text,text,boolean)'::regprocedure")" = t
test "$(query "SELECT NOT prosecdef FROM pg_proc WHERE oid='claim_background_job(uuid)'::regprocedure")" = t
# The clean installation above exercised absent runtime roles. This forward
# upgrade must also exercise the migration's existing-API-role grant branch.
query "DO \$\$ BEGIN IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='strataai_api_runtime') THEN CREATE ROLE strataai_api_runtime; END IF; END \$\$;" >/dev/null
cp db/migrations/048_attachment_preview_reads.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 48
test "$(query "SELECT has_table_privilege('strataai_api_runtime','attachment_previews','SELECT') AND has_table_privilege('strataai_api_runtime','attachment_preview_publications','SELECT')")" = t
test "$(query "SELECT has_table_privilege('strataai_api_runtime','attachment_previews','INSERT') OR has_table_privilege('strataai_api_runtime','attachment_preview_publications','UPDATE')")" = f
cp db/migrations/049_attachment_preview_backfill.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 49
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='attachment_preview_sweeps'::regclass")" = t
test "$(query "SELECT prosecdef FROM pg_proc WHERE oid='enqueue_attachment_preview_backfill(uuid,integer)'::regprocedure")" = t
test "$(query "SELECT has_function_privilege('strataai_worker_runtime','enqueue_attachment_preview_backfill(uuid,integer)','EXECUTE')")" = t
test "$(query "SELECT has_function_privilege('strataai_api_runtime','enqueue_attachment_preview_backfill(uuid,integer)','EXECUTE')")" = f
cp db/migrations/050_attachment_scan_recovery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 50
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='attachment_scan_sweeps'::regclass")" = t
test "$(query "SELECT has_function_privilege('strataai_worker_runtime','recover_attachment_scan_page(uuid,integer)','EXECUTE') AND NOT has_function_privilege('strataai_api_runtime','recover_attachment_scan_page(uuid,integer)','EXECUTE')")" = t
cp db/migrations/051_attachment_lifecycle.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 51
test "$(query "SELECT lifecycle_state='DELETED' AND archived_at IS NULL AND deleted_by IS NULL AND sha256 IS NULL AND version=2 FROM attachments WHERE id='04200000-0000-0000-0000-000000000003'")" = t
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='attachments'::regclass")" = t
test "$(query "SELECT count(*)=3 FROM pg_indexes WHERE tablename='attachments' AND indexname IN ('ix_attachments_card_cursor','ix_attachments_archive_cursor','ix_attachments_preview_sweep') AND indexdef LIKE '%lifecycle_state%'")" = t
if query "UPDATE attachments SET lifecycle_state='ACTIVE',deleted_at=NULL,version=version+1 WHERE id='04200000-0000-0000-0000-000000000003';" >/dev/null; then
 echo 'Legacy deleted attachment was restored'; exit 1
fi
cp db/migrations/052_attachment_lifecycle_scan.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 52
test "$(query "SELECT lifecycle_revision=0 AND lifecycle_state='DELETED' AND version=2 FROM attachments WHERE id='04200000-0000-0000-0000-000000000003'")" = t
query 'DO $$ DECLARE target uuid; prior bigint; BEGIN
 INSERT INTO attachments(id,tenant_id,card_id,uploader_id,kind,display_name,url,scan_status,created_at,updated_at)
 SELECT gen_random_uuid(),tenant_id,card_id,uploader_id,'\''URL'\'','\''Count fixture'\'','\''https://example.test/count'\'','\''NOT_APPLICABLE'\'',statement_timestamp(),statement_timestamp()
 FROM attachments LIMIT 1 RETURNING id,version INTO target,prior;
 IF target IS NULL THEN RAISE EXCEPTION '\''Lifecycle count fixture missing'\''; END IF;
 UPDATE attachments SET lifecycle_state='\''ARCHIVED'\'',archived_at=GREATEST(updated_at,statement_timestamp()),updated_at=GREATEST(updated_at,statement_timestamp()),version=version+1 WHERE id=target;
 UPDATE attachments SET lifecycle_state='\''ACTIVE'\'',version=version+1 WHERE id=target;
 IF NOT EXISTS(SELECT 1 FROM attachments WHERE id=target AND lifecycle_revision=2 AND version=prior+2) THEN RAISE EXCEPTION '\''Lifecycle count was not maintained'\''; END IF;
 BEGIN
  UPDATE attachments SET lifecycle_revision=lifecycle_revision+1 WHERE id=target;
  RAISE EXCEPTION '\''Lifecycle count was caller mutable'\'';
 EXCEPTION WHEN check_violation THEN NULL; END;
END $$;'
cp db/migrations/053_card_attachment_covers.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 53
test "$(query 'SELECT bool_and(cover_attachment_id IS NULL) FROM cards')" = t
if query "UPDATE cards SET cover_attachment_id=gen_random_uuid(),version=version+1,updated_at=GREATEST(updated_at,statement_timestamp());" >/dev/null; then
 echo 'Unpublished cover source was admitted'; exit 1
fi
test "$(query 'SELECT bool_and(cover_attachment_id IS NULL) FROM cards')" = t
cp db/migrations/054_card_cover_deferred_guard.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 54
test "$(query "SELECT prosecdef AND proconfig @> ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='public.enforce_selected_cover_source()'::regprocedure")" = t
cp db/migrations/055_card_comments.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 55
cp db/migrations/056_mention_handles.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 56
test "$(query 'SELECT count(*) FROM user_mention_handles')" = "$(query 'SELECT count(*) FROM users')"
cp db/migrations/057_identity_handle_claim_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 57
cp db/migrations/058_comment_mention_snapshots.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 58
cp db/migrations/059_comment_mention_notifications.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 59
cp db/migrations/060_mass_mention_recipient_history.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 60
cp db/migrations/061_mass_mention_quota.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 61
# Existing events have no saved event-time caption. A populated upgrade must
# retain a stable-ID fallback rather than assign a mutable present-day name.
query "INSERT INTO work_event_streams(tenant_id,board_id,last_sequence)
 VALUES('02100000-0000-0000-0000-000000000011','02500000-0000-0000-0000-000000000001',1);
 INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('02100000-0000-0000-0000-000000000011','06200000-0000-0000-0000-000000000091',
 '02500000-0000-0000-0000-000000000001',1,'02100000-0000-0000-0000-000000000010',
 'BOARD_UPDATED','Board','02500000-0000-0000-0000-000000000001',1,'activity-upgrade',now());
 UPDATE users SET display_name='After old event' WHERE id='02100000-0000-0000-0000-000000000010';" >/dev/null
cp db/migrations/062_activity_attribution.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 62
test "$(query "SELECT activity_actor_label='Member 02100000-0000-0000-0000-000000000010'
 FROM work_events WHERE event_id='06200000-0000-0000-0000-000000000091'")" = t
query "INSERT INTO users(id,email,email_normalized,display_name,status,email_verified,password_hash,created_at,updated_at)
 VALUES('06600000-0000-0000-0000-000000000010','notification-upgrade@example.test','NOTIFICATION-UPGRADE@EXAMPLE.TEST','Notification recipient','ACTIVE',true,'fixture',now(),now());
 INSERT INTO organization_members(id,user_id,tenant_id,role,status)
 VALUES(gen_random_uuid(),'06600000-0000-0000-0000-000000000010','02100000-0000-0000-0000-000000000011','MEMBER','ACTIVE');
 INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('02100000-0000-0000-0000-000000000011','06200000-0000-0000-0000-000000000092',
 '02500000-0000-0000-0000-000000000001',2,'02100000-0000-0000-0000-000000000010',
 'BOARD_UPDATED','Board','02500000-0000-0000-0000-000000000001',1,'activity-upgrade',now());
 UPDATE users SET display_name='Later still' WHERE id='02100000-0000-0000-0000-000000000010';" >/dev/null
test "$(query "SELECT activity_actor_label='After old event' FROM work_events WHERE event_id='06200000-0000-0000-0000-000000000092'")" = t
cp db/migrations/063_activity_generated_source_guard.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 63
cp db/migrations/064_private_activity_identity.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 64
cp db/migrations/065_activity_history_indexes.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 65
# A populated notification upgrade retains its complete historical envelope.
query "INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('02100000-0000-0000-0000-000000000011','06600000-0000-0000-0000-000000000091',
 '02500000-0000-0000-0000-000000000001',3,'02100000-0000-0000-0000-000000000010',
 'CARD_UPDATED','Card','04200000-0000-0000-0000-000000000002',1,'notification-upgrade',now());
 INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
 SELECT tenant_id,'06600000-0000-0000-0000-000000000092',board_id,entity_id,event_id,'06600000-0000-0000-0000-000000000010',actor_id,entity_version,created_at,'CARD_UPDATED'
 FROM work_events WHERE event_id='06600000-0000-0000-0000-000000000091';
 INSERT INTO card_reminders(tenant_id,id,user_id,card_id,interval_code,enabled,status,generation,created_at,updated_at,version)
 VALUES('02100000-0000-0000-0000-000000000011','06600000-0000-0000-0000-000000000095',
 '02100000-0000-0000-0000-000000000010','04200000-0000-0000-0000-000000000002','AT_DUE',false,'CANCELLED',1,now(),now(),1);
 INSERT INTO work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,entity_version,correlation_id,created_at)
 VALUES('02100000-0000-0000-0000-000000000011','06600000-0000-0000-0000-000000000093',
 '02500000-0000-0000-0000-000000000001',4,'02100000-0000-0000-0000-000000000010',
 'REMINDER_FIRED','Reminder','06600000-0000-0000-0000-000000000095',1,'notification-upgrade',now());
 INSERT INTO card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,card_version,created_at,notification_type)
 SELECT tenant_id,'06600000-0000-0000-0000-000000000094',board_id,'04200000-0000-0000-0000-000000000002',
 event_id,actor_id,actor_id,1,created_at,'REMINDER_FIRED' FROM work_events WHERE event_id='06600000-0000-0000-0000-000000000093';" >/dev/null
notification_before=$(query "SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n")
cp db/migrations/066_notification_historical_card.sql "$scratch/migrations/"
# The old current-Board FK admits a different Card on the same Board. A
# populated inconsistent source must refuse upgrade and roll back every DDL
# change, rather than silently rewriting history or leaving relaxed integrity.
query "INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('06600000-0000-0000-0000-000000000099','02100000-0000-0000-0000-000000000011',
 '02500000-0000-0000-0000-000000000001','04200000-0000-0000-0000-000000000001','Unrelated upgrade subject',
 '600000000000000000000000000000',now(),now());
 UPDATE card_assignment_notifications SET card_id='06600000-0000-0000-0000-000000000099'
 WHERE id='06600000-0000-0000-0000-000000000092';" >/dev/null
if run; then echo 'Inconsistent historical notification source accepted during upgrade'; exit 1; fi
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='066_notification_historical_card'")" = 0
test "$(query "SELECT count(*) FROM pg_constraint WHERE conrelid='card_assignment_notifications'::regclass AND conname='notification_stable_card_fk'")" = 0
test "$(query "SELECT count(*) FROM pg_constraint WHERE conrelid='card_assignment_notifications'::regclass AND confrelid='cards'::regclass AND contype='f' AND pg_get_constraintdef(oid) LIKE 'FOREIGN KEY (card_id, board_id, tenant_id)%'")" = 1
query "UPDATE card_assignment_notifications SET card_id='04200000-0000-0000-0000-000000000002'
 WHERE id='06600000-0000-0000-0000-000000000092';" >/dev/null
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 66
test "$(query "SELECT jsonb_agg(to_jsonb(n) ORDER BY id) FROM card_assignment_notifications n")" = "$notification_before"
cp db/migrations/067_card_copy_notifications.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 67
cp db/migrations/068_notification_private_journal.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 68
test "$(query 'SELECT (SELECT count(*) FROM notification_events)=(SELECT count(*)+count(read_at) FROM card_assignment_notifications)')" = t
test "$(query "SELECT count(*) FROM notification_events e JOIN card_assignment_notifications n ON n.tenant_id=e.tenant_id AND n.id=e.notification_id WHERE e.recipient_id<>n.recipient_id OR e.board_id<>n.board_id OR e.metadata<>'{}'::jsonb OR (e.event_type='NOTIFICATION_CREATED' AND (e.actor_id<>n.actor_id OR e.created_at<>n.created_at OR e.version<>1)) OR (e.event_type='NOTIFICATION_READ' AND (e.actor_id<>n.recipient_id OR e.created_at IS DISTINCT FROM n.read_at OR e.version<>2))")" = 0
test "$(query 'SELECT count(*) FROM notification_event_streams s WHERE s.last_sequence<>(SELECT count(*) FROM notification_events e WHERE e.tenant_id=s.tenant_id AND e.recipient_id=s.recipient_id)')" = 0
journal_before=$(query "SELECT md5(jsonb_build_object('events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY tenant_id,recipient_id,sequence) FROM notification_events e),'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY tenant_id,recipient_id) FROM notification_event_streams s))::text)")
query "BEGIN; UPDATE card_assignment_notifications SET read_at=created_at WHERE read_at IS NULL; ROLLBACK;" >/dev/null
test "$(query "SELECT md5(jsonb_build_object('events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY tenant_id,recipient_id,sequence) FROM notification_events e),'streams',(SELECT jsonb_agg(to_jsonb(s) ORDER BY tenant_id,recipient_id) FROM notification_event_streams s))::text)")" = "$journal_before"
query "UPDATE card_assignment_notifications SET read_at=created_at WHERE read_at IS NULL;" >/dev/null
test "$(query "SELECT count(*) FROM notification_events WHERE event_type='NOTIFICATION_READ'")" = "$(query 'SELECT count(*) FROM card_assignment_notifications WHERE read_at IS NOT NULL')"
journal_after=$(query "SELECT md5(string_agg(to_jsonb(e)::text,'' ORDER BY tenant_id,recipient_id,sequence)) FROM notification_events e")
query "UPDATE card_assignment_notifications SET read_at=COALESCE(read_at,created_at);" >/dev/null
test "$(query "SELECT md5(string_agg(to_jsonb(e)::text,'' ORDER BY tenant_id,recipient_id,sequence)) FROM notification_events e")" = "$journal_after"
cp db/migrations/069_work_deletion_actor.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 69
test "$(query 'SELECT (SELECT count(*) FROM boards WHERE deleted_by IS NOT NULL)+(SELECT count(*) FROM board_lists WHERE deleted_by IS NOT NULL)+(SELECT count(*) FROM cards WHERE deleted_by IS NOT NULL)')" = 0
query "INSERT INTO user_board_preferences(tenant_id,board_id,user_id,starred,updated_at) SELECT b.tenant_id,b.id,u.id,true,clock_timestamp() FROM boards b CROSS JOIN users u LIMIT 1 ON CONFLICT DO NOTHING;" >/dev/null
preferences_before="$(query 'SELECT md5(string_agg(row(tenant_id,board_id,user_id,starred,updated_at)::text,chr(10) ORDER BY board_id,user_id)) FROM user_board_preferences')"
cp db/migrations/070_board_star_revisions.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 70
test "$(query 'SELECT count(*) FROM user_board_preferences WHERE created_at IS NOT NULL OR version<>1')" = 0
test "$(query 'SELECT md5(string_agg(row(tenant_id,board_id,user_id,starred,updated_at)::text,chr(10) ORDER BY board_id,user_id)) FROM user_board_preferences')" = "$preferences_before"
cp db/migrations/071_board_star_private_journal.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 71
test "$(query 'SELECT count(*) FROM board_star_events')" = 0
test "$(query 'SELECT count(*) FROM user_board_preferences WHERE id IS NULL OR created_at IS NOT NULL OR version<>1')" = 0
test "$(query 'SELECT md5(string_agg(row(tenant_id,board_id,user_id,starred,updated_at)::text,chr(10) ORDER BY board_id,user_id)) FROM user_board_preferences')" = "$preferences_before"
cp db/migrations/072_board_background_images.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 72
test "$(query 'SELECT count(*) FROM board_background_images')" = 0

board_sources_before=$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY tenant_id,board_id,sequence),'')) FROM work_events e")
cp db/migrations/073_organization_board_journal.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 73
test "$(query "SELECT count(*) FROM organization_board_events")" = "$(query "SELECT count(*) FROM work_events WHERE entity_type='Board' AND entity_id=board_id AND event_type IN ('BOARD_CREATED','BOARD_UPDATED','BOARD_COPIED','BOARD_ARCHIVED','BOARD_RESTORED','BOARD_DELETED')")"
test "$(query 'SELECT count(*) FROM organization_board_event_streams s WHERE s.last_sequence<>(SELECT count(*) FROM organization_board_events e WHERE e.tenant_id=s.tenant_id)')" = 0
test "$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY tenant_id,board_id,sequence),'')) FROM work_events e")" = "$board_sources_before"

directory_members_before=$(query "SELECT md5(COALESCE(string_agg(to_jsonb(m)::text,chr(10) ORDER BY tenant_id,user_id),'')) FROM organization_members m")
cp db/migrations/074_organization_board_directory_epochs.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 74
test "$(query 'SELECT count(*) FROM organization_board_directory_epochs')" = "$(query 'SELECT count(*) FROM organization_members')"
test "$(query 'SELECT count(*) FROM organization_board_directory_epochs WHERE permission_revision<>1 OR generation IS NULL')" = 0
test "$(query "SELECT md5(COALESCE(string_agg(to_jsonb(m)::text,chr(10) ORDER BY tenant_id,user_id),'')) FROM organization_members m")" = "$directory_members_before"
test "$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY tenant_id,board_id,sequence),'')) FROM work_events e")" = "$board_sources_before"

reader_archive_before=$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY tenant_id,user_id),'')) FROM organization_board_directory_epochs e")
cp db/migrations/075_organization_board_reader_epochs.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 75
test "$(query 'SELECT count(*) FROM organization_board_directory_epochs WHERE reader_revision<>1 OR reader_updated_at<created_at')" = 0
test "$(query "SELECT md5(COALESCE(string_agg((to_jsonb(e)-'reader_revision'-'reader_updated_at')::text,chr(10) ORDER BY tenant_id,user_id),'')) FROM organization_board_directory_epochs e")" = "$reader_archive_before"
test "$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY tenant_id,board_id,sequence),'')) FROM work_events e")" = "$board_sources_before"

personal_identity_before=$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY user_id,sequence),'')) FROM identity_events e")
cp db/migrations/076_search_interaction_sources.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 76
test "$(query 'SELECT count(*) FROM search_interaction_events')" = 0
test "$(query 'SELECT count(*) FROM search_interaction_streams')" = 0
test "$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY user_id,sequence),'')) FROM identity_events e")" = "$personal_identity_before"
test "$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY tenant_id,board_id,sequence),'')) FROM work_events e")" = "$board_sources_before"

query "SELECT set_config('app.identity_subject',(SELECT id::text FROM users WHERE status='ACTIVE' ORDER BY id LIMIT 1),false); SELECT append_search_interaction('07700000-0000-0000-0000-000000000030',(SELECT id FROM users WHERE status='ACTIVE' ORDER BY id LIMIT 1),'SEARCH_EXECUTED',null,null,clock_timestamp());" >/dev/null
search_sources_before=$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY actor_id,sequence),'')) FROM search_interaction_events e")
cp db/migrations/077_board_filter_interaction_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 77
test "$(query 'SELECT count(*) FROM board_filter_interaction_replays')" = 0
test "$(query "SELECT md5(COALESCE(string_agg(to_jsonb(e)::text,chr(10) ORDER BY actor_id,sequence),'')) FROM search_interaction_events e")" = "$search_sources_before"

cp db/migrations/078_navigation_interaction_sources.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 78
test "$(query 'SELECT count(*) FROM navigation_interaction_events')" = 0

cp db/migrations/079_navigation_tenant_admission.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 79

cp db/migrations/080_navigation_interaction_replays.sql "$scratch/migrations/"
cp db/migrations/081_navigation_original_recovery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 81
test "$(query 'SELECT count(*) FROM navigation_interaction_replays')" = 0

cp db/migrations/082_organization_metadata_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 82
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='organization_metadata_replays'::regclass")" = t

cp db/migrations/083_organization_departure_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 83
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='organization_departure_replays'::regclass")" = t

cp db/migrations/084_organization_removal_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 84
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='organization_removal_replays'::regclass")" = t

cp db/migrations/085_interaction_actor_lock_order.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 85

cp db/migrations/086_organization_creation_replays.sql "$scratch/migrations/"
run
run
test "$(query "SELECT count(*) FROM schema_migrations")" = 86
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='organization_creation_replays'::regclass")" = t

cp db/migrations/087_organization_deletion_replays.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 87
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='organization_deletion_replays'::regclass")" = t

cp db/migrations/088_organization_deletion_progress.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 88
test "$(query "SELECT count(*) FROM pg_class WHERE oid IN ('organization_deletion_requests'::regclass,'organization_deletion_progress'::regclass) AND relrowsecurity AND relforcerowsecurity")" = 2

cp db/migrations/089_organization_deletion_terminal.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 89
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='organization_lifecycle_events'::regclass")" = t

cp db/migrations/090_organization_lifecycle_delivery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 90

cp db/migrations/091_organization_deletion_candidates.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 91

cp db/migrations/092_organization_deletion_pages.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 92
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='organization_deletion_steps'::regclass")" = t

cp db/migrations/093_organization_deletion_discovery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 93
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='discover_organization_deletion_scopes(uuid,integer)'::regprocedure")" = t

cp db/migrations/094_organization_metadata_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 94
test "$(query "SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE oid='organization_metadata_events'::regclass")" = t
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='journal_organization_metadata_event()'::regprocedure")" = t

cp db/migrations/095_organization_metadata_delivery.sql "$scratch/migrations/"
query "INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('09400000-0000-4000-8000-000000000010','metadata-upgrade@example.test','METADATA-UPGRADE@EXAMPLE.TEST','Metadata upgrade','ACTIVE','unused-fixture-hash',now(),now());
 INSERT INTO organizations(id,name,owner_user_id,status,version,created_at,updated_at)
 VALUES('09400000-0000-4000-8000-000000000011','Metadata upgrade','09400000-0000-4000-8000-000000000010','ACTIVE',1,now(),now());
 INSERT INTO organization_members(id,tenant_id,user_id,role,status)
 VALUES('09400000-0000-4000-8000-000000000012','09400000-0000-4000-8000-000000000011','09400000-0000-4000-8000-000000000010','OWNER','ACTIVE');
 INSERT INTO audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,safe_metadata)
 VALUES('09400000-0000-4000-8000-000000000013','09400000-0000-4000-8000-000000000011','09400000-0000-4000-8000-000000000010',
 'ORGANIZATION_CREATED','Organization','09400000-0000-4000-8000-000000000011','metadata-upgrade','{}');" >/dev/null
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 95
test "$(query "SELECT count(*)=1 FROM background_jobs WHERE tenant_id='09400000-0000-4000-8000-000000000011' AND job_type='ORGANIZATION_METADATA_EVENT_READY' AND state='PENDING' AND safe_metadata=jsonb_build_object('eventId','09400000-0000-4000-8000-000000000013'::uuid)")" = t
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='deliver_organization_metadata_event(uuid,uuid,uuid,uuid,uuid,uuid)'::regprocedure")" = t

cp db/migrations/096_organization_metadata_discovery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 96
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='discover_organization_metadata_scopes(uuid,integer)'::regprocedure")" = t
test "$(query "SELECT NOT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='claim_organization_metadata_job(uuid)'::regprocedure")" = t

cp db/migrations/097_organization_member_addition_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 97
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='journal_organization_member_addition()'::regprocedure")" = t

cp db/migrations/098_organization_member_removal_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 98
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='journal_organization_member_removal()'::regprocedure")" = t

cp db/migrations/099_organization_member_invitation_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 99
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='journal_organization_member_invitation()'::regprocedure")" = t
test "$(query 'SELECT count(*) FROM organization_invitation_creations')" = 0
test "$(query 'SELECT count(*) FROM invitations WHERE version<>1 OR updated_at IS DISTINCT FROM GREATEST(created_at,accepted_at,revoked_at)')" = 0
test "$(query "SELECT count(*) FROM organization_metadata_events WHERE event_type='ORGANIZATION_MEMBER_INVITED'")" = 0

cp db/migrations/100_organization_invitation_revocation_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 100
test "$(query 'SELECT count(*) FROM organization_invitation_revocations')" = 0
test "$(query "SELECT count(*) FROM organization_metadata_events WHERE event_type='INVITATION_REVOKED'")" = 0
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='journal_organization_invitation_revocation()'::regprocedure")" = t

cp db/migrations/101_organization_invitation_acceptance_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 101
test "$(query 'SELECT count(*) FROM organization_invitation_acceptances')" = 0
test "$(query "SELECT count(*) FROM organization_metadata_events WHERE event_type='INVITATION_ACCEPTED'")" = 0
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='journal_organization_invitation_acceptance()'::regprocedure")" = t

cp db/migrations/102_invitation_recipient_events.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 102
test "$(query 'SELECT count(*) FROM invitation_recipient_proofs')" = 0
test "$(query 'SELECT count(*) FROM invitation_recipient_events')" = 0
test "$(query 'SELECT count(*) FROM invitation_recipient_streams')" = 0

cp db/migrations/103_invitation_recipient_unpublished_cleanup.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 103
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='protect_invitation_recipient_proof()'::regprocedure")" = t

cp db/migrations/104_invitation_recipient_retained_board_admin.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 104
test "$(query "SELECT prosecdef AND proconfig=ARRAY['search_path=pg_catalog, public'] FROM pg_proc WHERE oid='journal_invitation_recipient_event()'::regprocedure")" = t

cp db/migrations/105_invitation_recipient_authority.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 105
test "$(query 'SELECT count(*) FROM invitation_recipient_authority_pages')" = 0
test "$(query 'SELECT count(*) FROM invitation_recipient_authority_revisions')" = 0

cp db/migrations/106_invitation_recipient_authority_discovery.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 106
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='106_invitation_recipient_authority_discovery'")" = 1

cp db/migrations/107_invitation_recipient_board_authority.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 107
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='107_invitation_recipient_board_authority'")" = 1
test "$(query 'SELECT count(*) FROM invitation_recipient_authority_sources')" = 0

cp db/migrations/108_invitation_recipient_organization_lifecycle.sql "$scratch/migrations/"
run
run
cp db/migrations/109_invitation_issuer_account_authority.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 109
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='108_invitation_recipient_organization_lifecycle'")" = 1
test "$(query 'SELECT count(*) FROM invitation_recipient_organization_lifecycle_sources')" = 0
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='109_invitation_issuer_account_authority'")" = 1
test "$(query 'SELECT count(*) FROM invitation_issuer_authority_sources')" = 0
test "$(query 'SELECT count(*) FROM invitation_issuer_authority_jobs')" = 0

cp db/migrations/110_invitation_issuer_authority_exhaustion.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 110
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='110_invitation_issuer_authority_exhaustion'")" = 1
test "$(query 'SELECT count(*) FROM invitation_issuer_authority_jobs')" = 0
cp db/migrations/111_notification_batch_source_guard.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 111
query "$(cat scripts/ci/notification-source-guard-fixture.sql)" >/dev/null
cp db/migrations/112_work_archive_history.sql "$scratch/migrations/"
run
run
test "$(query 'SELECT count(*) FROM schema_migrations')" = 112
query "$(cat scripts/ci/work-archive-history-fixture.sql)" >/dev/null
cat > "$scratch/migrations/113_serialization_fixture.sql" <<'SQL'
BEGIN;
SELECT pg_sleep(1);
CREATE TABLE migration_serialization_fixture(id integer);
INSERT INTO schema_migrations(version) VALUES ('113_serialization_fixture');
COMMIT;
SQL
run & first=$!
run & second=$!
wait "$first"
wait "$second"
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='113_serialization_fixture'")" = 1
cat > "$scratch/migrations/114_failure_fixture.sql" <<'SQL'
BEGIN;
CREATE TABLE migration_failure_fixture(id integer);
INSERT INTO schema_migrations(version) VALUES ('114_failure_fixture');
SELECT 1/0;
COMMIT;
SQL
if run; then echo 'Broken migration succeeded'; exit 1; fi
test "$(query "SELECT to_regclass('public.migration_failure_fixture') IS NULL")" = t
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='114_failure_fixture'")" = 0
rm "$scratch/migrations/114_failure_fixture.sql"
run
cat > "$scratch/migrations/115_unrecorded_fixture.sql" <<'SQL'
BEGIN;
CREATE TABLE migration_unrecorded_fixture(id integer);
COMMIT;
SQL
if run; then echo 'Unrecorded migration silently succeeded'; exit 1; fi
test "$(query "SELECT count(*) FROM schema_migrations WHERE version='115_unrecorded_fixture'")" = 0
rm "$scratch/migrations/115_unrecorded_fixture.sql"
run
echo 'Clean, repeat, forward upgrade, serialized runners and failure rollback passed.'
