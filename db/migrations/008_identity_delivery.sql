BEGIN;
ALTER TABLE password_reset_tokens ADD CONSTRAINT uq_password_reset_subject UNIQUE(id,user_id);
ALTER TABLE email_verification_tokens ADD CONSTRAINT uq_verification_subject UNIQUE(id,user_id);

-- Global account delivery is a narrowly scoped service capability. It has no
-- Organization/board records, attachments, credentials or plaintext tokens.
CREATE TABLE identity_delivery_jobs (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    scope_kind text NOT NULL DEFAULT 'GLOBAL_IDENTITY_MAIL' CHECK (scope_kind='GLOBAL_IDENTITY_MAIL'),
    service_identity text NOT NULL DEFAULT 'identity-mail' CHECK (service_identity='identity-mail'),
    purpose text NOT NULL CHECK (purpose IN ('VERIFY_EMAIL','RESET_PASSWORD')),
    password_reset_token_id uuid,
    verification_token_id uuid,
    key_id text NOT NULL CHECK (key_id ~ '^[A-Za-z0-9_-]{1,32}$'),
    correlation_id text NOT NULL CHECK (length(btrim(correlation_id)) BETWEEN 1 AND 120),
    recipient_email text NOT NULL CHECK (length(recipient_email) BETWEEN 3 AND 320),
    sender_address text NOT NULL CHECK (length(sender_address) BETWEEN 3 AND 320),
    public_origin text NOT NULL CHECK (public_origin ~ '^https://[^/?#]+$'),
    provider_account text NOT NULL CHECK (provider_account ~ '^[A-Za-z0-9_-]{1,80}$'),
    state text NOT NULL DEFAULT 'PENDING' CHECK (state IN ('PENDING','RUNNING','SENT','FAILED','CANCELLED')),
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count BETWEEN 0 AND 5),
    available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    lease_id uuid,
    worker_id uuid,
    lease_expires_at timestamptz,
    last_error_code text CHECK (last_error_code ~ '^[a-z][a-z0-9_]{0,79}$'),
    provider_message_id uuid,
    created_at timestamptz NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    expires_at timestamptz NOT NULL,
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    FOREIGN KEY(password_reset_token_id,user_id) REFERENCES password_reset_tokens(id,user_id) ON DELETE RESTRICT,
    FOREIGN KEY(verification_token_id,user_id) REFERENCES email_verification_tokens(id,user_id) ON DELETE RESTRICT,
    CHECK ((purpose='RESET_PASSWORD' AND password_reset_token_id IS NOT NULL AND password_reset_token_id=id AND verification_token_id IS NULL)
        OR (purpose='VERIFY_EMAIL' AND verification_token_id IS NOT NULL AND verification_token_id=id AND password_reset_token_id IS NULL)),
    CHECK (expires_at > created_at AND expires_at <= created_at + interval '23 hours'),
    CHECK ((state='RUNNING') = (lease_id IS NOT NULL AND worker_id IS NOT NULL AND lease_expires_at IS NOT NULL)),
    CHECK (state<>'SENT' OR provider_message_id IS NOT NULL)
);
CREATE INDEX ix_identity_delivery_pending ON identity_delivery_jobs(available_at,created_at,id) WHERE state='PENDING';
CREATE INDEX ix_identity_delivery_leases ON identity_delivery_jobs(lease_expires_at) WHERE state='RUNNING';
ALTER TABLE identity_delivery_jobs ENABLE ROW LEVEL SECURITY;
ALTER TABLE identity_delivery_jobs FORCE ROW LEVEL SECURITY;
CREATE POLICY identity_delivery_service_scope ON identity_delivery_jobs
    USING (current_setting('app.service_scope',true)='GLOBAL_IDENTITY_MAIL')
    WITH CHECK (current_setting('app.service_scope',true)='GLOBAL_IDENTITY_MAIL');

CREATE FUNCTION claim_identity_delivery(p_worker uuid)
RETURNS SETOF identity_delivery_jobs LANGUAGE plpgsql SECURITY INVOKER AS $$
DECLARE v_now timestamptz := clock_timestamp();
BEGIN
    IF p_worker IS NULL OR p_worker='00000000-0000-0000-0000-000000000000'::uuid THEN
        RAISE EXCEPTION 'worker identity is required'; END IF;
    IF current_setting('app.service_scope',true) IS DISTINCT FROM 'GLOBAL_IDENTITY_MAIL' THEN RETURN; END IF;
    UPDATE identity_delivery_jobs SET state='CANCELLED',lease_id=NULL,worker_id=NULL,lease_expires_at=NULL,
        last_error_code='delivery_expired',updated_at=v_now,version=version+1
    WHERE state IN ('PENDING','RUNNING') AND expires_at <= v_now;
    UPDATE identity_delivery_jobs SET state='FAILED',lease_id=NULL,worker_id=NULL,lease_expires_at=NULL,
        last_error_code='lease_expired',updated_at=v_now,version=version+1
    WHERE state='RUNNING' AND lease_expires_at <= v_now AND attempt_count >= 5;
    RETURN QUERY WITH candidate AS (
        SELECT id FROM identity_delivery_jobs WHERE attempt_count < 5 AND expires_at > v_now
          AND ((state='PENDING' AND available_at <= v_now) OR (state='RUNNING' AND lease_expires_at <= v_now))
        ORDER BY available_at,created_at,id LIMIT 1 FOR UPDATE SKIP LOCKED
    ) UPDATE identity_delivery_jobs j SET state='RUNNING',attempt_count=attempt_count+1,
        lease_id=gen_random_uuid(),worker_id=p_worker,lease_expires_at=v_now+interval '2 minutes',
        updated_at=v_now,version=version+1 FROM candidate c WHERE j.id=c.id RETURNING j.*;
END;
$$;

CREATE FUNCTION finish_identity_delivery(p_job uuid,p_lease uuid,p_worker uuid,p_outcome text,p_error text DEFAULT NULL,p_receipt uuid DEFAULT NULL)
RETURNS boolean LANGUAGE plpgsql SECURITY INVOKER AS $$
BEGIN
    IF p_outcome NOT IN ('SENT','CANCELLED','RETRY','FAILED') OR p_outcome IS NULL THEN RAISE EXCEPTION 'invalid delivery outcome'; END IF;
    IF p_error IS NOT NULL AND p_error !~ '^[a-z][a-z0-9_]{0,79}$' THEN RAISE EXCEPTION 'safe error code is required'; END IF;
    UPDATE identity_delivery_jobs SET state=CASE WHEN p_outcome='RETRY' THEN
        CASE WHEN attempt_count >= 5 THEN 'FAILED' ELSE 'PENDING' END ELSE p_outcome END,
        available_at=clock_timestamp()+make_interval(secs=>LEAST(3600,30*power(2,attempt_count-1))::integer),
        lease_id=NULL,worker_id=NULL,lease_expires_at=NULL,last_error_code=p_error,
        provider_message_id=CASE WHEN p_outcome='SENT' THEN p_receipt ELSE provider_message_id END,
        updated_at=clock_timestamp(),version=version+1
    WHERE id=p_job AND state='RUNNING' AND lease_id=p_lease AND worker_id=p_worker
      AND lease_expires_at > clock_timestamp() AND expires_at > clock_timestamp()
      AND current_setting('app.service_scope',true)='GLOBAL_IDENTITY_MAIL';
    RETURN FOUND;
END;
$$;
INSERT INTO schema_migrations(version) VALUES ('008_identity_delivery') ON CONFLICT(version) DO NOTHING;
COMMIT;
