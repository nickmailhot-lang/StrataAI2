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
                   'STRATAAI_WORKER_DB_PASSWORD': 'compose-test-worker',
                   **{f'STRATAAI_{host.upper()}_IMAGE': f'strataai-{host}:' + 'a' * 40
                      for host in ('web', 'api', 'worker')}, **settings}
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

    def test_private_postgres_transport_exception_reaches_api_and_worker_explicitly(self):
        fields = ('STRATAAI_DATABASE_HOST', 'STRATAAI_DATABASE_SSL_MODE')
        model = self.model({}, dict.fromkeys(('api', 'worker'), fields))
        for host, configuration in model.items():
            self.assertEqual(configuration, {
                'STRATAAI_DATABASE_HOST': 'postgres',
                'STRATAAI_DATABASE_SSL_MODE': 'Disable',
            }, host)

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


class ReleaseImageSelectionTests(unittest.TestCase):
    def render(self, settings):
        docker = shutil.which('docker')
        if not docker and os.name == 'nt':
            docker = str(Path(os.environ['LOCALAPPDATA']) / 'Programs/DockerDesktop/resources/bin/docker.exe')
        self.assertTrue(docker)
        environment = {key: value for key, value in os.environ.items()
                       if not key.startswith(('STRATAAI_', 'POSTGRES_', 'COMPOSE_'))}
        fixture = {'POSTGRES_PASSWORD': 'image-fixture-admin',
                   'STRATAAI_API_DB_PASSWORD': 'image-fixture-api',
                   'STRATAAI_WORKER_DB_PASSWORD': 'image-fixture-worker', **settings}
        with tempfile.TemporaryDirectory(prefix='strataai-release-images-') as temporary:
            root = Path(temporary).resolve()
            self.assertEqual(root.parent, Path(tempfile.gettempdir()).resolve())
            path = root / 'fixture.env'
            path.write_text(''.join(f'{key}={value}\n' for key, value in fixture.items()), encoding='utf-8')
            return subprocess.run([docker, 'compose', '--env-file', str(path), '-f',
                                   str(Path(__file__).resolve().parents[1] / 'compose.release.yml'),
                                   'config', '--format', 'json'], env=environment,
                                  capture_output=True, timeout=30)

    def references(self, digest=False):
        return {f'STRATAAI_{host.upper()}_IMAGE':
                f'ghcr.io/example/strataai-{host}' + ('@sha256:' + 'a' * 64 if digest else ':' + 'a' * 40)
                for host in ('web', 'api', 'worker')}

    def assert_selected(self, references):
        result = self.render(references)
        self.assertEqual(result.returncode, 0, 'Explicit retained image references must render')
        services = json.loads(result.stdout)['services']
        for host in ('web', 'api', 'worker'):
            service = services[host]
            self.assertEqual(service['image'], references[f'STRATAAI_{host.upper()}_IMAGE'])
            self.assertNotIn('build', service)
            self.assertEqual(service.get('pull_policy'), 'never', 'Release must use the loaded, verified application images')

    def test_explicit_commit_tags_are_selected_without_build_or_pull(self):
        self.assert_selected(self.references())

    def test_explicit_digests_are_selected_without_build_or_pull(self):
        self.assert_selected(self.references(digest=True))

    def test_each_missing_application_image_is_refused(self):
        for key in self.references():
            with self.subTest(setting=key):
                settings = self.references()
                del settings[key]
                self.assertNotEqual(self.render(settings).returncode, 0,
                                    'Missing image settings must not select a local default')

    def test_each_empty_application_image_is_refused(self):
        for key in self.references():
            with self.subTest(setting=key):
                settings = self.references()
                settings[key] = ''
                self.assertNotEqual(self.render(settings).returncode, 0,
                                    'Empty image settings must not select a local default')


class ReleaseDemoConfigurationTests(unittest.TestCase):
    def render(self, missing=None):
        docker=shutil.which('docker')
        if not docker and os.name=='nt':
            docker=str(Path(os.environ['LOCALAPPDATA'])/'Programs/DockerDesktop/resources/bin/docker.exe')
        self.assertTrue(docker)
        settings={f'STRATAAI_{host.upper()}_IMAGE':f'strataai-{host}:'+('a'*40) for host in ('web','api','worker')}
        if missing: settings.pop(missing)
        environment={key:value for key,value in os.environ.items() if not key.startswith(('STRATAAI_','POSTGRES_','COMPOSE_'))}
        with tempfile.TemporaryDirectory(prefix='strataai-demo-config-') as folder:
            root=Path(folder).resolve(); self.assertEqual(root.parent,Path(tempfile.gettempdir()).resolve())
            path=root/'demo.env'; path.write_text(''.join(f'{key}={value}\n' for key,value in settings.items()))
            result=subprocess.run([docker,'compose','--project-name','strataai-demo-config','--env-file',str(path),'-f',
                str(Path(__file__).resolve().parents[1]/'compose.demo.release.yml'),'config','--format','json'],
                env=environment,capture_output=True,timeout=30)
        if missing:
            self.assertNotEqual(result.returncode,0,'A missing retained image must not select a local build/default tag')
            return None
        self.assertEqual(result.returncode,0,'Demo Compose rejected the image-only fixture')
        return json.loads(result.stdout)

    def test_demo_has_image_only_entry_private_runtime_and_no_database_or_volumes(self):
        model=self.render(); self.assertEqual(set(model['services']),{'api','web','worker'})
        self.assertFalse(model.get('volumes')); self.assertTrue(model['networks']['demo-private']['internal'])
        for host,service in model['services'].items():
            self.assertNotIn('build',service); self.assertEqual(service['pull_policy'],'never')
            self.assertEqual(service['image'],f'strataai-{host}:'+('a'*40)); self.assertFalse(service.get('volumes'))
            if host=='web':
                self.assertTrue(all(port['host_ip']=='127.0.0.1' for port in service['ports']))
                self.assertEqual(set(service['networks']),{'demo-private','demo-edge'})
            else:
                self.assertFalse(service.get('ports')); self.assertEqual(set(service['networks']),{'demo-private'})
        for host in ('api','worker'):
            values=model['services'][host]['environment']; self.assertEqual(values['STRATAAI_RUNTIME_MODE'],'demo')
            self.assertEqual(values['STRATAAI_IDENTITY_EMAIL_ENABLED'],'false')
            self.assertEqual(values['STRATAAI_ATTACHMENT_STORAGE_ENABLED'],'false')
            self.assertFalse(any(key.startswith(('ConnectionStrings','STRATAAI_DATABASE_','STRATAAI_IDENTITY_TOKEN_','STRATAAI_ATTACHMENT_S3_')) for key in values))

    def test_each_missing_image_reference_is_refused(self):
        for host in ('WEB','API','WORKER'):
            with self.subTest(host=host): self.render(f'STRATAAI_{host}_IMAGE')


if __name__ == '__main__':
    unittest.main()
