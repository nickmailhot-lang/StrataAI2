import importlib.util
import json
import os
from pathlib import Path
import threading
import unittest
from unittest.mock import patch
import urllib.error
import urllib.request
import uuid


class IdentityProviderFixtureTests(unittest.TestCase):
    def setUp(self):
        source = Path(__file__).resolve().parents[1]/'scripts/ci/fake-identity-provider.py'
        spec = importlib.util.spec_from_file_location('provider_'+uuid.uuid4().hex, source)
        self.fixture = importlib.util.module_from_spec(spec)
        with patch.dict(os.environ, {'STRATAAI_TEST_IDENTITY_PROVIDER_INSTANCE': 'unit-fixture'}):
            spec.loader.exec_module(self.fixture)
        self.server = self.fixture.ThreadingHTTPServer(('127.0.0.1', 0), self.fixture.Handler)
        self.thread = threading.Thread(target=self.server.serve_forever, daemon=True)
        self.thread.start()
        self.addCleanup(self.close)

    def close(self):
        self.server.shutdown(); self.server.server_close(); self.thread.join(5)
        self.assertFalse(self.thread.is_alive())

    def request(self, path, data=None, authorized=True):
        headers = {'Authorization': 'Bearer ci-test-provider-key' if authorized else 'Bearer wrong',
                   'Idempotency-Key': 'strataai-identity/reset/fixture', 'Content-Type': 'application/json'}
        request = urllib.request.Request(f'http://127.0.0.1:{self.server.server_port}'+path,
                                         data=None if data is None else json.dumps(data).encode(), headers=headers)
        try: response = urllib.request.urlopen(request, timeout=3)
        except urllib.error.HTTPError as error: response = error
        with response: return response.status, json.loads(response.read(4096))

    def send(self, authorized=True):
        return self.request('/emails', {'from': 'sender@example.test', 'to': ['recipient@example.test'],
                                      'subject': 'Synthetic subject', 'text': 'Synthetic private fixture'}, authorized)

    def test_health_binds_a_fixture_instance_without_disclosing_messages(self):
        self.assertEqual(self.request('/health'), (200, {'ready': True, 'instance': 'unit-fixture'}))

    def test_outage_has_no_delivery_effect_and_recovery_retains_lost_ack_idempotency(self):
        self.assertEqual(self.request('/control', {'outage': True}), (200, {'outage': True}))
        self.assertEqual(self.send()[0], 503); self.assertEqual(self.send()[0], 503)
        self.assertEqual(self.request('/messages'), (200, []))
        self.assertEqual(self.request('/outage'), (200, {'active': True, 'attempts': [
            {'key': 'strataai-identity/reset/fixture', 'attempts': 2}]}))
        self.request('/control', {'outage': False})
        self.assertEqual(self.send()[0], 503)
        messages = self.request('/messages')[1]; self.assertEqual(len(messages), 1)
        self.assertEqual(self.send(), (200, {'id': messages[0]['id']}))
        self.assertEqual(self.request('/messages')[1][0]['attempts'], 2)

    def test_outage_does_not_bypass_provider_authentication(self):
        self.request('/control', {'outage': True})
        self.assertEqual(self.send(authorized=False)[0], 401)
        self.assertEqual(self.request('/outage')[1]['attempts'], [])

    def test_invalid_controls_preserve_the_current_outage(self):
        self.request('/control', {'outage': True})
        for data in [{'outage': 'false'}, {'outage': None}, {'unknown': False}, {'reject_next': 1}, {'conflict': 'false'}]:
            with self.subTest(data=data):
                self.assertEqual(self.request('/control', data)[0], 400)
                self.assertTrue(self.request('/outage')[1]['active'])

    def test_existing_one_shot_permanent_refusal_remains_available(self):
        self.request('/control', {'reject_next': True})
        self.assertEqual(self.send()[0], 401)
        self.assertEqual(self.request('/messages')[1], [])
        self.assertEqual(self.send()[0], 503)
        self.assertEqual(self.send()[0], 200)

    def test_conflict_has_no_effect_and_recovers_with_original_key_and_payload(self):
        self.request('/control', {'conflict': True})
        self.assertEqual(self.send(), (409, {'name': 'concurrent_idempotent_requests'}))
        self.assertEqual(self.send()[0], 409)
        self.assertEqual(self.request('/messages')[1], [])
        state = self.request('/conflicts')[1]
        self.assertEqual(state, {'active': True, 'attempts': [
            {'key': 'strataai-identity/reset/fixture', 'attempts': 2, 'mismatches': 0}]})
        self.assertNotIn('payload', json.dumps(state))
        self.request('/control', {'conflict': False})
        self.assertEqual(self.send()[0], 503)
        self.assertEqual(self.send()[0], 200)
        self.assertEqual(len(self.request('/messages')[1]), 1)

    def test_conflict_does_not_bypass_authentication_or_accept_changed_payload(self):
        self.request('/control', {'conflict': True})
        self.assertEqual(self.send(authorized=False)[0], 401)
        self.assertEqual(self.request('/conflicts')[1]['attempts'], [])
        self.assertEqual(self.send()[0], 409)
        self.request('/control', {'conflict': False})
        self.assertEqual(self.request('/emails', {'to': ['recipient@example.test'], 'text': 'changed'})[0], 409)
        self.assertEqual(self.request('/conflicts')[1]['attempts'][0]['mismatches'], 1)
        self.assertEqual(self.request('/messages')[1], [])


if __name__ == '__main__':
    unittest.main()
