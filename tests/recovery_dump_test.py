import importlib.util
import io
import os
from pathlib import Path
import re
import shlex
import subprocess
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('recovery_dump', Path(__file__).resolve().parents[1] / 'scripts/ci/normalize-recovery-dump.py')
dump = importlib.util.module_from_spec(spec)
spec.loader.exec_module(dump)


def normalized(value):
    output = io.StringIO()
    dump.normalize(io.StringIO(value), output)
    return output.getvalue()


class RecoveryDumpTests(unittest.TestCase):
    def test_failed_fingerprint_is_neither_equality_nor_deliberate_corruption_evidence(self):
        source = (Path(__file__).resolve().parents[1] / 'scripts/ci/test-database-backup-restore.sh').read_text()
        function = re.search(r'^compare_fingerprint\(\) \{.*?^\}', source, re.M | re.S).group()
        bash = 'C:/Program Files/Git/bin/bash.exe' if os.name == 'nt' else '/bin/bash'
        for operator, value in (('=', 'expected'), ('!=', 'changed')):
            for status in (0, 19):
                driver = f'''set -euo pipefail
expected=expected
target_db=fixture
target_container=fixture
fingerprint() {{ printf '%s' {value}; return {status}; }}
{function}
compare_fingerprint '{operator}'
'''
                result = subprocess.run([bash, '-c', driver], capture_output=True, timeout=15,
                                        env={k: v for k, v in os.environ.items() if k != 'BASH_ENV'})
                self.assertEqual(result.returncode == 0, status == 0)

    def test_fingerprint_rejects_late_dump_or_catalog_failure_inside_command_substitution(self):
        source = (Path(__file__).resolve().parents[1] / 'scripts/ci/test-database-backup-restore.sh').read_text()
        function = re.search(r'^fingerprint\(\) \{.*?^\}', source, re.M | re.S).group()
        bash = 'C:/Program Files/Git/bin/bash.exe' if os.name == 'nt' else '/bin/bash'
        for dump_status, catalog_status in ((0, 0), (13, 0), (0, 14)):
            with self.subTest(dump_status=dump_status, catalog_status=catalog_status), tempfile.TemporaryDirectory(prefix='strataai-backup-fingerprint-') as folder:
                driver = f'''set -euo pipefail
target_container=fixture
admin=fixture
scratch={shlex.quote(Path(folder).as_posix())}
python3() {{ {shlex.quote(Path(sys.executable).as_posix())} "$@"; }}
docker() {{
 case "$3" in
  pg_dump) printf '%s\\n' 'COPY public.fixture (id) FROM stdin;' 1 '\\.'; return {dump_status} ;;
  psql) printf '%s\\n' '[]'; return {catalog_status} ;;
  *) return 99 ;;
 esac
}}
{function}
result=$(fingerprint fixture)
test -n "$result"
'''
                result = subprocess.run([bash, '-c', driver], cwd=Path(__file__).resolve().parents[1],
                                        capture_output=True, timeout=15, env={k: v for k, v in os.environ.items() if k != 'BASH_ENV'})
                self.assertEqual(result.returncode == 0, dump_status == catalog_status == 0)

    def test_physical_row_order_is_irrelevant_but_duplicate_rows_are_preserved(self):
        first = 'COPY public.records (id, body) FROM stdin;\n2\t家\n1\tPréservé 🔒\n2\t家\n\\.\n'
        second = 'COPY public.records (id, body) FROM stdin;\n2\t家\n2\t家\n1\tPréservé 🔒\n\\.\n'
        self.assertEqual(normalized(first), normalized(second))
        self.assertEqual(normalized(first).count('2\t家\n'), 2)

    def test_missing_duplicate_and_changed_values_or_columns_still_differ(self):
        source = 'COPY public.records (id, body) FROM stdin;\n1\tx\n1\tx\n\\.\n'
        for changed in (source.replace('1\tx\n', '', 1), source.replace('x', 'y'), source.replace('(id, body)', '(body, id)')):
            self.assertNotEqual(normalized(source), normalized(changed))

    def test_session_and_constraint_looking_data_remains_data(self):
        value = 'COPY public.records (body) FROM stdin;\n\\restrict data\n    CONSTRAINT x CHECK (false)\n\\unrestrict data\n\\.\n'
        result = normalized(value)
        self.assertIn('\\restrict data\n', result)
        self.assertIn('    CONSTRAINT x CHECK (false)\n', result)
        self.assertIn('\\unrestrict data\n', result)

    def test_only_table_check_lines_are_replaced_by_the_separate_catalog_comparison(self):
        value = '-- Name: records; Type: TABLE; Schema: public; Owner: postgres\nCREATE TABLE public.records (\n    id integer,\n    CONSTRAINT positive CHECK ((id > 0)),\n    CONSTRAINT unique_id UNIQUE (id)\n);\n'
        result = normalized(value)
        self.assertNotIn('CHECK', result)
        self.assertIn('CONSTRAINT unique_id UNIQUE (id)', result)
        self.assertEqual(result, value.replace('    CONSTRAINT positive CHECK ((id > 0)),\n', ''))

    def test_function_source_is_not_treated_as_table_ddl(self):
        value = '-- Name: build_records(); Type: FUNCTION; Schema: public; Owner: postgres\nCREATE FUNCTION public.build_records() RETURNS void AS $$\nCREATE TABLE literal_source (\n    CONSTRAINT preserved CHECK (false)\n);\n$$ LANGUAGE sql;\n'
        self.assertEqual(normalized(value), value)

    def test_only_random_client_guard_lines_are_removed(self):
        self.assertEqual(normalized('\\restrict random\nSELECT 1;\n\\unrestrict random\n'), 'SELECT 1;\n')

    def test_incomplete_copy_or_table_rejects(self):
        for value in ('COPY public.records (id) FROM stdin;\n1\n', '-- Name: records; Type: TABLE; Schema: public; Owner: postgres\nCREATE TABLE public.records (\n'):
            with self.assertRaises(ValueError):
                normalized(value)


if __name__ == '__main__':
    unittest.main()
