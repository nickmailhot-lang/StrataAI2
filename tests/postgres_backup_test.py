import os
from pathlib import Path
import shlex
import shutil
import subprocess
import tempfile
import unittest


class PostgresBackupTests(unittest.TestCase):
    @staticmethod
    def remove_owned(path, parent, prefix):
        path=Path(path)
        assert path.resolve().parent == Path(parent).resolve() and path.name.startswith(prefix)
        assert not path.is_symlink()
        shutil.rmtree(path)

    @classmethod
    def setUpClass(cls):
        cls.root = Path(tempfile.mkdtemp(prefix='strataai-backup-command-'))
        cls.bash = 'C:/Program Files/Git/bin/bash.exe' if os.name == 'nt' else '/bin/bash'
        cls.repo = Path(__file__).resolve().parents[1]
        cls.script = cls.repo / 'scripts/backup-postgres.sh'
        command = 'MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj /CN=backup-command-fixture -keyout fixture.key -out fixture.crt'
        result = subprocess.run([cls.bash, '-c', command], cwd=cls.root, capture_output=True, timeout=20)
        if result.returncode:
            cls.remove_owned(cls.root,tempfile.gettempdir(),'strataai-backup-command-')
            raise RuntimeError('Backup fixture certificate generation failed')

    @classmethod
    def tearDownClass(cls):
        cls.remove_owned(cls.root,tempfile.gettempdir(),'strataai-backup-command-')

    def setUp(self):
        self.folder = Path(tempfile.mkdtemp(prefix='case-', dir=self.root))
        self.addCleanup(self.remove_owned,self.folder,self.root,'case-')
        self.output = self.folder / 'protected backup.cms'

    def run_command(self, mode='success', *, admin='fixture_role', database='fixture_db', container='fixture-container', certificate=None):
        certificate = certificate or self.root / 'fixture.crt'
        driver = f'''set -euo pipefail
export STRATAAI_BACKUP_DB_USER={shlex.quote(admin)}
output={shlex.quote(self.output.as_posix())}
calls={shlex.quote((self.folder/'calls').as_posix())}
protocol_certificate={shlex.quote(certificate.as_posix())}
docker() {{
 printf '%s\\n' "$@" > "$calls"
 case {shlex.quote(mode)} in
  dump-failure) printf '%s' PGDMP-partial-private-fixture; printf '%s' PRIVATE_DUMP_ERROR >&2; return 14 ;;
  file-race) printf '%s' preserved-race-file > "$output" ;;
  directory-race) mkdir "$output" ;;
 esac
 printf '%s' PGDMP-private-transport-fixture
}}
openssl() {{
 if [ {shlex.quote(mode)} = certificate-race ] && [ "$1" = cms ]; then
  printf '%s' replaced-public-certificate > "$protocol_certificate"
 fi
 if [ {shlex.quote(mode)} = encrypt-failure ] && [ "$1" = cms ]; then
  printf '%s' PRIVATE_ENCRYPT_ERROR >&2; return 17
 fi
 command openssl "$@"
}}
source {shlex.quote(self.script.as_posix())} {shlex.quote(container)} {shlex.quote(database)} {shlex.quote(certificate.as_posix())} "$output"
'''
        result = subprocess.run([self.bash, '-c', driver], cwd=self.repo, capture_output=True, timeout=20,
                                env={k: v for k, v in os.environ.items() if k != 'BASH_ENV'})
        self.assertNotIn(b'PRIVATE_', result.stdout + result.stderr)
        self.assertEqual(list(self.folder.glob('.strataai-backup.*')), [])
        return result

    def test_success_encrypts_the_exact_stream_and_never_stores_plaintext(self):
        result = self.run_command()
        self.assertEqual(result.returncode, 0)
        self.assertEqual(result.stdout, b'Encrypted PostgreSQL backup created.\n')
        self.assertNotIn(b'PGDMP-private-transport-fixture', self.output.read_bytes())
        command = f'openssl cms -decrypt -binary -inform DER -in {shlex.quote(self.output.as_posix())} -recip fixture.crt -inkey fixture.key'
        decoded = subprocess.run([self.bash, '-c', command], cwd=self.root, capture_output=True, timeout=15)
        self.assertEqual(decoded.returncode, 0)
        self.assertEqual(decoded.stdout, b'PGDMP-private-transport-fixture')
        self.assertEqual((self.folder/'calls').read_text().splitlines(), ['exec','fixture-container','pg_dump',
                         '--username=fixture_role','--dbname=fixture_db','--format','custom','--no-password','--lock-wait-timeout=30s'])

    def test_existing_output_is_never_overwritten_or_read_from_the_database(self):
        self.output.write_bytes(b'original-encrypted-backup')
        self.assertNotEqual(self.run_command().returncode, 0)
        self.assertEqual(self.output.read_bytes(), b'original-encrypted-backup')
        self.assertFalse((self.folder/'calls').exists())

    def test_encryption_uses_the_validated_certificate_snapshot(self):
        certificate=self.folder/'rotating.crt'
        shutil.copyfile(self.root/'fixture.crt',certificate)
        self.assertEqual(self.run_command('certificate-race',certificate=certificate).returncode,0)
        self.assertEqual(certificate.read_text(),'replaced-public-certificate')
        command = f'openssl cms -decrypt -binary -inform DER -in {shlex.quote(self.output.as_posix())} -recip fixture.crt -inkey fixture.key'
        decoded = subprocess.run([self.bash, '-c', command], cwd=self.root, capture_output=True, timeout=15)
        self.assertEqual(decoded.returncode,0)
        self.assertEqual(decoded.stdout,b'PGDMP-private-transport-fixture')

    def test_unicode_and_spaces_in_database_names_remain_single_literal_arguments(self):
        result = self.run_command(admin='Rôle opérateur',database='MVP données')
        self.assertEqual(result.returncode,0)
        calls=(self.folder/'calls').read_text(encoding='utf-8').splitlines()
        self.assertIn('--username=Rôle opérateur',calls)
        self.assertIn('--dbname=MVP données',calls)

    def test_invalid_identity_or_certificate_refuses_before_any_database_read(self):
        invalid = self.folder/'invalid.crt'; invalid.write_text('not-a-certificate')
        for options in ({'admin':''}, {'container':'--privileged'}, {'certificate':invalid},
                        {'database':'host=private-fixture'}, {'database':'postgresql://private-fixture/db'}):
            with self.subTest(options=options):
                self.assertNotEqual(self.run_command(**options).returncode, 0)
                self.assertFalse((self.folder/'calls').exists())
                self.assertFalse(self.output.exists())

    def test_partial_dump_or_encryption_failure_cannot_publish_a_backup(self):
        for mode in ('dump-failure','encrypt-failure'):
            with self.subTest(mode=mode):
                result = self.run_command(mode)
                self.assertNotEqual(result.returncode, 0)
                self.assertNotIn(b'backup created', result.stdout)
                self.assertFalse(self.output.exists())

    def test_file_created_during_dump_is_preserved(self):
        self.assertNotEqual(self.run_command('file-race').returncode, 0)
        self.assertEqual(self.output.read_bytes(), b'preserved-race-file')

    def test_directory_created_during_dump_cannot_redirect_publication(self):
        self.assertNotEqual(self.run_command('directory-race').returncode, 0)
        self.assertTrue(self.output.is_dir())
        self.assertEqual(list(self.output.iterdir()), [])


if __name__ == '__main__':
    unittest.main()
