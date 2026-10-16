import json
import os
from pathlib import Path
import secrets
import shutil
import subprocess
import tempfile
import time
import unittest
import uuid


class IdentityRoleProvisionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.repo = Path(__file__).resolve().parents[1]
        cls.docker = shutil.which('docker')
        if not cls.docker and os.name == 'nt':
            candidate = Path(os.environ.get('LOCALAPPDATA', '')) / 'Programs/DockerDesktop/resources/bin/docker.exe'
            if candidate.is_file():
                cls.docker = str(candidate)
        if not cls.docker:
            raise AssertionError('Docker is required for isolated role provisioning verification')
        cls.temporary = tempfile.TemporaryDirectory(prefix='strataai-mail-role-test-')
        cls.folder = Path(cls.temporary.name).resolve()
        assert cls.folder.parent == Path(tempfile.gettempdir()).resolve()
        assert cls.folder.name.startswith('strataai-mail-role-test-')
        cls.addClassCleanup(cls.cleanup_folder)
        cls.name = 'strataai-mail-role-test-' + uuid.uuid4().hex[:12]
        cls.passwords = {key: secrets.token_hex(24) for key in
                         ('POSTGRES_PASSWORD', 'STRATAAI_API_DB_PASSWORD', 'STRATAAI_WORKER_DB_PASSWORD')}
        cls.mail_password = secrets.token_hex(24) + "';mail"
        server_environment = {'POSTGRES_USER': 'postgres', 'POSTGRES_DB': 'role_fixture', **cls.passwords,
                              'STRATAAI_IDENTITY_DELIVERY_DB_PASSWORD': cls.mail_password}
        cls.addClassCleanup(cls.cleanup_container)
        result = cls.command('run', '-d', '--name', cls.name, '--network', 'none',
                             '--tmpfs', '/var/lib/postgresql/data', '--label', 'strataai.fixture=identity-role-provision',
                             *[argument for key in server_environment for argument in ('--env', key)],
                             'pgvector/pgvector:pg17', environment=server_environment)
        if result.returncode:
            raise AssertionError('Disposable PostgreSQL role fixture could not start')
        for _ in range(80):
            # The image's temporary init server accepts Unix-socket requests
            # before database creation/restart. Require the final TCP server.
            ready = cls.command('exec', cls.name, 'sh', '-c',
                                'PGPASSWORD="$POSTGRES_PASSWORD" psql -X -w -qAt -h 127.0.0.1 '
                                '-U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT 1"')
            if ready.returncode == 0 and ready.stdout.strip() == b'1':
                break
            time.sleep(0.25)
        else:
            raise AssertionError('Disposable PostgreSQL role fixture did not become ready')
        migrations = '\\set ON_ERROR_STOP on\n' + ''.join(
            path.read_text(encoding='utf-8') + '\n' for path in sorted((cls.repo / 'db/migrations').glob('*.sql')))
        if cls.sql(migrations).returncode:
            raise AssertionError('Ordered migrations failed in disposable role fixture')

    @classmethod
    def cleanup_folder(cls):
        root = cls.folder.resolve()
        assert root == cls.folder
        assert root.parent == Path(tempfile.gettempdir()).resolve()
        assert root.name.startswith('strataai-mail-role-test-')
        try:
            assert not list(root.glob('*.env')), 'Fixture credentials must not be materialized in local files'
        finally:
            cls.temporary.cleanup()

    @classmethod
    def command(cls, *arguments, input=None, environment=None):
        # Docker inherits explicitly named values from this child environment;
        # secrets are neither command-line arguments nor temporary file content.
        env = {**os.environ, **environment} if environment is not None else None
        return subprocess.run([cls.docker, *arguments], input=input, env=env, capture_output=True, timeout=90)

    @classmethod
    def cleanup_container(cls):
        inspected = cls.command('inspect', cls.name)
        if inspected.returncode:
            return
        data = json.loads(inspected.stdout)[0]
        assert data['Config']['Labels']['strataai.fixture'] == 'identity-role-provision'
        assert data['Name'] == '/' + cls.name
        cls.command('stop', cls.name)
        if cls.command('rm', '--volumes', cls.name).returncode:
            raise AssertionError('Owned role fixture cleanup failed')

    @classmethod
    def sql(cls, statement, *, runtime=False, mail_password=None, overrides=None):
        environment = {}
        if runtime:
            environment['PGPASSWORD'] = cls.mail_password
            arguments = ['exec', '-i', '--env', 'PGPASSWORD', cls.name, 'psql', '-X', '-qAt',
                         '-v', 'ON_ERROR_STOP=1', '-h', '127.0.0.1', '-U', 'strataai_identity_mail_runtime', '-d', 'role_fixture']
        else:
            arguments = ['exec', '-i']
            if mail_password is not None or overrides:
                values = dict(overrides or {})
                if mail_password is not None:
                    values['STRATAAI_IDENTITY_DELIVERY_DB_PASSWORD'] = mail_password
                environment.update(values)
                arguments += [argument for key in values for argument in ('--env', key)]
            arguments += [cls.name, 'psql', '-X', '-qAt', '-v', 'ON_ERROR_STOP=1', '-U', 'postgres', '-d', 'role_fixture']
        return cls.command(*arguments, input=statement.encode(), environment=environment)

    def provision(self, password=None):
        return self.sql((self.repo / 'db/provision-identity-mail-role.sql').read_text(), mail_password=password)

    def test_reprovisioning_preserves_restricted_delivery_and_refuses_unsafe_roles(self):
        general = (self.repo / 'db/provision-runtime-roles.sql').read_text()
        for override in ({'STRATAAI_API_DB_PASSWORD': 'short'}, {'STRATAAI_WORKER_DB_PASSWORD': 'short'},
                         {'STRATAAI_WORKER_DB_PASSWORD': self.passwords['STRATAAI_API_DB_PASSWORD']}):
            self.assertNotEqual(self.sql(general, overrides=override).returncode, 0)
            self.assertEqual(self.sql("SELECT count(*) FROM pg_roles WHERE rolname IN ('strataai_api_runtime','strataai_worker_runtime');").stdout.strip(), b'0')
        self.assertEqual(self.sql(general).returncode, 0, 'General runtime role provision failed')
        # PRD-01 FOUND-FR-009: retained receipts cannot acquire a mutable column
        # path, including after reprovisioning an existing runtime identity.
        receipts = ('identity_revocation_replays', 'identity_login_replays',
                    'identity_registration_replays', 'identity_recovery_request_replays',
                    'identity_token_consumption_replays')
        for role in ('strataai_api_runtime', 'strataai_worker_runtime'):
            for table in receipts:
                self.assertEqual(self.sql(f'GRANT UPDATE(created_at) ON {table} TO {role};').returncode, 0)
                self.assertEqual(self.sql(f"SELECT has_table_privilege('{role}','{table}','UPDATE'),"
                                          f"has_any_column_privilege('{role}','{table}','UPDATE');").stdout.strip(), b'f|t',
                                 'Column-grant fixture did not exercise the table-level blind spot')
        self.assertEqual(self.sql(general).returncode, 0, 'Runtime reprovisioning failed after column privilege drift')
        for role in ('strataai_api_runtime', 'strataai_worker_runtime'):
            for table in receipts:
                self.assertEqual(self.sql(f"SELECT has_any_column_privilege('{role}','{table}','UPDATE');").stdout.strip(), b'f',
                                 'Runtime reprovisioning retained a receipt UPDATE path')
        # ARCH-04/12: effective permissions include PUBLIC, which resetting the
        # managed roles cannot remove. Refuse that deployment atomically.
        fingerprint = """SELECT md5(jsonb_build_object(
          'roles',(SELECT jsonb_agg(to_jsonb(r) ORDER BY rolname) FROM pg_authid r
            WHERE rolname IN ('strataai_api_runtime','strataai_worker_runtime')),
          'relations',(SELECT jsonb_agg(jsonb_build_object('oid',c.oid,'owner',c.relowner,'acl',c.relacl) ORDER BY c.oid)
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public'),
          'columns',(SELECT jsonb_agg(jsonb_build_object('table',a.attrelid,'column',a.attnum,'acl',a.attacl)
            ORDER BY a.attrelid,a.attnum) FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid
            JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname='public' AND a.attnum>0 AND NOT a.attisdropped),
          'functions',(SELECT jsonb_agg(jsonb_build_object('oid',p.oid,'acl',p.proacl) ORDER BY p.oid)
            FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname='public'),
          'memberships',(SELECT jsonb_agg(to_jsonb(m) ORDER BY m.roleid,m.member,m.grantor)
            FROM pg_auth_members m WHERE member IN (SELECT oid FROM pg_roles
              WHERE rolname IN ('strataai_api_runtime','strataai_worker_runtime')))
          )::text);"""
        for privilege, table, column, role in (
            ('SELECT', 'users', 'password_hash', 'strataai_worker_runtime'),
            ('UPDATE', 'attachment_preview_publications', 'published_at', 'strataai_api_runtime'),
            ('INSERT', 'attachment_previews', 'created_at', 'strataai_api_runtime'),
            ('UPDATE', 'board_star_events', 'created_at', 'strataai_worker_runtime'),
            ('REFERENCES', 'users', 'id', 'strataai_worker_runtime'),
            ('TRUNCATE', 'board_star_events', None, 'strataai_api_runtime'),
            ('DELETE', 'attachments', None, 'strataai_api_runtime'),
            ('TRIGGER', 'user_board_preferences', None, 'strataai_worker_runtime'),
            ('MAINTAIN', 'attachments', None, 'strataai_worker_runtime'),
        ):
            with self.subTest(public_capability=(privilege, table, column)):
                column_sql = f'({column})' if column else ''
                check = (f"has_column_privilege('{role}','{table}','{column}','{privilege}')" if column
                         else f"has_table_privilege('{role}','{table}','{privilege}')")
                self.assertEqual(self.sql(f'SELECT {check};').stdout.strip(), b'f',
                                 'Drift fixture must start outside the canonical role boundary')
                self.assertEqual(self.sql(f'GRANT {privilege}{column_sql} ON {table} TO PUBLIC;').returncode, 0)
                try:
                    self.assertEqual(self.sql(f'SELECT {check};').stdout.strip(), b't',
                                     'PUBLIC fixture did not establish the unexpected effective capability')
                    before = self.sql(fingerprint).stdout
                    result = self.sql(general, overrides={'STRATAAI_API_DB_PASSWORD': secrets.token_hex(24),
                                                         'STRATAAI_WORKER_DB_PASSWORD': secrets.token_hex(24)})
                    self.assertNotEqual(result.returncode, 0, 'General provisioning accepted effective PUBLIC privilege expansion')
                    self.assertEqual(self.sql(fingerprint).stdout, before,
                                     'Refused general provisioning changed credentials or relation/column capabilities')
                finally:
                    self.assertEqual(self.sql(f'REVOKE {privilege}{column_sql} ON {table} FROM PUBLIC;').returncode, 0)
                    self.assertEqual(self.sql(general).returncode, 0, 'Canonical provision did not recover after PUBLIC drift was removed')
        self.assertEqual(self.sql('GRANT SELECT ON schema_migrations TO PUBLIC;').returncode, 0)
        try:
            self.assertEqual(self.sql(general).returncode, 0,
                             'Redundant PUBLIC access already present in both canonical role grants must remain admissible')
        finally:
            self.assertEqual(self.sql('REVOKE SELECT ON schema_migrations FROM PUBLIC;').returncode, 0)
        # NOINHERIT alone does not prevent SET ROLE into a separately granted peer.
        self.assertEqual(self.sql('CREATE ROLE runtime_provision_peer NOLOGIN; '
                                 'GRANT SELECT(password_hash) ON users TO runtime_provision_peer; '
                                 'GRANT runtime_provision_peer TO strataai_worker_runtime;').returncode, 0)
        try:
            self.assertEqual(self.sql("SELECT has_column_privilege('strataai_worker_runtime','users','password_hash','SELECT');").stdout.strip(), b'f')
            peer = self.command('exec', '-i', '--env', 'PGPASSWORD', self.name, 'psql', '-X', '-qAt',
                                '-v', 'ON_ERROR_STOP=1', '-h', '127.0.0.1', '-U', 'strataai_worker_runtime', '-d', 'role_fixture',
                                input=b"SET ROLE runtime_provision_peer; SELECT has_column_privilege(current_user,'users','password_hash','SELECT'); RESET ROLE;",
                                environment={'PGPASSWORD': self.passwords['STRATAAI_WORKER_DB_PASSWORD']})
            self.assertEqual(peer.returncode, 0, 'Owned peer fixture did not exercise actual SET ROLE')
            self.assertEqual(peer.stdout.strip(), b't')
            before = self.sql(fingerprint).stdout
            result = self.sql(general, overrides={'STRATAAI_API_DB_PASSWORD': secrets.token_hex(24),
                                                 'STRATAAI_WORKER_DB_PASSWORD': secrets.token_hex(24)})
            self.assertNotEqual(result.returncode, 0, 'General provisioning accepted a SET ROLE escape from its direct capability boundary')
            self.assertEqual(self.sql(fingerprint).stdout, before, 'Refused peer-role provisioning changed protected state')
        finally:
            self.assertEqual(self.sql('REVOKE runtime_provision_peer FROM strataai_worker_runtime; '
                                     'REVOKE SELECT(password_hash) ON users FROM runtime_provision_peer; DROP ROLE runtime_provision_peer;').returncode, 0)
            self.assertEqual(self.sql(general).returncode, 0)
        # Ownership retains implicit grant options even after ordinary grants
        # are revoked. This exercises that separate postcondition directly.
        self.assertEqual(self.sql('CREATE TABLE runtime_provision_owned(id integer); '
                                 'ALTER TABLE runtime_provision_owned OWNER TO strataai_api_runtime;').returncode, 0)
        try:
            option = self.sql("BEGIN; REVOKE ALL ON runtime_provision_owned FROM strataai_api_runtime; "
                              "SELECT has_table_privilege('strataai_api_runtime','runtime_provision_owned','SELECT'),"
                              "has_table_privilege('strataai_api_runtime','runtime_provision_owned','SELECT WITH GRANT OPTION'); ROLLBACK;")
            self.assertEqual(option.returncode, 0)
            self.assertEqual(option.stdout.strip(), b'f|t', 'Ownership fixture must isolate implicit grant options')
            before = self.sql(fingerprint).stdout
            result = self.sql(general, overrides={'STRATAAI_API_DB_PASSWORD': secrets.token_hex(24),
                                                 'STRATAAI_WORKER_DB_PASSWORD': secrets.token_hex(24)})
            self.assertNotEqual(result.returncode, 0, 'General provisioning accepted implicit owner grant options')
            self.assertEqual(self.sql(fingerprint).stdout, before, 'Refused ownership configuration changed protected state')
        finally:
            self.assertEqual(self.sql('DROP TABLE runtime_provision_owned;').returncode, 0)
            self.assertEqual(self.sql(general).returncode, 0)
        for password in ('', 'short', *self.passwords.values()):
            with self.subTest(refused_password=True):
                self.assertNotEqual(self.provision(password).returncode, 0)
                self.assertEqual(self.sql("SELECT count(*) FROM pg_roles WHERE rolname='strataai_identity_mail_runtime';").stdout.strip(), b'0')
        self.assertEqual(self.provision().returncode, 0, 'Valid isolated mail role provision failed')
        self.assertEqual(self.provision().returncode, 0, 'Repeated provision failed')
        self.assertEqual(self.sql('SELECT runtime_database_role_is_safe();', runtime=True).stdout.strip(), b't')
        # Seed only this disposable database; runtime role must claim with service RLS.
        seed = """
INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
VALUES ('44444444-4444-4444-4444-444444444444','identity@example.test','IDENTITY@EXAMPLE.TEST','Role fixture','not-a-real-hash','ACTIVE',true,now(),now());
INSERT INTO password_reset_tokens(id,user_id,token_hash,created_at,expires_at)
VALUES ('66666666-6666-6666-6666-666666666666','44444444-4444-4444-4444-444444444444',repeat('a',64),now(),now()+interval '30 minutes');
INSERT INTO identity_delivery_jobs(id,user_id,purpose,password_reset_token_id,key_id,correlation_id,recipient_email,sender_address,public_origin,provider_account,created_at,expires_at)
VALUES ('66666666-6666-6666-6666-666666666666','44444444-4444-4444-4444-444444444444','RESET_PASSWORD','66666666-6666-6666-6666-666666666666','fixture','role-fixture','identity@example.test','sender@example.test','https://app.example.test','fixture',now(),now()+interval '30 minutes');
"""
        self.assertEqual(self.sql(seed).returncode, 0, 'Disposable identity fixtures failed')
        contract = """
BEGIN;
DO $$ BEGIN
 IF EXISTS (SELECT FROM identity_delivery_jobs) THEN RAISE EXCEPTION 'service RLS was bypassed'; END IF;
 IF has_column_privilege(current_user,'users','password_hash','SELECT') OR has_table_privilege(current_user,'boards','SELECT')
    OR has_table_privilege(current_user,'organizations','SELECT') OR has_table_privilege(current_user,'users','UPDATE')
 THEN RAISE EXCEPTION 'mail role has domain or credential grants'; END IF;
END $$;
SELECT set_config('app.service_scope','GLOBAL_IDENTITY_MAIL',true);
DO $$ DECLARE job identity_delivery_jobs; BEGIN
 SELECT * INTO STRICT job FROM claim_identity_delivery(gen_random_uuid());
 PERFORM id,email,status,email_verified FROM users;
 PERFORM id,user_id,token_hash,used_at,revoked_at,expires_at FROM password_reset_tokens;
 PERFORM id,user_id,token_hash,used_at,revoked_at,expires_at FROM email_verification_tokens;
 IF NOT finish_identity_delivery(job.id,job.lease_id,job.worker_id,'SENT',NULL,gen_random_uuid()) THEN RAISE EXCEPTION 'mail completion refused'; END IF;
END $$;
COMMIT;
"""
        self.assertEqual(self.sql(contract, runtime=True).returncode, 0, 'Restricted mail delivery contract failed')
        self.assertEqual(self.sql('GRANT SELECT(password_hash) ON users TO strataai_identity_mail_runtime;').returncode, 0)
        self.assertEqual(self.provision().returncode, 0)
        self.assertEqual(self.sql("SELECT has_column_privilege(current_user,'users','password_hash','SELECT');", runtime=True).stdout.strip(), b'f')
        for grant, revoke in (
            ('GRANT SELECT(password_hash) ON users TO PUBLIC;', 'REVOKE SELECT(password_hash) ON users FROM PUBLIC;'),
            ('GRANT SELECT ON boards TO PUBLIC;', 'REVOKE SELECT ON boards FROM PUBLIC;'),
        ):
            before = self.sql("SELECT md5(rolpassword) FROM pg_authid WHERE rolname='strataai_identity_mail_runtime';").stdout
            self.assertEqual(self.sql(grant).returncode, 0)
            self.assertNotEqual(self.provision(secrets.token_hex(24)).returncode, 0)
            self.assertEqual(self.sql("SELECT md5(rolpassword) FROM pg_authid WHERE rolname='strataai_identity_mail_runtime';").stdout, before)
            self.assertEqual(self.sql(revoke).returncode, 0)
        for broaden in ('CREATE ROLE mail_provision_peer NOLOGIN; GRANT mail_provision_peer TO strataai_identity_mail_runtime;',
                        'ALTER ROLE strataai_identity_mail_runtime CREATEDB;',
                        'CREATE TABLE mail_provision_owned(id integer); ALTER TABLE mail_provision_owned OWNER TO strataai_identity_mail_runtime;'):
            before = self.sql("SELECT md5(rolpassword) FROM pg_authid WHERE rolname='strataai_identity_mail_runtime';").stdout
            self.assertEqual(self.sql(broaden).returncode, 0)
            self.assertNotEqual(self.provision(secrets.token_hex(24)).returncode, 0)
            self.assertEqual(self.sql("SELECT md5(rolpassword) FROM pg_authid WHERE rolname='strataai_identity_mail_runtime';").stdout, before,
                             'Refused unsafe role changed its credential')
            self.assertEqual(self.sql("DO $$ BEGIN IF EXISTS (SELECT FROM pg_roles WHERE rolname='mail_provision_peer') THEN REVOKE mail_provision_peer FROM strataai_identity_mail_runtime; DROP ROLE mail_provision_peer; END IF; END $$; ALTER ROLE strataai_identity_mail_runtime NOCREATEDB; ALTER TABLE IF EXISTS mail_provision_owned OWNER TO postgres; DROP TABLE IF EXISTS mail_provision_owned;").returncode, 0)


if __name__ == '__main__':
    unittest.main()
