BEGIN;
-- Fence the parent publication path before its counter and journal tables.
LOCK TABLE card_assignment_notifications,notification_event_streams,notification_events IN ACCESS EXCLUSIVE MODE;
DO $$ BEGIN
 IF EXISTS(SELECT 1 FROM notification_events e
  LEFT JOIN card_assignment_notifications n ON (n.tenant_id,n.id,n.recipient_id)=(e.tenant_id,e.notification_id,e.recipient_id)
  LEFT JOIN notification_event_streams s ON (s.tenant_id,s.recipient_id)=(e.tenant_id,e.recipient_id)
  WHERE n.id IS NULL OR s.tenant_id IS NULL OR NOT isfinite(e.created_at)
   OR e.board_id IS DISTINCT FROM n.board_id
   OR e.actor_id IS DISTINCT FROM CASE WHEN e.event_type='NOTIFICATION_CREATED' THEN n.actor_id ELSE n.recipient_id END
   OR e.created_at IS DISTINCT FROM CASE WHEN e.event_type='NOTIFICATION_CREATED' THEN n.created_at ELSE n.read_at END)
 OR EXISTS(SELECT 1 FROM notification_event_streams s LEFT JOIN
  (SELECT tenant_id,recipient_id,count(*) AS records,max(sequence) AS last_sequence,max(created_at) AS updated_at
   FROM notification_events GROUP BY tenant_id,recipient_id) e USING(tenant_id,recipient_id)
  LEFT JOIN notification_events first ON (first.tenant_id,first.recipient_id,first.sequence)=(s.tenant_id,s.recipient_id,1)
  WHERE e.records IS DISTINCT FROM s.last_sequence OR e.last_sequence IS DISTINCT FROM s.last_sequence
   OR s.created_at IS DISTINCT FROM first.created_at OR s.updated_at IS DISTINCT FROM e.updated_at) THEN
  RAISE EXCEPTION 'Notification journal history does not match retained parent facts' USING ERRCODE='23514';
 END IF;
END $$;
CREATE FUNCTION protect_notification_event_history() RETURNS trigger
 LANGUAGE plpgsql SET search_path=pg_catalog,public AS $$
BEGIN
 RAISE EXCEPTION 'Notification event history is immutable' USING ERRCODE='23514';
END $$;
CREATE TRIGGER notification_event_history BEFORE UPDATE OR DELETE ON notification_events
 FOR EACH ROW EXECUTE FUNCTION protect_notification_event_history();
REVOKE ALL ON FUNCTION protect_notification_event_history() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES('127_notification_event_history');
COMMIT;
