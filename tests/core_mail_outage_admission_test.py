import json
import os
from pathlib import Path
import shlex
import subprocess
import tempfile
import unittest


class CoreMailOutageAdmissionTests(unittest.TestCase):
    repo = Path(__file__).resolve().parents[1]
    bash = 'C:/Program Files/Git/bin/bash.exe' if os.name == 'nt' else '/bin/bash'

    def check_refusal(self, *, ci='true', revision='a'*40, mode='production', provider='another-fixture', actor='11111111-1111-1111-1111-111111111111', conflict=False):
        with tempfile.TemporaryDirectory(prefix='strataai-core-mail-admission-') as temporary:
            calls = Path(temporary)/'calls'
            runtime = json.dumps({'service': 'strataai-api', 'mode': mode, 'revision': 'a'*40})
            health = json.dumps({'ready': True, 'instance': provider})
            source = self.repo/'scripts/ci/core-mail-outage.inc.sh'
            helpers = ''
            if conflict:
                core = source.read_text()
                helpers = core[core.index('core_uuid() {'):core.index('# Admit both process')]
                source = self.repo/'scripts/ci/identity-mail-conflict.inc.sh'
            script = f'''set -euo pipefail
calls={shlex.quote(calls.as_posix())}
curl() {{
 uri="${{!#}}"; printf '%s\\n' "$uri" >> "$calls"
 case "$uri" in
  */api/runtime) printf '%s\\n200' {shlex.quote(runtime)} ;;
  */health) printf '%s\\n200' {shlex.quote(health)} ;;
  *) return 97 ;;
 esac
}}
query() {{ printf '%s\\n' unexpected-database-access >> "$calls"; printf 1; }}
core_mail_process_identity() {{ printf '%s\\n' unexpected-process-access >> "$calls"; }}
base=http://127.0.0.1:8080; fixture=http://127.0.0.1:19090; scratch={shlex.quote(temporary)}
mail_owner_id={shlex.quote(actor)}
signup_org=22222222-2222-2222-2222-222222222222
mail_board=33333333-3333-3333-3333-333333333333
{helpers}
source {shlex.quote(source.as_posix())}
'''
            env = {k: v for k, v in os.environ.items() if k != 'BASH_ENV'}
            env.update(CI=ci, GITHUB_ACTIONS='true', STRATAAI_IMAGE_TAG=revision,
                       STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE='owned-fixture')
            result = subprocess.run([self.bash, '-c', script], cwd=self.repo, env=env, capture_output=True, timeout=15)
            self.assertNotEqual(result.returncode, 0)
            return calls.read_text().splitlines() if calls.exists() else []

    def test_non_ci_context_refuses_before_any_http_or_database_access(self):
        self.assertEqual(self.check_refusal(ci='false'), [])

    def test_unbound_revision_refuses_before_any_http_or_database_access(self):
        self.assertEqual(self.check_refusal(revision='main'), [])

    def test_demo_api_refuses_before_provider_control_or_database_access(self):
        self.assertEqual(self.check_refusal(mode='demo'), ['http://127.0.0.1:8080/api/runtime'])

    def test_different_api_revision_refuses_before_provider_control_or_database_access(self):
        self.assertEqual(self.check_refusal(revision='b'*40), ['http://127.0.0.1:8080/api/runtime'])

    def test_invalid_actor_refuses_before_any_http_or_database_access(self):
        self.assertEqual(self.check_refusal(actor="fixture'; invalid identifier"), [])

    def test_foreign_provider_instance_refuses_before_control_or_database_access(self):
        self.assertEqual(self.check_refusal(), ['http://127.0.0.1:8080/api/runtime', 'http://127.0.0.1:19090/health'])

    def test_complete_identity_rehearsal_runs_outage_before_restoring_the_profile(self):
        source = (self.repo/'scripts/ci/test-identity-email.sh').read_text()
        call = '\nsource scripts/ci/core-mail-outage.inc.sh\n'
        self.assertEqual(source.count(call), 1)
        self.assertLess(source.index('unset signup_token signup_hash mail_proof signup_body'), source.index(call))
        self.assertLess(source.index(call), source.rindex('up -d --wait --wait-timeout 180 api worker >/dev/null; signup_closed=false'))

    def test_conflict_rehearsal_refuses_unowned_targets_before_control_or_database_access(self):
        for options, expected in [
            ({'ci': 'false'}, []), ({'revision': 'main'}, []),
            ({'actor': "invalid'; actor"}, []),
            ({'mode': 'demo'}, ['http://127.0.0.1:8080/api/runtime']),
            ({'revision': 'b'*40}, ['http://127.0.0.1:8080/api/runtime']),
            ({}, ['http://127.0.0.1:8080/api/runtime', 'http://127.0.0.1:19090/health']),
        ]:
            with self.subTest(options=options):
                self.assertEqual(self.check_refusal(conflict=True, **options), expected)

    def test_complete_identity_rehearsal_runs_conflict_once_after_outage_before_profile_restore(self):
        source = (self.repo/'scripts/ci/test-identity-email.sh').read_text()
        call = '\nsource scripts/ci/identity-mail-conflict.inc.sh\n'
        self.assertEqual(source.count(call), 1)
        self.assertLess(source.index('source scripts/ci/core-mail-outage.inc.sh'), source.index(call))
        self.assertLess(source.index(call), source.rindex('up -d --wait --wait-timeout 180 api worker >/dev/null; signup_closed=false'))
        self.assertIn('"${mail_conflict_owned:-0}" = 1', source)


if __name__ == '__main__':
    unittest.main()
