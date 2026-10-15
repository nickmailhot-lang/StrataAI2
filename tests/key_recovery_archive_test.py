import io
from pathlib import Path
import subprocess
import sys
import tarfile
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts/ci/verify-key-recovery-archive.py'
KEY = 'key-11111111-1111-4111-8111-111111111111.xml'
CONTENT = b'<key id="11111111-1111-4111-8111-111111111111" version="1"><descriptor>PRIVATE_FIXTURE_KEY</descriptor></key>'


class KeyRecoveryArchiveTests(unittest.TestCase):
    def run_archive(self, entries, *, empty=False, oversized=False, existing=False):
        with tempfile.TemporaryDirectory(prefix='strataai-key-archive-') as folder:
            root = Path(folder)
            source = root / 'source.tar'
            output = root / 'canonical.tar'
            with tarfile.open(source, 'w') as archive:
                for name, content, kind in entries:
                    entry = tarfile.TarInfo(name)
                    entry.type = kind
                    entry.size = len(content) if kind == tarfile.REGTYPE else 0
                    entry.mode = 0o777
                    entry.uid = 12345
                    if kind in (tarfile.SYMTYPE, tarfile.LNKTYPE):
                        entry.linkname = '/outside/private'
                    archive.addfile(entry, io.BytesIO(content))
            if oversized:
                source.write_bytes(b'x' * (2 * 1024 * 1024 + 1))
            if existing:
                output.write_bytes(b'preserved')
            args = ['--empty', str(source)] if empty else [str(source), str(output)]
            result = subprocess.run([sys.executable, str(SCRIPT), *args], capture_output=True, timeout=10)
            self.assertNotIn(b'PRIVATE', result.stdout + result.stderr)
            payload = output.read_bytes() if output.exists() else None
            return result, payload

    def test_flat_key_is_repacked_without_untrusted_paths_ownership_or_permissions(self):
        result, payload = self.run_archive([('.', b'', tarfile.DIRTYPE), ('./' + KEY, CONTENT, tarfile.REGTYPE)])
        self.assertEqual(result.returncode, 0)
        self.assertRegex(result.stdout.decode(), r'^[a-f0-9]{64}\n$')
        with tarfile.open(fileobj=io.BytesIO(payload)) as archive:
            entry, = archive.getmembers()
            self.assertEqual((entry.name, entry.uid, entry.gid, entry.mode), (KEY, 0, 0, 0o600))
            self.assertEqual(archive.extractfile(entry).read(), CONTENT)
        other, _ = self.run_archive([(KEY, CONTENT, tarfile.REGTYPE)])
        self.assertEqual(result.stdout, other.stdout)

    def test_empty_target_is_distinct_from_a_valid_backup(self):
        entries = [('.', b'', tarfile.DIRTYPE)]
        self.assertEqual(self.run_archive(entries, empty=True)[0].returncode, 0)
        self.assertNotEqual(self.run_archive(entries)[0].returncode, 0)
        self.assertNotEqual(self.run_archive([(KEY, CONTENT, tarfile.REGTYPE)], empty=True)[0].returncode, 0)

    def test_small_fixture_key_limit_accepts_sixteen_distinct_keys(self):
        entries = [(f'key-{n:08x}-1111-4111-8111-111111111111.xml',
                    CONTENT.replace(b'11111111-1111', f'{n:08x}-1111'.encode()), tarfile.REGTYPE) for n in range(16)]
        result, payload = self.run_archive(entries)
        self.assertEqual(result.returncode, 0)
        with tarfile.open(fileobj=io.BytesIO(payload)) as archive:
            self.assertEqual(len(archive.getmembers()), 16)

    def test_unsafe_archives_are_refused_before_any_output(self):
        cases = [
            [('../' + KEY, CONTENT, tarfile.REGTYPE)],
            [('/' + KEY, CONTENT, tarfile.REGTYPE)],
            [('nested/' + KEY, CONTENT, tarfile.REGTYPE)],
            [(KEY, CONTENT, tarfile.SYMTYPE)],
            [(KEY, CONTENT, tarfile.LNKTYPE)],
            [(KEY, CONTENT, tarfile.FIFOTYPE)],
            [(KEY, CONTENT, tarfile.REGTYPE)] * 2,
            [(KEY, CONTENT, tarfile.REGTYPE), ('./' + KEY, CONTENT, tarfile.REGTYPE)],
            [('.', b'', tarfile.DIRTYPE)] * 2,
            [(KEY, b'broken XML PRIVATE', tarfile.REGTYPE)],
            [(KEY, CONTENT.replace(b'11111111-1111', b'22222222-2222'), tarfile.REGTYPE)],
            [(KEY, CONTENT.replace(b'version="1"', b'version="2"'), tarfile.REGTYPE)],
            [(KEY, b'<!DOCTYPE key><key/>', tarfile.REGTYPE)],
            [(KEY, b'<?xml version="1.0" encoding="PRIVATE_UNSUPPORTED"?>' + CONTENT, tarfile.REGTYPE)],
            [(KEY, ('<!DOCTYPE key>' + CONTENT.decode()).encode('utf-16'), tarfile.REGTYPE)],
            [(KEY, b'\xff\xff' + CONTENT, tarfile.REGTYPE)],
            [(KEY, b'x' * 65537, tarfile.REGTYPE)],
            [('not-a-key.xml', CONTENT, tarfile.REGTYPE)],
            [(f'key-{n:08x}-1111-4111-8111-111111111111.xml', CONTENT.replace(b'11111111-1111', f'{n:08x}-1111'.encode()), tarfile.REGTYPE) for n in range(17)],
        ]
        for entries in cases:
            with self.subTest(kind=entries[0][0]):
                result, payload = self.run_archive(entries)
                self.assertNotEqual(result.returncode, 0)
                self.assertEqual(result.stderr, b'Recovery key archive refused.\n')
                self.assertIsNone(payload)

    def test_input_size_and_existing_output_are_protected(self):
        self.assertNotEqual(self.run_archive([], oversized=True)[0].returncode, 0)
        result, payload = self.run_archive([(KEY, CONTENT, tarfile.REGTYPE)], existing=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(payload, b'preserved')
