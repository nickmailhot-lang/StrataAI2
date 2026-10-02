BEGIN;
ALTER TABLE work_events DROP CONSTRAINT work_events_entity_type_check;
ALTER TABLE work_events ADD CONSTRAINT work_events_entity_type_check
 CHECK(entity_type IN ('Board','List','Card','Label','WatchSubscription','Reminder'));
ALTER TABLE work_events ADD CONSTRAINT work_events_reminder_type_check
 CHECK((entity_type='Reminder')=(event_type IN ('REMINDER_SCHEDULED','REMINDER_CANCELLED','REMINDER_FIRED')));
ALTER TABLE work_events ADD COLUMN reminder_id uuid
 GENERATED ALWAYS AS (CASE WHEN entity_type='Reminder' THEN entity_id END) STORED;
ALTER TABLE work_events ADD CONSTRAINT work_events_reminder_scope_fk
 FOREIGN KEY(tenant_id,reminder_id) REFERENCES card_reminders(tenant_id,id) ON DELETE RESTRICT;
ALTER TABLE card_assignment_notifications DROP CONSTRAINT card_assignment_notifications_notification_type_check;
ALTER TABLE card_assignment_notifications ADD CONSTRAINT card_assignment_notifications_notification_type_check
 CHECK(notification_type IN ('CARD_ASSIGNED','CARD_CREATED','CARD_UPDATED','CARD_MOVED','CARD_ARCHIVED','CARD_RESTORED',
  'CARD_MEMBER_ADDED','CARD_MEMBER_REMOVED','LABEL_ADDED','LABEL_REMOVED','CARD_DATE_CHANGED','CARD_DUE_COMPLETED','CARD_DUE_REOPENED','REMINDER_FIRED'));
ALTER TABLE card_assignment_notifications DROP CONSTRAINT card_assignment_notifications_check;
ALTER TABLE card_assignment_notifications ADD CONSTRAINT notification_actor_recipient_check
 CHECK(actor_id<>recipient_id OR notification_type='REMINDER_FIRED');

-- The Worker gets only this capability, never general Card/account/membership
-- reads or notification writes. Every protected reference is tenant qualified.
CREATE FUNCTION deliver_card_reminder(p_job uuid,p_tenant uuid,p_actor uuid,p_worker uuid,p_lease uuid,
 p_reminder uuid,p_generation bigint,p_require_verified boolean) RETURNS text
LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,public AS $$
DECLARE
 hint_card uuid; hint_user uuid; hint_board uuid;
 r public.card_reminders%ROWTYPE; c public.cards%ROWTYPE; b public.boards%ROWTYPE;
 m public.organization_members%ROWTYPE; u public.users%ROWTYPE; j public.background_jobs%ROWTYPE;
 organization_status text; board_member_status text; list_status text; sequence_number bigint; effect_time timestamptz;
