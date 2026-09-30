#!/usr/bin/env bash
set -euo pipefail
# ARCH-07-TC-01 / AC-004: real non-bypass role, no SECURITY DEFINER queue paths.
psql -v ON_ERROR_STOP=1 --quiet <<'SQL'
GRANT SELECT, INSERT, UPDATE ON background_jobs TO strataai_ci_app;
INSERT INTO background_jobs(id, tenant_id, job_type, idempotency_key, actor_id, service_identity, correlation_id)
VALUES ('77777777-7777-7777-7777-777777777777','22222222-2222-2222-2222-222222222222',
    'TEST_JOB','tenant-b','22222222-2222-2222-2222-222222222222','ci-worker','arch-07-ci');
SET ROLE strataai_ci_app;
SELECT set_config('app.tenant_id','11111111-1111-1111-1111-111111111111',false);
DO $$ BEGIN
    IF EXISTS (SELECT FROM background_jobs) THEN RAISE EXCEPTION 'tenant B leaked'; END IF;
    IF EXISTS (SELECT FROM claim_background_job('99999999-9999-9999-9999-999999999999')) THEN
        RAISE EXCEPTION 'tenant B claimed';
    END IF;
    BEGIN
        INSERT INTO background_jobs(id, tenant_id, job_type, idempotency_key, actor_id, service_identity, correlation_id)
        VALUES (gen_random_uuid(),'22222222-2222-2222-2222-222222222222','TEST_JOB','forbidden',
            gen_random_uuid(),'ci-worker','arch-07-ci');
        RAISE EXCEPTION 'cross-tenant enqueue allowed';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
-- Publication participates in the domain write transaction: both roll back.
BEGIN;
UPDATE organizations SET description = 'uncommitted job change'
WHERE id='11111111-1111-1111-1111-111111111111';
INSERT INTO background_jobs(id, tenant_id, job_type, idempotency_key, actor_id, service_identity, correlation_id)
VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','TEST_JOB','rolled-back',gen_random_uuid(),'ci-worker','arch-07-ci');
ROLLBACK;
DO $$ BEGIN
    IF EXISTS (SELECT FROM background_jobs WHERE idempotency_key='rolled-back') OR
       EXISTS (SELECT FROM organizations WHERE description='uncommitted job change') THEN
        RAISE EXCEPTION 'transactional publication rollback failed';
    END IF;
END $$;
INSERT INTO background_jobs(id, tenant_id, job_type, idempotency_key, actor_id, service_identity, correlation_id, max_attempts)
VALUES ('88888888-8888-8888-8888-888888888888','11111111-1111-1111-1111-111111111111','TEST_JOB','retry-key',gen_random_uuid(),'ci-worker','arch-07-ci',2);
INSERT INTO background_jobs(id, tenant_id, job_type, idempotency_key, actor_id, service_identity, correlation_id)
VALUES (gen_random_uuid(),'11111111-1111-1111-1111-111111111111','TEST_JOB','retry-key',gen_random_uuid(),'ci-worker','arch-07-ci')
ON CONFLICT (tenant_id,job_type,idempotency_key) DO NOTHING;
DO $$ DECLARE j background_jobs; second background_jobs; BEGIN
    IF (SELECT count(*) FROM background_jobs) <> 1 THEN RAISE EXCEPTION 'duplicate job'; END IF;
    SELECT * INTO STRICT j FROM claim_background_job('99999999-9999-9999-9999-999999999999');
    IF j.attempt_count <> 1 OR j.state <> 'RUNNING' THEN RAISE EXCEPTION 'bad first claim'; END IF;
    IF EXISTS (SELECT FROM claim_background_job('aaaaaaaa-1111-1111-1111-111111111111')) THEN RAISE EXCEPTION 'active lease stolen'; END IF;
    IF complete_background_job(j.id,gen_random_uuid(),j.worker_id) THEN RAISE EXCEPTION 'wrong lease accepted'; END IF;
    IF complete_background_job(j.id,j.lease_id,gen_random_uuid()) THEN RAISE EXCEPTION 'wrong worker accepted'; END IF;
    IF NOT fail_background_job(j.id,j.lease_id,j.worker_id,'provider_unavailable') THEN RAISE EXCEPTION 'retry rejected'; END IF;
    IF NOT EXISTS (SELECT FROM background_jobs WHERE state='PENDING' AND available_at > clock_timestamp() + interval '25 seconds') THEN
        RAISE EXCEPTION 'backoff missing'; END IF;
    IF EXISTS (SELECT FROM claim_background_job(j.worker_id)) THEN RAISE EXCEPTION 'backoff ignored'; END IF;
    UPDATE background_jobs SET available_at=clock_timestamp()-interval '1 second' WHERE id=j.id;
    SELECT * INTO STRICT second FROM claim_background_job(j.worker_id);
    IF second.attempt_count <> 2 OR second.lease_id = j.lease_id THEN RAISE EXCEPTION 'bad retry lease'; END IF;
    IF complete_background_job(j.id,j.lease_id,j.worker_id) THEN RAISE EXCEPTION 'stale completion accepted'; END IF;
    IF NOT fail_background_job(second.id,second.lease_id,second.worker_id,'provider_unavailable') THEN RAISE EXCEPTION 'final failure rejected'; END IF;
    IF NOT EXISTS (SELECT FROM background_jobs WHERE state='FAILED' AND attempt_count=2) THEN RAISE EXCEPTION 'dead letter missing'; END IF;
    IF EXISTS (SELECT FROM claim_background_job(j.worker_id)) THEN RAISE EXCEPTION 'dead letter reclaimed'; END IF;
