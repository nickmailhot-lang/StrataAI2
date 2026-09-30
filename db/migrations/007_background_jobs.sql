BEGIN;

-- Organization jobs carry references/safe metadata, never documents, credentials,
-- or plaintext reset/invite tokens. Global identity delivery is a separate scope.
CREATE TABLE background_jobs (
    id uuid PRIMARY KEY,
    tenant_id uuid NOT NULL REFERENCES organizations(id) ON DELETE RESTRICT,
    job_type text NOT NULL CHECK (job_type ~ '^[A-Z][A-Z0-9_]{0,79}$'),
    idempotency_key text NOT NULL CHECK (length(idempotency_key) BETWEEN 1 AND 200),
    actor_id uuid NOT NULL,
    service_identity text NOT NULL CHECK (length(btrim(service_identity)) BETWEEN 1 AND 120),
    correlation_id text NOT NULL CHECK (length(btrim(correlation_id)) BETWEEN 1 AND 120),
    safe_metadata jsonb NOT NULL DEFAULT '{}'::jsonb
        CHECK (jsonb_typeof(safe_metadata) = 'object' AND octet_length(safe_metadata::text) <= 32768),
    state text NOT NULL DEFAULT 'PENDING' CHECK (state IN ('PENDING','RUNNING','SUCCEEDED','FAILED')),
    attempt_count integer NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
    max_attempts integer NOT NULL DEFAULT 5 CHECK (max_attempts BETWEEN 1 AND 10),
    available_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    lease_id uuid,
    worker_id uuid,
    lease_expires_at timestamptz,
    last_error_code text CHECK (last_error_code ~ '^[a-z][a-z0-9_]{0,79}$'),
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    version bigint NOT NULL DEFAULT 1 CHECK (version > 0),
    UNIQUE (tenant_id, job_type, idempotency_key),
    CHECK (attempt_count <= max_attempts),
    CHECK ((state = 'RUNNING') =
        (lease_id IS NOT NULL AND worker_id IS NOT NULL AND lease_expires_at IS NOT NULL))
);

CREATE INDEX ix_background_jobs_pending
    ON background_jobs(tenant_id, available_at, created_at, id) WHERE state = 'PENDING';
CREATE INDEX ix_background_jobs_expired_lease
    ON background_jobs(tenant_id, lease_expires_at) WHERE state = 'RUNNING';
ALTER TABLE background_jobs ENABLE ROW LEVEL SECURITY;
ALTER TABLE background_jobs FORCE ROW LEVEL SECURITY;
CREATE POLICY background_jobs_tenant_isolation ON background_jobs
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

-- SECURITY INVOKER deliberately retains the caller's RLS and grants. Claiming
-- commits a lease; provider work must occur after that short transaction commits.
CREATE FUNCTION claim_background_job(p_worker_id uuid)
RETURNS SETOF background_jobs LANGUAGE plpgsql SECURITY INVOKER AS $$
DECLARE v_now timestamptz := clock_timestamp();
BEGIN
    IF p_worker_id IS NULL THEN RAISE EXCEPTION 'worker identity is required'; END IF;
    -- A crash on the final attempt must eventually reach a terminal state.
    UPDATE background_jobs SET state = 'FAILED', lease_id = NULL, worker_id = NULL,
        lease_expires_at = NULL, last_error_code = 'lease_expired',
        updated_at = v_now, version = version + 1
    WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
      AND state = 'RUNNING' AND lease_expires_at <= v_now AND attempt_count >= max_attempts;
    RETURN QUERY
    WITH candidate AS (
        SELECT id FROM background_jobs
        WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
          AND attempt_count < max_attempts
          AND ((state = 'PENDING' AND available_at <= v_now)
            OR (state = 'RUNNING' AND lease_expires_at <= v_now))
        ORDER BY available_at, created_at, id
        LIMIT 1 FOR UPDATE SKIP LOCKED
    )
    UPDATE background_jobs j SET state = 'RUNNING', attempt_count = attempt_count + 1,
        lease_id = gen_random_uuid(), worker_id = p_worker_id,
        lease_expires_at = v_now + interval '2 minutes', updated_at = v_now, version = version + 1
    FROM candidate c WHERE j.id = c.id RETURNING j.*;
END;
$$;

CREATE FUNCTION complete_background_job(p_job_id uuid, p_lease_id uuid, p_worker_id uuid)
RETURNS boolean LANGUAGE plpgsql SECURITY INVOKER AS $$
BEGIN
    UPDATE background_jobs SET state = 'SUCCEEDED', lease_id = NULL, worker_id = NULL,
        lease_expires_at = NULL, last_error_code = NULL,
        updated_at = clock_timestamp(), version = version + 1
    WHERE id = p_job_id AND tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
      AND state = 'RUNNING' AND lease_id = p_lease_id AND worker_id = p_worker_id
      AND lease_expires_at > clock_timestamp();
    RETURN FOUND;
END;
$$;

CREATE FUNCTION fail_background_job(p_job_id uuid, p_lease_id uuid, p_worker_id uuid, p_error_code text)
RETURNS boolean LANGUAGE plpgsql SECURITY INVOKER AS $$
BEGIN
    IF p_error_code IS NULL OR p_error_code !~ '^[a-z][a-z0-9_]{0,79}$' THEN
        RAISE EXCEPTION 'safe stable error code is required';
    END IF;
    UPDATE background_jobs SET state = CASE WHEN attempt_count >= max_attempts THEN 'FAILED' ELSE 'PENDING' END,
        available_at = clock_timestamp() + make_interval(secs => LEAST(3600, 30 * power(2, attempt_count - 1))::integer),
        lease_id = NULL, worker_id = NULL, lease_expires_at = NULL,
        last_error_code = p_error_code, updated_at = clock_timestamp(), version = version + 1
    WHERE id = p_job_id AND tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
      AND state = 'RUNNING' AND lease_id = p_lease_id AND worker_id = p_worker_id
      AND lease_expires_at > clock_timestamp();
    RETURN FOUND;
END;
$$;

INSERT INTO schema_migrations(version) VALUES ('007_background_jobs') ON CONFLICT (version) DO NOTHING;
COMMIT;
