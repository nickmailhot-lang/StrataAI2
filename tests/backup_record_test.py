import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = Path(__file__).resolve().parents[1] / 'scripts/verify-backup-record.py'
spec = importlib.util.spec_from_file_location('backup_record', SCRIPT)
record_check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(record_check)


class BackupRecordTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='strataai-backup-record-')
        self.addCleanup(self.temporary.cleanup)
        self.folder = Path(self.temporary.name)
        self.archive = self.folder/'private-database.cms'
        self.record = self.folder/'private-database.json'
        self.archive.write_bytes(b'small-synthetic-ciphertext-fixture')
        self.value = {'schemaVersion': 1, 'startedAtUnixSeconds': 1000, 'completedAtUnixSeconds': 1001,
                      'archiveBytes': self.archive.stat().st_size, 'sha256': hashlib.sha256(self.archive.read_bytes()).hexdigest()}
        self.write()

    def write(self):
        self.record.write_text(json.dumps(self.value))

    def verify(self, age=60, now=1060):
        return record_check.verify(self.record, self.archive, age, lambda: now)

    def test_fresh_archive_has_only_fixed_sanitized_result_fields(self):
        self.assertEqual(self.verify(), {'schemaVersion': 1, 'status': 'verified', 'ageSeconds': 60,
                                        'maximumAgeSeconds': 60, 'archiveBytes': self.value['archiveBytes']})
        before = [path.read_bytes() for path in [self.record, self.archive]]
        self.verify()
        self.assertEqual(before, [path.read_bytes() for path in [self.record, self.archive]])

    def test_age_is_checked_at_completion_and_never_rounded_down_to_pass(self):
        with self.assertRaises(ValueError): self.verify(now=1060.001)
        self.assertEqual(self.verify(now=1059.001)['ageSeconds'], 60)
        with patch.object(record_check.hashlib, 'file_digest', wraps=hashlib.file_digest) as digest:
            def clock():
                self.assertEqual(digest.call_count, 1)
                return 1060
            record_check.verify(self.record, self.archive, 60, clock)

    def test_new_completion_time_or_file_mtime_cannot_refresh_an_old_snapshot(self):
        self.value['startedAtUnixSeconds'] = 1
        self.value['completedAtUnixSeconds'] = 1060
        self.write(); os.utime(self.record, None); os.utime(self.archive, None)
        with self.assertRaises(ValueError): self.verify()

    def test_future_or_reversed_clocks_are_refused(self):
        for start, end in [(1000, 1061), (1061, 1062), (1001, 1000), (1, 253402300800)]:
            self.value.update(startedAtUnixSeconds=start, completedAtUnixSeconds=end); self.write()
            with self.subTest(start=start, end=end), self.assertRaises(ValueError): self.verify()

    def test_invalid_age_limits_are_refused_before_files_are_opened(self):
        with patch.object(record_check, 'stable_file', side_effect=AssertionError('No I/O expected')):
            for age in [0, -1, True, 1.5, '60', 31536001]:
                with self.subTest(age=age), self.assertRaises(ValueError): self.verify(age=age)

    def test_missing_unknown_private_or_duplicate_record_fields_are_refused(self):
        for name in list(self.value):
            changed = dict(self.value); del changed[name]
            self.record.write_text(json.dumps(changed))
            with self.subTest(missing=name), self.assertRaises(ValueError): self.verify()
        changed = dict(self.value, database='private@example.test')
        self.record.write_text(json.dumps(changed))
        with self.assertRaises(ValueError): self.verify()
        self.record.write_text(json.dumps(self.value)[:-1] + ',"schemaVersion":1}')
        with self.assertRaises(ValueError): self.verify()

    def test_record_types_versions_numbers_and_digest_are_strict(self):
        cases = [('schemaVersion', True), ('schemaVersion', 2), ('sha256', 'A'*64), ('sha256', 12)]
        cases += [(name, value) for name in ['startedAtUnixSeconds','completedAtUnixSeconds','archiveBytes'] for value in [True, 0, -1, 1.5, '1000']]
        for name, value in cases:
            changed = dict(self.value); changed[name] = value
            self.record.write_text(json.dumps(changed))
            with self.subTest(name=name, value=value), self.assertRaises(ValueError): self.verify()

    def test_malformed_oversized_and_nonobject_records_are_refused(self):
        for content in ['{', '[]', 'null', ' '*4097, '\ufeff{}']:
            self.record.write_text(content, encoding='utf-8')
            with self.subTest(content=content[:8]), self.assertRaises(ValueError): self.verify()

    def test_missing_partial_or_modified_archive_is_refused(self):
        original = self.archive.read_bytes()
        for data in [b'', original[:-1], b'X'+original[1:]]:
            self.archive.write_bytes(data)
            with self.subTest(length=len(data)), self.assertRaises(ValueError): self.verify()
        self.archive.unlink()
        with self.assertRaises(OSError): self.verify()

    def test_changed_record_or_archive_during_hashing_is_refused(self):
        original_digest = hashlib.file_digest
        for path in [self.record, self.archive]:
            def changed(source, algorithm):
                digest = original_digest(source, algorithm)
                current = path.stat()
                os.utime(path, ns=(current.st_atime_ns, current.st_mtime_ns+10000000))
                return digest
            with self.subTest(path=path.name), patch.object(record_check.hashlib, 'file_digest', side_effect=changed), self.assertRaises(ValueError):
                self.verify()

    def test_directories_and_symbolic_links_are_refused(self):
        for name in ['record', 'archive']:
            path = getattr(self, name); original = path.read_bytes(); path.unlink(); path.mkdir()
            with self.subTest(name=name), self.assertRaises(ValueError): self.verify()
            path.rmdir(); path.write_bytes(original)
        link = self.folder/'symbolic.json'
        try: link.symlink_to(self.record)
        except OSError: self.skipTest('Symbolic link creation is unavailable on this host')
        with self.assertRaises(ValueError): record_check.verify(link, self.archive, 60, lambda: 1060)

    def test_cli_failure_does_not_print_private_input_or_create_a_pass_result(self):
        result = subprocess.run([sys.executable, str(SCRIPT), str(self.record), str(self.archive), '60'],
                                capture_output=True, timeout=10)
        self.assertEqual(result.returncode, 1)
        self.assertEqual(result.stdout, b'')
        self.assertEqual(result.stderr.decode().splitlines(), ['Backup record, archive integrity or freshness verification failed.'])


if __name__ == '__main__':
    unittest.main()
