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


class ListMoveParentIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.repo = Path(__file__).resolve().parents[1]
        cls.docker = shutil.which('docker')
        if not cls.docker and os.name == 'nt':
            candidate = Path(os.environ.get('LOCALAPPDATA', '')) / 'Programs/DockerDesktop/resources/bin/docker.exe'
            if candidate.is_file():
                cls.docker = str(candidate)
        if not cls.docker:
            raise AssertionError('Docker is required for real List parent-integrity verification')
        if shutil.disk_usage(cls.repo).free < 5 * 1024**3:
            raise AssertionError('Disposable PostgreSQL requires at least 5 GiB free')
        cls.temporary = tempfile.TemporaryDirectory(prefix='strataai-list-move-')
        cls.folder = Path(cls.temporary.name).resolve()
        assert cls.folder.parent == Path(tempfile.gettempdir()).resolve()
        cls.addClassCleanup(cls.cleanup_folder)
        cls.name = 'strataai-list-move-' + uuid.uuid4().hex[:12]
        cls.role_password = secrets.token_hex(24)
        server_environment = {'POSTGRES_USER': 'postgres', 'POSTGRES_DB': 'list_move',
                              'POSTGRES_PASSWORD': secrets.token_hex(24), 'STRATAAI_API_DB_PASSWORD': cls.role_password,
                              'STRATAAI_WORKER_DB_PASSWORD': secrets.token_hex(24)}
        cls.addClassCleanup(cls.cleanup_container)
        started = cls.command('run', '-d', '--name', cls.name, '--network', 'none',
                              '--tmpfs', '/var/lib/postgresql/data', '--label', 'strataai.fixture=list-move-parent',
                              *[argument for key in server_environment for argument in ('--env', key)],
                              'pgvector/pgvector:pg17', environment=server_environment)
        if started.returncode:
            raise AssertionError('Disposable PostgreSQL List fixture could not start')
        for _ in range(80):
            ready = cls.command('exec', cls.name, 'sh', '-c',
                                'PGPASSWORD="$POSTGRES_PASSWORD" psql -X -w -qAt -h 127.0.0.1 '
                                '-U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT 1"')
            if ready.returncode == 0 and ready.stdout.strip() == b'1':
                break
            time.sleep(0.25)
        else:
            raise AssertionError('Disposable PostgreSQL did not become ready')
        paths = sorted((cls.repo / 'db/migrations').glob('*.sql'))
        upgrade = '147_list_board_move_parent_integrity.sql'
        migrations = ''.join(p.read_text(encoding='utf-8') + '\n' for p in paths if p.name < upgrade)
        if cls.sql(migrations).returncode:
            raise AssertionError('Ordered migrations failed in List fixture')
        old_constraint = cls.sql("SELECT NOT condeferrable AND NOT condeferred AND convalidated FROM pg_constraint WHERE conrelid='cards'::regclass AND conname='cards_list_id_board_id_tenant_id_fkey';")
        if old_constraint.stdout.strip() != b't':
            raise AssertionError('Expected validated immediate parent constraint before upgrade')
        seed = """
INSERT INTO users(id,email,email_normalized,display_name,password_hash,status,email_verified,created_at,updated_at)
VALUES ('14700000-0000-0000-0000-000000000099','list-move@example.test','LIST-MOVE@EXAMPLE.TEST','Fixture','not-a-real-hash','ACTIVE',true,now(),now());
INSERT INTO organizations(id,name,created_at,updated_at) VALUES
 ('14700000-0000-0000-0000-000000000001','A',now(),now()),
 ('14700000-0000-0000-0000-000000000002','B',now(),now());
INSERT INTO boards(id,tenant_id,name,created_at,updated_at) VALUES
 ('14700000-0000-0000-0000-000000000011','14700000-0000-0000-0000-000000000001','Source',now(),now()),
 ('14700000-0000-0000-0000-000000000012','14700000-0000-0000-0000-000000000001','Destination',now(),now()),
 ('14700000-0000-0000-0000-000000000013','14700000-0000-0000-0000-000000000002','Other tenant',now(),now());
INSERT INTO board_lists(id,tenant_id,board_id,name,rank,created_at,updated_at) VALUES
 ('14700000-0000-0000-0000-000000000021','14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000011','Source List',repeat('5',30),now(),now()),
 ('14700000-0000-0000-0000-000000000022','14700000-0000-0000-0000-000000000002','14700000-0000-0000-0000-000000000013','Other List',repeat('5',30),now(),now());
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,lifecycle_state,created_at,updated_at,archived_at,deleted_at,deleted_by)
SELECT ('14700000-0000-0000-0000-00000000003'||n)::uuid,'14700000-0000-0000-0000-000000000001',
 '14700000-0000-0000-0000-000000000011','14700000-0000-0000-0000-000000000021','Card '||n,repeat(n::text,30),
 CASE n WHEN 1 THEN 'ACTIVE' WHEN 2 THEN 'ARCHIVED' ELSE 'DELETED' END,now(),now(),
 CASE WHEN n>1 THEN now() END,CASE WHEN n=3 THEN now() END,
 CASE WHEN n=3 THEN '14700000-0000-0000-0000-000000000099'::uuid END FROM generate_series(1,3) n;
INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES
 ('14700000-0000-0000-0000-000000000034','14700000-0000-0000-0000-000000000002','14700000-0000-0000-0000-000000000013','14700000-0000-0000-0000-000000000022','Other',repeat('5',30),now(),now());
"""
        if cls.sql(seed).returncode:
            raise AssertionError('Synthetic List graph seed failed')
        graph = "SELECT md5(jsonb_build_object('lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id IN ('14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000002')),'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c))::text);"
        before_upgrade = cls.sql(graph).stdout
        if cls.sql((cls.repo / 'db/migrations' / upgrade).read_text()).returncode:
            raise AssertionError('List parent deferral upgrade failed')
        if cls.sql((cls.repo / 'scripts/ci/configuration-list-history-before-upgrade.sql').read_text()).returncode:
            raise AssertionError('Real historical configuration source fixture failed')
        cls.history_hash = cls.sql(cls.configuration_hash_sql()).stdout
        graph_before_history_upgrade = cls.sql(cls.complete_graph_hash_sql()).stdout
        remaining = ''.join(p.read_text(encoding='utf-8') + '\n' for p in paths if p.name > upgrade)
        if cls.sql(remaining).returncode:
            raise AssertionError('List parent-integrity upgrade failed')
        if cls.sql(graph).stdout != before_upgrade:
            raise AssertionError('Upgrade changed existing List/Card data')
        if cls.sql(cls.complete_graph_hash_sql()).stdout != graph_before_history_upgrade:
            raise AssertionError('Historical identity upgrade changed the complete seeded graph')
        if cls.sql((cls.repo / 'db/provision-runtime-roles.sql').read_text(encoding='utf-8')).returncode:
            raise AssertionError('Restricted runtime role provision failed')

    @classmethod
    def command(cls, *arguments, input=None, environment=None):
        # Pass secrets only in the Docker child's environment, never argv/files.
        env = {**os.environ, **environment} if environment is not None else None
        return subprocess.run([cls.docker, *arguments], input=input, env=env, capture_output=True, timeout=90)

    @staticmethod
    def complete_graph_hash_sql():
        return "SELECT md5(jsonb_build_object('lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l),'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c))::text);"

    @staticmethod
    def configuration_hash_sql():
        return """SELECT md5(jsonb_build_object(
          'current',(SELECT jsonb_agg(to_jsonb(c) ORDER BY tenant_id) FROM organization_configurations c),
          'history',(SELECT jsonb_agg(to_jsonb(h) ORDER BY tenant_id,version) FROM organization_configuration_history h),
          'receipts',(SELECT jsonb_agg(to_jsonb(r) ORDER BY tenant_id,version) FROM organization_configuration_receipts r),
          'audit',(SELECT jsonb_agg(to_jsonb(a) ORDER BY id) FROM audit_events a),
          'events',(SELECT jsonb_agg(to_jsonb(e) ORDER BY tenant_id,version) FROM organization_configuration_events e))::text);"""

    def test_configuration_history_preserves_original_parent_without_pinning_former_intake(self):
        self.assertEqual(self.sql(self.configuration_hash_sql()).stdout, self.history_hash,
                         'Upgrade must preserve complete configuration history, current source, receipts and audit/events')
        schema = self.sql((self.repo / 'scripts/ci/assert-organization-configuration-schema.sql').read_text())
        self.assertEqual(schema.returncode, 0, 'Installed configuration keys, RLS and restricted capabilities must pass')
        prefix = "BEGIN; SET LOCAL app.tenant_id='14800000-0000-0000-0000-000000000001';\n"
        parent = "UPDATE board_lists SET board_id='14800000-0000-0000-0000-000000000012',version=version+1,updated_at=now() WHERE id='14800000-0000-0000-0000-0000000000{}';"
        before_current_refusal = self.sql(self.complete_graph_hash_sql()).stdout
        current = self.sql(prefix + parent.format('22') + ' COMMIT;', runtime=True)
        self.assertNotEqual(current.returncode, 0)
        self.assertIn(b'23503', current.stderr, 'Current intake binding must still reject reparenting')
        self.assertEqual(self.sql(self.complete_graph_hash_sql()).stdout, before_current_refusal)
        moved = self.sql(prefix + parent.format('21') + ' COMMIT;', runtime=True)
        self.assertEqual(moved.returncode, 0, 'Former intake List should move through the restricted runtime login')
        preserved = self.sql("""SELECT
          (SELECT board_id='14800000-0000-0000-0000-000000000012' AND version=2 FROM board_lists WHERE id='14800000-0000-0000-0000-000000000021')
          AND (SELECT intake_board_id='14800000-0000-0000-0000-000000000011' AND intake_list_id='14800000-0000-0000-0000-000000000021'
            FROM organization_configuration_history WHERE tenant_id='14800000-0000-0000-0000-000000000001' AND version=1);""")
        self.assertEqual(preserved.stdout.strip(), b't')
        self.assertEqual(self.sql(self.configuration_hash_sql()).stdout, self.history_hash)
        # Manufacturing a revision with a mismatched live parent remains denied.
        forged = self.sql(prefix + """UPDATE organization_configurations SET version=3,
          record_json=record_json || jsonb_build_object('Version',3,'UpdatedAt',now(),
            'Configuration',(record_json->'Configuration') || jsonb_build_object('IntakeBoardId','14800000-0000-0000-0000-000000000012'));
          COMMIT;""", runtime=True)
        self.assertNotEqual(forged.returncode, 0)
        self.assertIn(b'23514', forged.stderr)
        history = self.sql(prefix + """INSERT INTO organization_configuration_history(tenant_id,version,record_json)
          SELECT tenant_id,3,record_json || jsonb_build_object('Version',3,'EventId',gen_random_uuid())
          FROM organization_configurations; COMMIT;""", runtime=True)
        self.assertNotEqual(history.returncode, 0)
        self.assertIn(b'23514', history.stderr, 'History must still match its exact current configuration source')
        mutation = self.sql(prefix + 'UPDATE organization_configuration_history SET record_json=record_json; COMMIT;', runtime=True)
        self.assertNotEqual(mutation.returncode, 0)
        self.assertIn(b'42501', mutation.stderr, 'Runtime cannot rewrite immutable history')
        missing = self.sql('SELECT count(*) FROM organization_configuration_history;', runtime=True)
        self.assertEqual(missing.stdout.strip(), b'0')
        other = self.sql("BEGIN; SET LOCAL app.tenant_id='14700000-0000-0000-0000-000000000001'; SELECT count(*) FROM organization_configuration_history; ROLLBACK;", runtime=True)
        self.assertEqual(other.stdout.strip(), b'0', 'Another tenant must not see historical configuration sources')
        self.assertEqual(self.sql(self.configuration_hash_sql()).stdout, self.history_hash,
                         'All refusals preserve complete configuration sources')

    @classmethod
    def sql(cls, statement, runtime=False):
        args = ['exec', '-i']
        if runtime:
            args += ['--env', 'PGPASSWORD']
        args += [cls.name, 'psql', '-X', '-w', '-qAt', '-v', 'ON_ERROR_STOP=1', '-v', 'VERBOSITY=sqlstate']
        if runtime:
            args += ['-h', '127.0.0.1']
        args += ['-U', 'strataai_api_runtime' if runtime else 'postgres', '-d', 'list_move']
        return cls.command(*args, input=statement.encode(), environment={'PGPASSWORD': cls.role_password} if runtime else None)

    @classmethod
    def cleanup_container(cls):
        inspected = cls.command('inspect', cls.name)
        if inspected.returncode:
            return
        data = json.loads(inspected.stdout)[0]
        assert data['Name'] == '/' + cls.name and data['Config']['Labels']['strataai.fixture'] == 'list-move-parent'
        cls.command('stop', cls.name)
        if cls.command('rm', '--volumes', cls.name).returncode:
            raise AssertionError('Owned List fixture cleanup failed')

    @classmethod
    def cleanup_folder(cls):
        assert cls.folder.resolve() == cls.folder and cls.folder.parent == Path(tempfile.gettempdir()).resolve()
        assert cls.folder.name.startswith('strataai-list-move-')
        try:
            assert not list(cls.folder.glob('*.env')), 'Fixture credentials must not be materialized in local files'
        finally:
            cls.temporary.cleanup()

    def test_real_commit_preserves_all_lifecycles_and_rejects_incomplete_or_cross_tenant_graphs(self):
        constraint = self.sql("SELECT condeferrable AND NOT condeferred AND convalidated FROM pg_constraint WHERE conrelid='cards'::regclass AND conname='cards_list_id_board_id_tenant_id_fkey';")
        self.assertEqual(constraint.stdout.strip(), b't')
        forced = self.sql("SELECT count(*)=3 AND bool_and(relrowsecurity AND relforcerowsecurity) FROM pg_class WHERE oid IN ('boards'::regclass,'board_lists'::regclass,'cards'::regclass);")
        self.assertEqual(forced.stdout.strip(), b't')
        safe_role = self.sql('SELECT NOT rolsuper AND NOT rolbypassrls FROM pg_roles WHERE rolname=current_user;', runtime=True)
        self.assertEqual(safe_role.stdout.strip(), b't')
        prefix = "BEGIN; SET LOCAL app.tenant_id='14700000-0000-0000-0000-000000000001';\n"
        defer = 'SET CONSTRAINTS cards_list_id_board_id_tenant_id_fkey DEFERRED;\n'
        move = "UPDATE board_lists SET board_id='14700000-0000-0000-0000-000000000012',version=version+1,updated_at=now() WHERE id='14700000-0000-0000-0000-000000000021';\n"
        before = self.sql("SELECT md5(jsonb_build_object('lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id IN ('14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000002')),'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c))::text);").stdout
        for statements in (prefix + move + 'COMMIT;', prefix + defer + move + 'COMMIT;'):
            denied = self.sql(statements, runtime=True)
            self.assertNotEqual(denied.returncode, 0)
            self.assertIn(b'23503', denied.stderr, 'Incomplete graph must fail its parent constraint')
            after = self.sql("SELECT md5(jsonb_build_object('lists',(SELECT jsonb_agg(to_jsonb(l) ORDER BY id) FROM board_lists l WHERE tenant_id IN ('14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000002')),'cards',(SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM cards c))::text);").stdout
            self.assertEqual(after, before, 'Failed statement/commit must preserve complete graph')
        cross = self.sql(prefix + defer + "UPDATE board_lists SET board_id='14700000-0000-0000-0000-000000000013' WHERE id='14700000-0000-0000-0000-000000000021'; COMMIT;", runtime=True)
        self.assertNotEqual(cross.returncode, 0)
        self.assertIn(b'23503', cross.stderr)
        scope = self.sql(prefix + "SELECT count(*) FROM cards; ROLLBACK;", runtime=True)
        self.assertEqual(scope.stdout.strip(), b'3')
        missing = self.sql('SELECT count(*) FROM cards;', runtime=True)
        self.assertEqual(missing.stdout.strip(), b'0')
        forbidden = self.sql(prefix + "INSERT INTO cards(id,tenant_id,board_id,list_id,title,rank,created_at,updated_at) VALUES (gen_random_uuid(),'14700000-0000-0000-0000-000000000002','14700000-0000-0000-0000-000000000013','14700000-0000-0000-0000-000000000022','Forbidden',repeat('6',30),now(),now()); COMMIT;", runtime=True)
        self.assertNotEqual(forbidden.returncode, 0)
        self.assertIn(b'42501', forbidden.stderr, 'Deferral cannot admit another tenant write')
        stable_fields = "SELECT md5(jsonb_build_object('lists',(SELECT jsonb_agg(to_jsonb(l)-'board_id'-'version'-'updated_at' ORDER BY id) FROM board_lists l WHERE tenant_id IN ('14700000-0000-0000-0000-000000000001','14700000-0000-0000-0000-000000000002')),'cards',(SELECT jsonb_agg(to_jsonb(c)-'board_id'-'version'-'updated_at' ORDER BY id) FROM cards c))::text);"
        stable_before = self.sql(stable_fields).stdout
        complete = self.sql(prefix + defer + move + "UPDATE cards SET board_id='14700000-0000-0000-0000-000000000012',version=version+1,updated_at=now() WHERE list_id='14700000-0000-0000-0000-000000000021'; COMMIT;", runtime=True)
        self.assertEqual(complete.returncode, 0, 'Complete deferred parent transition must commit')
        persisted = self.sql("SELECT count(*)=3 AND count(DISTINCT lifecycle_state)=3 AND bool_and(version=2) AND bool_and(board_id='14700000-0000-0000-0000-000000000012') FROM cards WHERE tenant_id='14700000-0000-0000-0000-000000000001';")
        self.assertEqual(persisted.stdout.strip(), b't')
        parent = self.sql("SELECT board_id='14700000-0000-0000-0000-000000000012' AND version=2 FROM board_lists WHERE id='14700000-0000-0000-0000-000000000021';")
        self.assertEqual(parent.stdout.strip(), b't')
        self.assertEqual(self.sql(stable_fields).stdout, stable_before, 'Identity, content, rank, lifecycle and creation history must persist')
        immediate_again = self.sql(prefix + "UPDATE cards SET board_id='14700000-0000-0000-0000-000000000011' WHERE id='14700000-0000-0000-0000-000000000031'; COMMIT;", runtime=True)
        self.assertNotEqual(immediate_again.returncode, 0)
        self.assertIn(b'23503', immediate_again.stderr, 'Committed deferral must not alter later statement defaults')
        other = self.sql("SELECT board_id='14700000-0000-0000-0000-000000000013' AND version=1 FROM cards WHERE id='14700000-0000-0000-0000-000000000034';")
        self.assertEqual(other.stdout.strip(), b't')
