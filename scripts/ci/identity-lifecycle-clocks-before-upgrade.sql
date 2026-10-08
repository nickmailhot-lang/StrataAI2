-- CI migration-runner's disposable database only. Preserve complete pre-upgrade
-- rows so backfill cannot silently change lifecycle, expiry, identity or secret.
INSERT INTO users(id,email,email_normalized,display_name,status,password_hash,created_at,updated_at)
 VALUES('f14a0000-0000-4000-8000-000000000001','clock-upgrade@example.test','CLOCK-UPGRADE@EXAMPLE.TEST','Upgrade fixture','ACTIVE','unused-fixture-hash','2020-01-01T00:00:00Z','2020-01-01T00:00:00Z');
INSERT INTO sessions(id,user_id,token_hash,created_at,expires_at,revoked_at,last_seen_at)
 SELECT ('f14a0000-0000-4000-8000-'||lpad(n::text,12,'0'))::uuid,'f14a0000-0000-4000-8000-000000000001',
 md5('clock-session-'||n)||md5('clock-session-tail-'||n),'2020-01-01T00:00:00Z','2050-01-01T00:00:00Z',
 CASE WHEN n=12 THEN '2020-03-01T00:00:00Z'::timestamptz END,
 CASE WHEN n=13 THEN '2020-04-01T00:00:00Z'::timestamptz END FROM generate_series(11,13) n;
INSERT INTO password_reset_tokens(id,user_id,token_hash,created_at,expires_at,used_at,revoked_at)
 SELECT ('f14a0000-0000-4000-8000-'||lpad(n::text,12,'0'))::uuid,'f14a0000-0000-4000-8000-000000000001',
 md5('clock-reset-'||n)||md5('clock-reset-tail-'||n),'2020-01-01T00:00:00Z','2050-01-01T00:00:00Z',
 CASE WHEN n=22 THEN '2020-03-01T00:00:00Z'::timestamptz END,
 CASE WHEN n=23 THEN '2020-04-01T00:00:00Z'::timestamptz END FROM generate_series(21,23) n;
INSERT INTO email_verification_tokens(id,user_id,token_hash,created_at,expires_at,used_at,revoked_at)
 SELECT ('f14a0000-0000-4000-8000-'||lpad(n::text,12,'0'))::uuid,'f14a0000-0000-4000-8000-000000000001',
 md5('clock-verification-'||n)||md5('clock-verification-tail-'||n),'2020-01-01T00:00:00Z','2050-01-01T00:00:00Z',
 CASE WHEN n=32 THEN '2020-03-01T00:00:00Z'::timestamptz END,
 CASE WHEN n=33 THEN '2020-04-01T00:00:00Z'::timestamptz END FROM generate_series(31,33) n;
CREATE TABLE identity_lifecycle_clock_upgrade_fixture(table_name text,id uuid,before_state jsonb,expected timestamptz);
INSERT INTO identity_lifecycle_clock_upgrade_fixture
 SELECT 'sessions',id,to_jsonb(s),greatest(created_at,revoked_at,last_seen_at) FROM sessions s WHERE user_id='f14a0000-0000-4000-8000-000000000001'
 UNION ALL
 SELECT 'password_reset_tokens',id,to_jsonb(t),greatest(created_at,used_at,revoked_at) FROM password_reset_tokens t WHERE user_id='f14a0000-0000-4000-8000-000000000001'
 UNION ALL
 SELECT 'email_verification_tokens',id,to_jsonb(t),greatest(created_at,used_at,revoked_at) FROM email_verification_tokens t WHERE user_id='f14a0000-0000-4000-8000-000000000001';