END $$;
-- Crash/restart recovery fences the old worker; a final-attempt crash terminates.
UPDATE background_jobs SET state='PENDING',attempt_count=0,available_at=clock_timestamp()-interval '1 second';
DO $$ DECLARE old_job background_jobs; new_job background_jobs; BEGIN
    SELECT * INTO STRICT old_job FROM claim_background_job('99999999-9999-9999-9999-999999999999');
    UPDATE background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second';
    SELECT * INTO STRICT new_job FROM claim_background_job('aaaaaaaa-1111-1111-1111-111111111111');
    IF new_job.attempt_count <> 2 OR new_job.worker_id=old_job.worker_id THEN RAISE EXCEPTION 'restart recovery failed'; END IF;
    IF complete_background_job(old_job.id,old_job.lease_id,old_job.worker_id) THEN RAISE EXCEPTION 'replaced worker completed'; END IF;
    UPDATE background_jobs SET lease_expires_at=clock_timestamp()-interval '1 second';
    IF complete_background_job(new_job.id,new_job.lease_id,new_job.worker_id) THEN RAISE EXCEPTION 'expired lease completed'; END IF;
    PERFORM * FROM claim_background_job(old_job.worker_id);
    IF NOT EXISTS (SELECT FROM background_jobs WHERE state='FAILED' AND last_error_code='lease_expired') THEN RAISE EXCEPTION 'crashed final attempt not terminal'; END IF;
END $$;
UPDATE background_jobs SET state='PENDING',attempt_count=0,available_at=clock_timestamp()-interval '1 second';
DO $$ DECLARE j background_jobs; BEGIN
    SELECT * INTO STRICT j FROM claim_background_job('99999999-9999-9999-9999-999999999999');
    IF NOT complete_background_job(j.id,j.lease_id,j.worker_id) THEN RAISE EXCEPTION 'success rejected'; END IF;
    IF complete_background_job(j.id,j.lease_id,j.worker_id) THEN RAISE EXCEPTION 'replayed completion accepted'; END IF;
END $$;
SELECT set_config('app.tenant_id','',false);
DO $$ BEGIN
    IF EXISTS (SELECT FROM background_jobs) OR EXISTS (SELECT FROM claim_background_job(gen_random_uuid())) THEN
        RAISE EXCEPTION 'missing tenant did not fail closed'; END IF;
END $$;
SQL
scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT
psql -v ON_ERROR_STOP=1 --quiet --command="UPDATE background_jobs SET state='PENDING',attempt_count=0,available_at=clock_timestamp()-interval '1 second' WHERE id='88888888-8888-8888-8888-888888888888';"
# Separate competing connections must commit exactly one claim for one job.
PGOPTIONS='-c app.tenant_id=11111111-1111-1111-1111-111111111111' PGPASSWORD='strataai-ci-app' \
  psql --username=strataai_ci_app -v ON_ERROR_STOP=1 --tuples-only --no-align --quiet \
  --command="SELECT count(*) FROM claim_background_job('99999999-9999-9999-9999-999999999999');" >"$scratch/first" &
first_pid=$!
PGOPTIONS='-c app.tenant_id=11111111-1111-1111-1111-111111111111' PGPASSWORD='strataai-ci-app' \
  psql --username=strataai_ci_app -v ON_ERROR_STOP=1 --tuples-only --no-align --quiet \
  --command="SELECT count(*) FROM claim_background_job('aaaaaaaa-1111-1111-1111-111111111111');" >"$scratch/second" &
second_pid=$!
wait "$first_pid"
wait "$second_pid"
first_count="$(cat "$scratch/first")"
second_count="$(cat "$scratch/second")"
test "$((first_count + second_count))" = 1
echo 'Tenant-scoped durable publication, idempotency, lease fencing, retries, crash recovery and dead-letter checks passed.'
