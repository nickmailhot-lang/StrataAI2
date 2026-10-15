import hashlib
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import tempfile
import unittest


class PostgresRestoreTests(unittest.TestCase):
    @staticmethod
    def remove_owned(path, parent, prefix):
        path=Path(path)
        assert path.resolve().parent == Path(parent).resolve() and path.name.startswith(prefix)
        assert not path.is_symlink()
        shutil.rmtree(path)

    @classmethod
    def setUpClass(cls):
        cls.root=Path(tempfile.mkdtemp(prefix='strataai-restore-command-'))
        cls.repo=Path(__file__).resolve().parents[1]
        cls.bash='C:/Program Files/Git/bin/bash.exe' if os.name=='nt' else '/bin/bash'
        cls.script=cls.repo/'scripts/restore-postgres.sh'
        try:
            for name in ('recipient','wrong'):
                cmd=f'MSYS_NO_PATHCONV=1 openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj /CN={name} -keyout {name}.key -out {name}.crt'
                result=subprocess.run([cls.bash,'-c',cmd],cwd=cls.root,capture_output=True,timeout=20)
                assert result.returncode==0,'Restore fixture certificate generation failed'
            (cls.root/'plain').write_bytes(b'PGDMP-private-restore-fixture')
            result=subprocess.run([cls.bash,'-c','openssl cms -encrypt -binary -stream -aes-256-gcm -outform DER -in plain -recip recipient.crt -out backup.cms'],cwd=cls.root,capture_output=True,timeout=15)
            assert result.returncode==0,'Restore fixture encryption failed'
            result=subprocess.run([cls.bash,'-c','openssl pkey -in recipient.key -aes-256-cbc -passout env:STRATAAI_FIXTURE_PASSPHRASE -out encrypted.key'],cwd=cls.root,capture_output=True,timeout=15,env={**os.environ,'STRATAAI_FIXTURE_PASSPHRASE':'PRIVATE_RESTORE_PASSPHRASE'})
            assert result.returncode==0,'Restore encrypted-key fixture failed'
            (cls.root/'expired-index.txt').write_text('')
            (cls.root/'expired-serial.txt').write_text('02\n')
            (cls.root/'expired-ca.cnf').write_text('[ca]\ndefault_ca=fixture\n[fixture]\ndatabase=expired-index.txt\nserial=expired-serial.txt\nnew_certs_dir=.\ndefault_md=sha256\npolicy=fixture_policy\n[fixture_policy]\ncommonName=supplied\n')
            for cmd in ('MSYS_NO_PATHCONV=1 openssl req -new -key recipient.key -subj /CN=recipient -out expired.csr',
                        'openssl ca -batch -selfsign -notext -config expired-ca.cnf -cert recipient.crt -keyfile recipient.key -startdate 20200101000000Z -enddate 20200102000000Z -in expired.csr -out expired.crt'):
                result=subprocess.run([cls.bash,'-c',cmd],cwd=cls.root,capture_output=True,timeout=15)
                assert result.returncode==0,'Expired recovery certificate fixture failed'
            result=subprocess.run([cls.bash,'-c','openssl cms -encrypt -binary -stream -aes-256-gcm -outform DER -in plain -recip expired.crt -out expired.cms'],cwd=cls.root,capture_output=True,timeout=15)
            assert result.returncode==0,'Historical recovery archive fixture failed'
        except BaseException:
            cls.remove_owned(cls.root,tempfile.gettempdir(),'strataai-restore-command-')
            raise

    @classmethod
    def tearDownClass(cls):
        cls.remove_owned(cls.root,tempfile.gettempdir(),'strataai-restore-command-')

    def setUp(self):
        self.folder=Path(tempfile.mkdtemp(prefix='case-',dir=self.root))
        self.addCleanup(self.remove_owned,self.folder,self.root,'case-')
        self.archive=self.folder/'backup.cms';shutil.copyfile(self.root/'backup.cms',self.archive)
        self.digest=hashlib.sha256(self.archive.read_bytes()).hexdigest()

    def run_command(self,mode='success',*,digest=None,key=None,certificate=None,admin='Rôle opérateur',database='MVP récupéré',temporary=True,passphrase=None):
        key=key or self.root/'recipient.key'
        certificate=certificate or self.root/'recipient.crt'
        driver=f'''set -euo pipefail
export STRATAAI_RESTORE_DB_USER={shlex.quote(admin)}
export STRATAAI_RESTORE_TMPDIR={shlex.quote(self.folder.as_posix() if temporary else '')}
protocol_archive={shlex.quote(self.archive.as_posix())}
protocol_folder={shlex.quote(self.folder.as_posix())}
docker() {{
 test "${{STRATAAI_RESTORE_KEY_PASSPHRASE+x}}" != x
 test "$1" = exec
 shift
 if [ "$1" = -i ]; then shift; fi
 test "$1" = fixture-container
 shift
 if [ "$1" = pg_restore ] && [ "$2" = --list ]; then
  printf '%s\\n' list >> "$protocol_folder/calls"
  cat > "$protocol_folder/list-input"
  if [ {shlex.quote(mode)} = format-failure ]; then printf '%s' PRIVATE_FORMAT_ERROR >&2; return 21; fi
 elif [ "$1" = psql ]; then
  printf '%s\\n' empty >> "$protocol_folder/calls"
  if [ {shlex.quote(mode)} = query-failure ]; then printf '%s' PRIVATE_QUERY_ERROR >&2; return 22; fi
  if [ {shlex.quote(mode)} = nonempty ]; then printf '%s\\n' f; else printf '%s\\n' t; fi
 elif [ "$1" = pg_restore ]; then
  printf '%s\\n' restore >> "$protocol_folder/calls"
  printf '%s\\n' "$@" > "$protocol_folder/restore-args"
  cat > "$protocol_folder/restore-input"
  if [ {shlex.quote(mode)} = restore-failure ]; then printf '%s' PRIVATE_RESTORE_ERROR >&2; return 23; fi
 else return 99; fi
}}
openssl() {{
 if [ {shlex.quote(mode)} = archive-race ]; then printf '%s' replaced-ciphertext > "$protocol_archive"; fi
 command openssl "$@"
}}
source {shlex.quote(self.script.as_posix())} fixture-container {shlex.quote(database)} {shlex.quote(certificate.as_posix())} {shlex.quote(key.as_posix())} "$protocol_archive" {shlex.quote(digest or self.digest)}
'''
        env={k:v for k,v in os.environ.items() if k not in ('BASH_ENV','STRATAAI_RESTORE_KEY_PASSPHRASE')}
        if passphrase is not None:env['STRATAAI_RESTORE_KEY_PASSPHRASE']=passphrase
        result=subprocess.run([self.bash,'-c',driver],cwd=self.repo,capture_output=True,timeout=20,env=env)
        self.assertNotIn(b'PRIVATE_',result.stdout+result.stderr)
        self.assertEqual(list(self.folder.glob('.strataai-restore.*')),[])
        return result

    def calls(self):
        path=self.folder/'calls'
        return path.read_text().splitlines() if path.exists() else []

    def test_authenticated_stream_restores_transactionally_with_literal_unicode_names(self):
        self.assertEqual(self.run_command().returncode,0)
        self.assertEqual(self.calls(),['list','empty','restore'])
        self.assertEqual((self.folder/'restore-input').read_bytes(),b'PGDMP-private-restore-fixture')
        self.assertEqual((self.folder/'restore-args').read_text(encoding='utf-8').splitlines(),['pg_restore','--username=Rôle opérateur','--dbname=MVP récupéré','--no-password','--single-transaction','--exit-on-error'])

    def test_digest_mismatch_refuses_before_any_docker_call(self):
        self.assertNotEqual(self.run_command(digest='0'*64).returncode,0)
        self.assertEqual(self.calls(),[])

    def test_wrong_key_or_tampered_gcm_refuses_before_any_docker_call(self):
        self.assertNotEqual(self.run_command(key=self.root/'wrong.key').returncode,0)
        self.assertEqual(self.calls(),[])
        data=bytearray(self.archive.read_bytes());data[len(data)//2]^=1;self.archive.write_bytes(data)
        self.assertNotEqual(self.run_command(digest=hashlib.sha256(data).hexdigest()).returncode,0)
        self.assertEqual(self.calls(),[])

    def test_nonempty_target_refuses_without_restore(self):
        self.assertNotEqual(self.run_command('nonempty').returncode,0)
        self.assertEqual(self.calls(),['list','empty'])

    def test_format_or_target_query_failure_cannot_be_reported_as_restore(self):
        self.assertNotEqual(self.run_command('format-failure').returncode,0)
        self.assertEqual(self.calls(),['list'])
        (self.folder/'calls').unlink()
        self.assertNotEqual(self.run_command('query-failure').returncode,0)
        self.assertEqual(self.calls(),['list','empty'])

    def test_restore_failure_is_unsuccessful_and_private(self):
        result=self.run_command('restore-failure')
        self.assertNotEqual(result.returncode,0)
        self.assertNotIn(b'restore completed',result.stdout)

    def test_archive_replacement_after_hash_cannot_change_the_restored_snapshot(self):
        self.assertEqual(self.run_command('archive-race').returncode,0)
        self.assertEqual(self.archive.read_bytes(),b'replaced-ciphertext')
        self.assertEqual((self.folder/'restore-input').read_bytes(),b'PGDMP-private-restore-fixture')

    def test_encrypted_key_requires_the_secret_environment_value_without_prompting(self):
        self.assertNotEqual(self.run_command(key=self.root/'encrypted.key').returncode,0)
        self.assertEqual(self.calls(),[])
        self.assertEqual(self.run_command(key=self.root/'encrypted.key',passphrase='PRIVATE_RESTORE_PASSPHRASE').returncode,0)
        self.assertNotIn('PRIVATE_', (self.folder/'restore-args').read_text())

    def test_expired_original_certificate_still_recovers_its_historical_archive(self):
        shutil.copyfile(self.root/'expired.cms',self.archive)
        digest=hashlib.sha256(self.archive.read_bytes()).hexdigest()
        self.assertEqual(self.run_command(digest=digest,certificate=self.root/'expired.crt').returncode,0)
        self.assertEqual(self.calls(),['list','empty','restore'])

    def test_missing_temporary_directory_or_identity_refuses_before_docker(self):
        for options in ({'temporary':False},{'admin':''},{'database':'host=private-fixture'},{'database':'postgresql://private-fixture/db'}):
            with self.subTest(options=options):
                self.assertNotEqual(self.run_command(**options).returncode,0)
                self.assertEqual(self.calls(),[])


if __name__=='__main__':
    unittest.main()
