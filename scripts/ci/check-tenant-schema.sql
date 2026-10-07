-- ARCH-04-FR-002/004: classify every application table, then inspect the
-- actual migrated catalog. Global identity tables use separate subject/service
-- authorization; this classification does not grant database privileges.
DO $$
DECLARE
    relation record;
    isolation_key record;
    actor_tables constant text[] := ARRAY['search_interaction_streams','search_interaction_events','board_filter_interaction_replays',
      'navigation_interaction_events','navigation_interaction_replays',
      'invitation_issuer_authority_proofs','invitation_issuer_authority_sources',
      'invitation_issuer_authority_jobs','invitation_issuer_authority_effects'];
    global_tables constant text[] := ARRAY[
      'schema_migrations','users','sessions','password_reset_tokens','email_verification_tokens',
      'identity_delivery_jobs','identity_event_streams','identity_events','identity_profile_replays',
      'identity_revocation_replays','identity_login_replays','identity_registration_replays',
      'identity_recovery_request_replays','identity_token_consumption_replays',
      'mention_handle_reservations','user_mention_handles','identity_handle_claim_replays'];
BEGIN
    FOR relation IN
      SELECT c.oid, c.relname, c.relrowsecurity, c.relforcerowsecurity
      FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      WHERE n.nspname='public' AND c.relkind IN ('r','p')
        AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.classid='pg_class'::regclass
          AND d.objid=c.oid AND d.deptype='e')
      ORDER BY c.relname
    LOOP
      IF relation.relname = ANY(actor_tables) THEN
        SELECT a.attnotnull,a.atttypid INTO isolation_key FROM pg_attribute a
          WHERE a.attrelid=relation.oid AND NOT a.attisdropped AND a.attname='actor_id';
        IF NOT FOUND OR isolation_key.atttypid<>'uuid'::regtype OR isolation_key.attnotnull IS NOT TRUE
          OR NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity THEN
          RAISE EXCEPTION 'Actor schema invariant failed: % requires non-null UUID actor and forced RLS',relation.relname;
        END IF;
        IF (SELECT count(*) FROM pg_policy WHERE polrelid=relation.oid)<>1 OR NOT EXISTS (
          SELECT 1 FROM pg_policy p WHERE p.polrelid=relation.oid AND p.polcmd='*'
            AND pg_get_expr(p.polqual,p.polrelid) LIKE '%actor_id%app.identity_subject%'
            AND pg_get_expr(p.polwithcheck,p.polrelid) LIKE '%actor_id%app.identity_subject%') THEN
          RAISE EXCEPTION 'Actor schema invariant failed: % requires explicit subject read/write policy',relation.relname;
        END IF;
        CONTINUE;
      END IF;
      IF relation.relname IN ('invitation_recipient_streams','invitation_recipient_authority_revisions') THEN
        SELECT a.attnotnull,a.atttypid INTO isolation_key FROM pg_attribute a
          WHERE a.attrelid=relation.oid AND NOT a.attisdropped AND a.attname='email_normalized';
        IF NOT FOUND OR isolation_key.atttypid<>'text'::regtype OR isolation_key.attnotnull IS NOT TRUE
          OR NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity
          OR (SELECT count(*) FROM pg_policy WHERE polrelid=relation.oid)<>1 OR NOT EXISTS(
          SELECT 1 FROM pg_policy p WHERE p.polrelid=relation.oid AND p.polcmd='r'
            AND pg_get_expr(p.polqual,p.polrelid) LIKE '%app.route_kind%INVITATION_RECIPIENT%email_normalized%app.route_key%') THEN
          RAISE EXCEPTION 'Recipient schema invariant failed: % requires explicit recipient-only read policy and forced RLS',relation.relname;
        END IF;
        CONTINUE;
      END IF;
      IF relation.relname = ANY(global_tables) THEN CONTINUE; END IF;
      SELECT a.attnotnull, a.atttypid INTO isolation_key
      FROM pg_attribute a WHERE a.attrelid=relation.oid AND NOT a.attisdropped
        AND a.attname=CASE WHEN relation.relname='organizations' THEN 'id' ELSE 'tenant_id' END;
      -- The shared append-only audit table also stores demonstrably global
      -- identity events without manufacturing an Organization. Keep its UUID
      -- key, forced RLS and policy checks; only this existing nullable-key
      -- classification differs from tenant-only tables.
      IF NOT FOUND OR isolation_key.atttypid <> 'uuid'::regtype
        OR (isolation_key.attnotnull IS NOT TRUE AND relation.relname <> 'audit_events') THEN
        RAISE EXCEPTION 'Tenant schema invariant failed: % requires a non-null UUID Organization key', relation.relname;
      END IF;
      IF NOT relation.relrowsecurity OR NOT relation.relforcerowsecurity THEN
        RAISE EXCEPTION 'Tenant schema invariant failed: % requires enabled and forced RLS', relation.relname;
      END IF;
      IF NOT EXISTS (SELECT 1 FROM pg_policy p WHERE p.polrelid=relation.oid) THEN
        RAISE EXCEPTION 'Tenant schema invariant failed: % requires an explicit RLS policy', relation.relname;
      END IF;
    END LOOP;
END;
$$;
