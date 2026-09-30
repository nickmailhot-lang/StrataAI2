#!/usr/bin/env bash
set -euo pipefail
psql -v ON_ERROR_STOP=1 --quiet -f scripts/ci/grant-identity-worker.sql
psql -v ON_ERROR_STOP=1 --quiet <<'SQL'
INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,created_at,updated_at)
VALUES ('44444444-4444-4444-4444-444444444444','identity@example.test','IDENTITY@EXAMPLE.TEST','Identity fixture','not-a-real-password-hash','ACTIVE',now(),now()),
       ('55555555-5555-5555-5555-555555555555','other-identity@example.test','OTHER-IDENTITY@EXAMPLE.TEST','Other fixture','not-a-real-password-hash','ACTIVE',now(),now());
INSERT INTO password_reset_tokens(id,user_id,token_hash,created_at,expires_at)
VALUES ('66666666-6666-6666-6666-666666666666','44444444-4444-4444-4444-444444444444',repeat('a',64),now(),now()+interval '30 minutes');
INSERT INTO identity_delivery_jobs(id,user_id,purpose,password_reset_token_id,key_id,correlation_id,recipient_email,sender_address,public_origin,provider_account,created_at,expires_at)
VALUES ('66666666-6666-6666-6666-666666666666','44444444-4444-4444-4444-444444444444','RESET_PASSWORD','66666666-6666-6666-6666-666666666666','k1','identity-scope-test','identity@example.test','sender@example.test','https://app.example.test','ci-account',now(),now()+interval '30 minutes');
-- Neither a missing reference nor another subject's token can be published.
DO $$ BEGIN
    BEGIN
        INSERT INTO identity_delivery_jobs(id,user_id,purpose,key_id,correlation_id,recipient_email,sender_address,public_origin,provider_account,created_at,expires_at)
        VALUES (gen_random_uuid(),'44444444-4444-4444-4444-444444444444','RESET_PASSWORD','k1','test','identity@example.test','sender@example.test','https://app.example.test','ci-account',now(),now()+interval '30 minutes');
        RAISE EXCEPTION 'missing token accepted';
    EXCEPTION WHEN check_violation THEN NULL; END;
    BEGIN
        UPDATE identity_delivery_jobs SET user_id='55555555-5555-5555-5555-555555555555';
        RAISE EXCEPTION 'wrong token subject accepted';
    EXCEPTION WHEN foreign_key_violation THEN NULL; END;
END $$;
SET ROLE strataai_ci_app;
SELECT set_config('app.service_scope','GLOBAL_IDENTITY_MAIL',false);
DO $$ BEGIN
    BEGIN PERFORM * FROM identity_delivery_jobs; RAISE EXCEPTION 'Organization role read global mail';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
END $$;
RESET ROLE;
SET ROLE strataai_identity_mail_ci;
SELECT set_config('app.service_scope','',false);
DO $$ BEGIN
    IF EXISTS (SELECT FROM identity_delivery_jobs) OR EXISTS (SELECT FROM claim_identity_delivery(gen_random_uuid())) THEN
        RAISE EXCEPTION 'missing identity service scope did not fail closed'; END IF;
END $$;
SELECT set_config('app.service_scope','GLOBAL_IDENTITY_MAIL',false);
DO $$ DECLARE j identity_delivery_jobs; replacement identity_delivery_jobs; BEGIN
    BEGIN PERFORM password_hash FROM users; RAISE EXCEPTION 'worker read password hashes';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    BEGIN PERFORM * FROM organizations; RAISE EXCEPTION 'identity worker read Organization data';
    EXCEPTION WHEN insufficient_privilege THEN NULL; END;
    SELECT * INTO STRICT j FROM claim_identity_delivery('99999999-9999-9999-9999-999999999999');
    IF j.scope_kind<>'GLOBAL_IDENTITY_MAIL' OR j.attempt_count<>1 THEN RAISE EXCEPTION 'wrong claim context'; END IF;
    IF EXISTS (SELECT FROM claim_identity_delivery(gen_random_uuid())) THEN RAISE EXCEPTION 'active identity lease stolen'; END IF;
    IF finish_identity_delivery(j.id,gen_random_uuid(),j.worker_id,'SENT',NULL,gen_random_uuid()) THEN RAISE EXCEPTION 'wrong lease acknowledged'; END IF;
    IF NOT finish_identity_delivery(j.id,j.lease_id,j.worker_id,'RETRY','identity_provider_unavailable') THEN RAISE EXCEPTION 'retry rejected'; END IF;
    IF EXISTS (SELECT FROM claim_identity_delivery(j.worker_id)) THEN RAISE EXCEPTION 'identity backoff ignored'; END IF;
    UPDATE identity_delivery_jobs SET available_at=clock_timestamp()-interval '1 second';
    SELECT * INTO STRICT replacement FROM claim_identity_delivery(j.worker_id);
    IF finish_identity_delivery(j.id,j.lease_id,j.worker_id,'SENT',NULL,gen_random_uuid()) THEN RAISE EXCEPTION 'stale identity lease acknowledged'; END IF;
    IF NOT finish_identity_delivery(replacement.id,replacement.lease_id,replacement.worker_id,'SENT',NULL,gen_random_uuid()) THEN RAISE EXCEPTION 'receipt completion rejected'; END IF;
    IF EXISTS (SELECT FROM claim_identity_delivery(j.worker_id)) THEN RAISE EXCEPTION 'sent identity mail reclaimed'; END IF;
END $$;
-- Final-attempt crash and expiry cannot produce endless sends.
UPDATE identity_delivery_jobs SET state='PENDING',attempt_count=4,available_at=clock_timestamp()-interval '1 second';
DO $$ DECLARE j identity_delivery_jobs; BEGIN
    SELECT * INTO STRICT j FROM claim_identity_delivery(gen_random_uuid());
    UPDATE identity_delivery_jobs SET lease_expires_at=clock_timestamp()-interval '1 second';
    IF finish_identity_delivery(j.id,j.lease_id,j.worker_id,'SENT',NULL,gen_random_uuid()) THEN RAISE EXCEPTION 'expired identity lease acknowledged'; END IF;
    PERFORM * FROM claim_identity_delivery(gen_random_uuid());
    IF NOT EXISTS (SELECT FROM identity_delivery_jobs WHERE state='FAILED' AND attempt_count=5) THEN RAISE EXCEPTION 'final identity crash not terminal'; END IF;
END $$;
UPDATE identity_delivery_jobs SET state='PENDING',attempt_count=0,created_at=clock_timestamp()-interval '40 minutes',expires_at=clock_timestamp()-interval '10 minutes';
DO $$ BEGIN
    IF EXISTS (SELECT FROM claim_identity_delivery(gen_random_uuid())) THEN RAISE EXCEPTION 'expired identity mail claimed'; END IF;
    IF NOT EXISTS (SELECT FROM identity_delivery_jobs WHERE state='CANCELLED') THEN RAISE EXCEPTION 'expired identity mail not cancelled'; END IF;
END $$;
SQL
echo 'Global identity scope, column grants, token-subject integrity, leases, retries, receipts and expiry checks passed.'
