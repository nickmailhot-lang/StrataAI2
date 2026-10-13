import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


POLICY_DEFAULTS = {
    'STRATAAI_AUTH_ALLOW_SELF_REGISTRATION': 'false',
    'STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL': 'true',
    'STRATAAI_AUTH_MIN_PASSWORD_LENGTH': '12',
    'STRATAAI_AUTH_SESSION_HOURS': '12',
    'STRATAAI_AUTH_SECURITY_TOKEN_MINUTES': '30',
}


class ReleaseAuthenticationPolicyTests(unittest.TestCase):
    def model(self, settings):
        docker = shutil.which('docker')
        if not docker and os.name == 'nt':
            candidate = Path(os.environ.get('LOCALAPPDATA', '')) / 'Programs/DockerDesktop/resources/bin/docker.exe'
            if candidate.is_file():
                docker = str(candidate)
        self.assertTrue(docker, 'Docker Compose is required for release configuration verification')
        environment = {key: value for key, value in os.environ.items()
                       if not key.startswith(('STRATAAI_', 'POSTGRES_', 'COMPOSE_'))}
        fixture = {'POSTGRES_PASSWORD': 'compose-test-admin', 'STRATAAI_API_DB_PASSWORD': 'compose-test-api',
                   'STRATAAI_WORKER_DB_PASSWORD': 'compose-test-worker', **settings}
        with tempfile.TemporaryDirectory(prefix='strataai-compose-policy-') as temporary:
            root = Path(temporary).resolve()
            self.assertEqual(root.parent, Path(tempfile.gettempdir()).resolve())
            self.assertTrue(root.name.startswith('strataai-compose-policy-'))
            env_file = root / 'fixture.env'
            env_file.write_text(''.join(f'{key}={value}\n' for key, value in fixture.items()), encoding='utf-8')
            result = subprocess.run([docker, 'compose', '--env-file', str(env_file), '-f',
                                     str(Path(__file__).resolve().parents[1] / 'compose.release.yml'),
                                     'config', '--format', 'json'], env=environment, capture_output=True, timeout=30)
            self.assertEqual(result.returncode, 0, 'Release Compose rejected a disposable policy fixture')
            model = json.loads(result.stdout)
            # Return only policy fields, never the complete resolved secret-bearing model.
            return {host: {key: model['services'][host]['environment'].get(key)
                           for key in (POLICY_DEFAULTS if host == 'api' else ('STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL',))}
                    for host in ('api', 'worker')}

    def test_omission_retains_production_policy_defaults(self):
        for host, policy in self.model({}).items():
            self.assertEqual(policy, {key: POLICY_DEFAULTS[key] for key in policy}, host)

    def test_explicit_operator_policy_reaches_both_hosts(self):
        configured = dict(zip(POLICY_DEFAULTS, ('true', 'false', '24', '48', '60')))
        for host, policy in self.model(configured).items():
            self.assertEqual(policy, {key: configured[key] for key in policy}, host)

    def test_explicit_empty_values_reach_startup_validation(self):
        configured = dict.fromkeys(POLICY_DEFAULTS, '')
        for host, policy in self.model(configured).items():
            self.assertEqual(policy, {key: configured[key] for key in policy}, host)

    def test_malformed_values_are_not_replaced_by_defaults(self):
        configured = dict.fromkeys(POLICY_DEFAULTS, 'invalid')
        for host, policy in self.model(configured).items():
            self.assertEqual(policy, {key: configured[key] for key in policy}, host)


if __name__ == '__main__':
    unittest.main()