BEGIN
 IF p_tenant IS DISTINCT FROM NULLIF(current_setting('app.tenant_id',true),'')::uuid
  OR p_tenant IS NULL OR COALESCE(p_generation,0)<1 OR p_require_verified IS NULL THEN RETURN 'LEASE_LOST'; END IF;
 -- Reject an unproven claim before loading or locking protected scope. Recheck
 -- with a row lock after acquiring the Board gate, including elapsed lease time.
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='CARD_REMINDER' AND service_identity='card-reminder-delivery'
  AND safe_metadata=jsonb_build_object('reminderId',p_reminder,'generation',p_generation)
  AND idempotency_key='card-reminder/'||replace(p_reminder::text,'-','')||'/'||p_generation::text
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp())
  THEN RETURN 'LEASE_LOST'; END IF;
 SELECT reminder.card_id,reminder.user_id,card.board_id INTO hint_card,hint_user,hint_board
 FROM public.card_reminders reminder JOIN public.cards card ON card.id=reminder.card_id AND card.tenant_id=reminder.tenant_id
 WHERE reminder.tenant_id=p_tenant AND reminder.id=p_reminder;
 IF hint_card IS NOT NULL THEN
  -- Match command order: Organization/member, Board gate, Board member, account.
  SELECT status INTO organization_status FROM public.organizations WHERE id=p_tenant FOR SHARE;
  SELECT * INTO m FROM public.organization_members WHERE tenant_id=p_tenant AND user_id=hint_user FOR SHARE;
  SELECT * INTO b FROM public.boards WHERE tenant_id=p_tenant AND id=hint_board FOR UPDATE;
  SELECT status INTO board_member_status FROM public.board_members
   WHERE tenant_id=p_tenant AND board_id=hint_board AND user_id=hint_user FOR SHARE;
  SELECT * INTO u FROM public.users WHERE id=hint_user FOR SHARE;
  SELECT * INTO c FROM public.cards WHERE tenant_id=p_tenant AND id=hint_card;
  -- Movement committed before this gate was acquired. Retry using its new scope.
  IF c.board_id IS DISTINCT FROM hint_board THEN RETURN 'LEASE_LOST'; END IF;
  SELECT * INTO r FROM public.card_reminders WHERE tenant_id=p_tenant AND id=p_reminder FOR UPDATE;
 END IF;
 -- Board precedes the claim lock so an API duplicate publication holding the
 -- same gate cannot deadlock against the Worker waiting for that Board.
 SELECT * INTO j FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job AND actor_id=p_actor
  AND job_type='CARD_REMINDER' AND service_identity='card-reminder-delivery'
  AND safe_metadata=jsonb_build_object('reminderId',p_reminder,'generation',p_generation)
  AND idempotency_key='card-reminder/'||replace(p_reminder::text,'-','')||'/'||p_generation::text
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp() FOR UPDATE;
 IF NOT FOUND THEN RETURN 'LEASE_LOST'; END IF;
 IF r.id IS NULL OR r.generation<>p_generation OR NOT r.enabled OR r.status NOT IN ('SCHEDULED','FIRED')
  THEN RETURN 'SUPERSEDED'; END IF;
 IF r.status='FIRED' THEN RETURN 'DELIVERED'; END IF;
 SELECT lifecycle_state INTO list_status FROM public.board_lists
  WHERE tenant_id=p_tenant AND board_id=c.board_id AND id=c.list_id;
 IF organization_status IS DISTINCT FROM 'ACTIVE' OR m.status IS DISTINCT FROM 'ACTIVE'
  OR u.status IS DISTINCT FROM 'ACTIVE' OR (p_require_verified AND NOT u.email_verified)
  OR b.lifecycle_state IS DISTINCT FROM 'ACTIVE' OR c.lifecycle_state IS DISTINCT FROM 'ACTIVE'
  OR list_status IS DISTINCT FROM 'ACTIVE' OR c.due_complete OR c.due_at IS DISTINCT FROM r.due_at
  OR NOT (b.visibility IN ('ORGANIZATION','PUBLIC') OR m.role IN ('OWNER','ADMIN') OR COALESCE(board_member_status,'')='ACTIVE')
  THEN RETURN 'SUPERSEDED'; END IF;
 IF r.trigger_at>clock_timestamp() THEN RETURN 'LEASE_LOST'; END IF;
 effect_time=GREATEST(clock_timestamp(),r.updated_at);
 INSERT INTO public.work_event_streams(tenant_id,board_id) VALUES(p_tenant,c.board_id) ON CONFLICT DO NOTHING;
 UPDATE public.work_event_streams SET last_sequence=last_sequence+1,updated_at=effect_time
  WHERE tenant_id=p_tenant AND board_id=c.board_id RETURNING last_sequence INTO sequence_number;
 INSERT INTO public.work_events(tenant_id,event_id,board_id,sequence,actor_id,event_type,entity_type,entity_id,
  entity_version,correlation_id,created_at,ready_at)
 VALUES(p_tenant,p_job,c.board_id,sequence_number,p_actor,'REMINDER_FIRED','Reminder',r.id,r.version+1,
  j.correlation_id,effect_time,effect_time);
 INSERT INTO public.card_assignment_notifications(tenant_id,id,board_id,card_id,event_id,recipient_id,actor_id,
  notification_type,card_version,created_at)
 VALUES(p_tenant,p_job,c.board_id,c.id,p_job,r.user_id,p_actor,'REMINDER_FIRED',c.version,effect_time);
 INSERT INTO public.audit_events(id,tenant_id,actor_id,event_type,entity_type,entity_id,correlation_id,created_at)
 VALUES(p_job,p_tenant,p_actor,'REMINDER_FIRED','Reminder',r.id,j.correlation_id,effect_time);
 UPDATE public.card_reminders SET status='FIRED',version=version+1,updated_at=effect_time
  WHERE tenant_id=p_tenant AND id=r.id AND generation=p_generation AND status='SCHEDULED';
 -- Raising after tentative effects rolls back the entire statement, including
 -- sequence allocation. An expired lease cannot leave any committed effect.
 IF NOT EXISTS(SELECT 1 FROM public.background_jobs WHERE tenant_id=p_tenant AND id=p_job
  AND state='RUNNING' AND worker_id=p_worker AND lease_id=p_lease AND lease_expires_at>clock_timestamp())
  THEN RAISE EXCEPTION 'Reminder lease fence failed'; END IF;
 RETURN 'DELIVERED';
END;
$$;
REVOKE ALL ON FUNCTION deliver_card_reminder(uuid,uuid,uuid,uuid,uuid,uuid,bigint,boolean) FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('038_card_reminder_delivery');
COMMIT;
