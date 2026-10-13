\set ON_ERROR_STOP on
\getenv identity_password STRATAAI_IDENTITY_DELIVERY_DB_PASSWORD
\getenv api_password STRATAAI_API_DB_PASSWORD
\getenv worker_password STRATAAI_WORKER_DB_PASSWORD
\getenv admin_password POSTGRES_PASSWORD
SELECT length(:'identity_password') >= 20
    AND :'identity_password' <> :'api_password'
    AND :'identity_password' <> :'worker_password'
    AND :'identity_password' <> :'admin_password' AS password_valid \gset
\if :password_valid
\else
DO $$ BEGIN RAISE EXCEPTION 'A distinct identity mail runtime password of at least 20 characters is required.'; END $$;
\endif

BEGIN;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='strataai_identity_mail_runtime') THEN
        CREATE ROLE strataai_identity_mail_runtime LOGIN NOSUPERUSER NOBYPASSRLS
            NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT;
    END IF;
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname='strataai_identity_mail_runtime'
        AND (rolsuper OR rolbypassrls OR rolcreatedb OR rolcreaterole OR rolreplication))
        OR EXISTS (SELECT 1 FROM pg_auth_members WHERE member='strataai_identity_mail_runtime'::regrole)
        OR has_schema_privilege('strataai_identity_mail_runtime','public','CREATE')
        OR EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public' AND c.relowner='strataai_identity_mail_runtime'::regrole)
        OR EXISTS (SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
            WHERE n.nspname='public' AND p.proowner='strataai_identity_mail_runtime'::regrole)
    THEN RAISE EXCEPTION 'Existing identity mail role has unsafe privileges or ownership; no changes applied.';
    END IF;
END $$;
ALTER ROLE strataai_identity_mail_runtime LOGIN NOSUPERUSER NOBYPASSRLS
    NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT PASSWORD :'identity_password';
REVOKE ALL ON ALL TABLES IN SCHEMA public FROM strataai_identity_mail_runtime;
-- Table-level REVOKE does not remove old column grants. Reset those as well.
DO $$ DECLARE relation record;
BEGIN
    FOR relation IN
        SELECT n.nspname,c.relname,string_agg(format('%I',a.attname),',' ORDER BY a.attnum) AS columns
        FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
        WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','f')
        GROUP BY n.nspname,c.relname
    LOOP
        EXECUTE format('REVOKE SELECT (%s), INSERT (%s), UPDATE (%s), REFERENCES (%s) ON %I.%I FROM strataai_identity_mail_runtime',
            relation.columns,relation.columns,relation.columns,relation.columns,relation.nspname,relation.relname);
    END LOOP;
END $$;
GRANT USAGE ON SCHEMA public TO strataai_identity_mail_runtime;
GRANT EXECUTE ON FUNCTION public.runtime_database_role_is_safe() TO strataai_identity_mail_runtime;
GRANT SELECT ON schema_migrations TO strataai_identity_mail_runtime;
GRANT SELECT,UPDATE ON identity_delivery_jobs TO strataai_identity_mail_runtime;
GRANT SELECT(id,email,status,email_verified) ON users TO strataai_identity_mail_runtime;
GRANT SELECT(id,user_id,token_hash,used_at,revoked_at,expires_at)
    ON password_reset_tokens,email_verification_tokens TO strataai_identity_mail_runtime;
-- Effective checks include PUBLIC grants, which direct role REVOKE cannot remove.
DO $$ DECLARE attribute record; privilege text;
BEGIN
    FOR attribute IN
        SELECT c.oid,c.relname,a.attnum,a.attname
        FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
        WHERE n.nspname='public' AND c.relkind IN ('r','p','v','m','f')
    LOOP
        IF has_table_privilege('strataai_identity_mail_runtime',attribute.oid,'DELETE,TRUNCATE,TRIGGER') THEN
            RAISE EXCEPTION 'Effective identity mail grants exceed delivery boundary; no changes applied.';
        END IF;
        FOREACH privilege IN ARRAY ARRAY['SELECT','INSERT','UPDATE','REFERENCES'] LOOP
            IF has_column_privilege('strataai_identity_mail_runtime',attribute.oid,attribute.attnum,privilege)
                AND NOT (
                    (attribute.relname='identity_delivery_jobs' AND privilege IN ('SELECT','UPDATE'))
                    OR (attribute.relname='schema_migrations' AND privilege='SELECT')
                    OR (attribute.relname='users' AND privilege='SELECT' AND attribute.attname IN ('id','email','status','email_verified'))
                    OR (attribute.relname IN ('password_reset_tokens','email_verification_tokens') AND privilege='SELECT'
                        AND attribute.attname IN ('id','user_id','token_hash','used_at','revoked_at','expires_at'))
                ) THEN
                RAISE EXCEPTION 'Effective identity mail grants exceed delivery boundary; no changes applied.';
            END IF;
        END LOOP;
    END LOOP;
END $$;
COMMIT;
