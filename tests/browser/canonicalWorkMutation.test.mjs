import { test } from 'node:test';
import assert from 'node:assert/strict';
import { isCanonicalWorkMutation } from './canonicalWorkMutation.ts';

test('mapped work hub POST negotiation is transport, including nested hubs', () => {
  for (const hub of ['/boards/live', '/boards/live/stars', '/organizations/live',
    '/organizations/live/lifecycle', '/organizations/live/operational-lifecycle',
    '/organizations/live/metadata', '/organizations/live/configuration']) {
    assert.equal(isCanonicalWorkMutation('POST', `https://example.test${hub}/negotiate?negotiateVersion=1`), false);
    assert.equal(isCanonicalWorkMutation('PATCH', `https://example.test${hub}/negotiate`), true);
  }
});

test('real commands and unknown negotiation-shaped paths still count', () => {
  for (const method of ['POST', 'PATCH', 'DELETE', 'PUT']) {
    for (const path of ['/organizations', '/boards/id', '/lists/id/cards', '/cards/id',
      '/attachments/id', '/labels/id', '/organizations/live/unknown/negotiate',
      '/boards/id/live/negotiate', '/boards/live/negotiate/extra']) {
      assert.equal(isCanonicalWorkMutation(method, `https://example.test${path}`), true);
    }
  }
  for (const method of ['GET', 'HEAD'])
    assert.equal(isCanonicalWorkMutation(method, 'https://example.test/cards/id'), false);
  assert.equal(isCanonicalWorkMutation('POST', 'https://example.test/me/activity-client-events'), false);
});
