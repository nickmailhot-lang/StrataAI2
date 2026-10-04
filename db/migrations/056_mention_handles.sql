BEGIN;
-- Global account identity, never an Organization-owned collaboration record.
-- Former aliases remain reserved; only the current handle resolves mentions.
CREATE TABLE mention_handle_reservations (
 handle text COLLATE "C" PRIMARY KEY,
 user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
 created_at timestamptz NOT NULL CHECK(isfinite(created_at)),
 UNIQUE(handle,user_id),
 CHECK(length(handle) BETWEEN 3 AND 40 AND handle ~ '^[a-z][a-z0-9_]*$'
  AND handle NOT IN ('card','board')),
 CHECK(left(handle,2)<>'u_' OR handle='u_'||replace(user_id::text,'-',''))
);
CREATE INDEX ix_mention_handle_reservations_owner ON mention_handle_reservations(user_id);
CREATE TABLE user_mention_handles (
 user_id uuid PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
 handle text COLLATE "C" NOT NULL UNIQUE,
 created_at timestamptz NOT NULL,updated_at timestamptz NOT NULL,
 version bigint NOT NULL DEFAULT 1 CHECK(version>0),
 FOREIGN KEY(handle,user_id) REFERENCES mention_handle_reservations(handle,user_id)
  DEFERRABLE INITIALLY DEFERRED,
 CHECK(isfinite(created_at) AND isfinite(updated_at) AND updated_at>=created_at)
);
-- A single seed statement gives both sides the same finite registry timestamp.
WITH seeded AS (
 INSERT INTO mention_handle_reservations(handle,user_id,created_at)
 SELECT 'u_'||replace(id::text,'-',''),id,statement_timestamp() FROM users
 RETURNING handle,user_id,created_at
)
INSERT INTO user_mention_handles(user_id,handle,created_at,updated_at)
 SELECT user_id,handle,created_at,created_at FROM seeded;

-- Runtime registration can create an account but cannot insert arbitrary aliases.
CREATE FUNCTION seed_user_mention_handle() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
 SET search_path=pg_catalog AS $$
DECLARE handle_value text := 'u_'||replace(NEW.id::text,'-','');
 registry_time timestamptz := clock_timestamp();
BEGIN
 INSERT INTO public.mention_handle_reservations(handle,user_id,created_at)
  VALUES(handle_value,NEW.id,registry_time);
 INSERT INTO public.user_mention_handles(user_id,handle,created_at,updated_at)
  VALUES(NEW.id,handle_value,registry_time,registry_time);
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION seed_user_mention_handle() FROM PUBLIC;
CREATE TRIGGER users_seed_mention_handle AFTER INSERT ON users
 FOR EACH ROW EXECUTE FUNCTION seed_user_mention_handle();

CREATE FUNCTION enforce_mention_handle_reservation() RETURNS trigger LANGUAGE plpgsql
 SET search_path=pg_catalog AS $$
BEGIN
 IF NEW IS DISTINCT FROM OLD THEN
  RAISE EXCEPTION 'Mention handle ownership is immutable' USING ERRCODE='23514';
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION enforce_mention_handle_reservation() FROM PUBLIC;
CREATE TRIGGER mention_handle_reservations_immutable BEFORE UPDATE ON mention_handle_reservations
 FOR EACH ROW EXECUTE FUNCTION enforce_mention_handle_reservation();

-- UPDATE locks the current account row before reserving a new alias. The 32
-- lifetime reservation bound prevents unlimited alias hoarding by one account.
-- The Application command must prove the current session owns this subject;
-- a transaction-local subject alone is not authentication.
CREATE FUNCTION enforce_user_mention_handle_revision() RETURNS trigger LANGUAGE plpgsql SECURITY DEFINER
 SET search_path=pg_catalog AS $$
DECLARE reserved_owner uuid;
BEGIN
 IF NULLIF(current_setting('app.identity_subject',true),'') IS DISTINCT FROM OLD.user_id::text THEN
  RAISE EXCEPTION 'Mention handle subject is unavailable' USING ERRCODE='42501';
 END IF;
 IF NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
 IF NEW.user_id IS DISTINCT FROM OLD.user_id OR NEW.created_at IS DISTINCT FROM OLD.created_at
  OR OLD.version=9223372036854775807 OR NEW.version<>OLD.version+1
  OR NOT isfinite(NEW.updated_at) OR NEW.updated_at<OLD.updated_at
  OR NEW.handle IS NOT DISTINCT FROM OLD.handle THEN
  RAISE EXCEPTION 'Mention handle revision is invalid' USING ERRCODE='23514';
 END IF;
 SELECT user_id INTO reserved_owner FROM public.mention_handle_reservations WHERE handle=NEW.handle;
 IF reserved_owner IS NOT NULL AND reserved_owner<>OLD.user_id THEN
  RAISE EXCEPTION 'Mention handle is unavailable' USING ERRCODE='23505';
 END IF;
 IF reserved_owner IS NULL THEN
  IF (SELECT count(*) FROM public.mention_handle_reservations WHERE user_id=OLD.user_id)>=32 THEN
   RAISE EXCEPTION 'Mention handle reservation limit reached' USING ERRCODE='23514';
  END IF;
  INSERT INTO public.mention_handle_reservations(handle,user_id,created_at)
   VALUES(NEW.handle,OLD.user_id,NEW.updated_at);
 END IF;
 RETURN NEW;
END $$;
REVOKE ALL ON FUNCTION enforce_user_mention_handle_revision() FROM PUBLIC;
CREATE TRIGGER user_mention_handles_revision_guard BEFORE UPDATE ON user_mention_handles
 FOR EACH ROW EXECUTE FUNCTION enforce_user_mention_handle_revision();
INSERT INTO schema_migrations(version) VALUES('056_mention_handles');
COMMIT;
