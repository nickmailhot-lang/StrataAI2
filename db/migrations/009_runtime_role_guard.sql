BEGIN;
CREATE FUNCTION public.runtime_database_role_is_safe()
RETURNS boolean LANGUAGE sql STABLE SECURITY INVOKER SET search_path=pg_catalog AS $$
    SELECT NOT EXISTS (
        SELECT 1 FROM pg_roles r
        WHERE (r.rolname=current_user OR pg_has_role(session_user,r.oid,'MEMBER'))
          AND (r.rolsuper OR r.rolbypassrls OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication OR left(r.rolname,3)='pg_')
    ) AND NOT has_schema_privilege(current_user,'public','CREATE')
      AND NOT EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname='public' AND pg_has_role(session_user,c.relowner,'MEMBER')
    ) AND NOT EXISTS (
        SELECT 1 FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace
        WHERE n.nspname='public' AND pg_has_role(session_user,p.proowner,'MEMBER')
    );
$$;
REVOKE ALL ON FUNCTION public.runtime_database_role_is_safe() FROM PUBLIC;
INSERT INTO schema_migrations(version) VALUES ('009_runtime_role_guard') ON CONFLICT(version) DO NOTHING;
COMMIT;
