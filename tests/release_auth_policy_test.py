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

DISCOVERY_DEFAULTS = dict.fromkeys((
    'STRATAAI_ORGANIZATION_DELETION_DISCOVERY_ENABLED',
    'STRATAAI_ORGANIZATION_METADATA_DISCOVERY_ENABLED',
    'STRATAAI_ORGANIZATION_CONFIGURATION_DISCOVERY_ENABLED',
    'STRATAAI_ORGANIZATION_OPERATIONAL_LIFECYCLE_DISCOVERY_ENABLED',
    'STRATAAI_INVITATION_RECIPIENT_AUTHORITY_DISCOVERY_ENABLED',
    'STRATAAI_INVITATION_ISSUER_AUTHORITY_DISCOVERY_ENABLED',
), 'true')


class ReleaseAuthenticationPolicyTests(unittest.TestCase):
    def model(self, settings, host_fields=None):
        if host_fields is None:
            host_fields = {'api': POLICY_DEFAULTS, 'worker': ('STRATAAI_AUTH_REQUIRE_VERIFIED_EMAIL',)}
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
            # Return only requested fields; never print the resolved secret-bearing model.
            return {host: {key: model['services'][host]['environment'].get(key)
                           for key in keys}
                    for host, keys in host_fields.items()}

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

    def discovery_model(self, settings):
        model = self.model(settings, dict.fromkeys(('api', 'worker'), DISCOVERY_DEFAULTS))
        self.assertEqual(model['api'], dict.fromkeys(DISCOVERY_DEFAULTS), 'Discovery settings belong to Worker')
        return model['worker']

    def test_discovery_omission_retains_production_defaults(self):
        self.assertEqual(self.discovery_model({}), DISCOVERY_DEFAULTS)

    def test_operator_can_disable_each_discovery_loop(self):
        disabled = dict.fromkeys(DISCOVERY_DEFAULTS, 'false')
        self.assertEqual(self.discovery_model(disabled), disabled)

    def test_explicit_empty_discovery_settings_reach_worker_validation(self):
        empty = dict.fromkeys(DISCOVERY_DEFAULTS, '')
        self.assertEqual(self.discovery_model(empty), empty)

    def test_malformed_discovery_settings_reach_worker_validation(self):
        invalid = dict.fromkeys(DISCOVERY_DEFAULTS, 'invalid')
        self.assertEqual(self.discovery_model(invalid), invalid)

    def test_mail_example_has_complete_disabled_setup_without_embedded_secrets(self):
        sample = (Path(__file__).resolve().parents[1] / '.env.release.example').read_text(encoding='utf-8')
        entries = [line.split('=', 1) for line in sample.splitlines() if line and not line.startswith('#')]
        self.assertEqual(len(entries), len(dict(entries)), 'Example must not repeat settings')
        values = dict(entries)
        fields = ('STRATAAI_IDENTITY_EMAIL_FROM', 'STRATAAI_PUBLIC_ORIGIN', 'STRATAAI_IDENTITY_EMAIL_ACCOUNT',
                  'STRATAAI_IDENTITY_TOKEN_CURRENT_KEY', 'STRATAAI_IDENTITY_TOKEN_KEYS',
                  'STRATAAI_IDENTITY_EMAIL_API_KEY', 'STRATAAI_IDENTITY_DELIVERY_CONNECTION_STRING',
                  'STRATAAI_IDENTITY_DELIVERY_DB_PASSWORD')
        self.assertTrue(set(fields).issubset(values), 'Complete identity-mail configuration must be documented')
        self.assertEqual(values.get('STRATAAI_IDENTITY_EMAIL_ENABLED'), 'false')
        self.assertTrue(all(values[key] == '' for key in fields), 'Example must leave identity values for operator configuration')

    def test_mail_shared_configuration_and_worker_only_credentials_reach_expected_hosts(self):
        shared = {'STRATAAI_IDENTITY_EMAIL_ENABLED': 'true', 'STRATAAI_IDENTITY_EMAIL_FROM': 'sender@example.test',
                  'STRATAAI_PUBLIC_ORIGIN': 'https://app.example.test', 'STRATAAI_IDENTITY_EMAIL_ACCOUNT': 'fixture-account',
                  'STRATAAI_IDENTITY_TOKEN_CURRENT_KEY': 'fixture', 'STRATAAI_IDENTITY_TOKEN_KEYS': '{"fixture":"fixture-only-key"}'}
        private = {'STRATAAI_IDENTITY_EMAIL_API_KEY': 'fixture-only-provider-key',
                   'STRATAAI_IDENTITY_DELIVERY_CONNECTION_STRING': 'Host=postgres;Database=strataai;Username=strataai_identity_mail_runtime;Password=fixture-only-mail-password'}
        fields = (*shared, 'STRATAAI_IDENTITY_EMAIL_API_KEY', 'ConnectionStrings__IdentityDeliveryPostgres')
        model = self.model({**shared, **private}, dict.fromkeys(('api', 'worker'), fields))
        # Compare sensitive fixture settings through booleans, never secret-bearing assertion diffs.
        self.assertTrue(all(model[host][key] == value for host in model for key, value in shared.items()), 'Shared mail settings must reach both hosts')
        self.assertTrue(model['api']['STRATAAI_IDENTITY_EMAIL_API_KEY'] is None)
        self.assertTrue(model['api']['ConnectionStrings__IdentityDeliveryPostgres'] is None)
        self.assertTrue(model['worker']['STRATAAI_IDENTITY_EMAIL_API_KEY'] == private['STRATAAI_IDENTITY_EMAIL_API_KEY'])
        self.assertTrue(model['worker']['ConnectionStrings__IdentityDeliveryPostgres'] == private['STRATAAI_IDENTITY_DELIVERY_CONNECTION_STRING'])


if __name__ == '__main__':
    unittest.main()
