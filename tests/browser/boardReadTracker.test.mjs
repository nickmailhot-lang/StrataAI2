import { EventEmitter } from 'node:events';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { trackBoardReads } from './boardReadTracker.ts';

test('late previous-screen responses cannot satisfy visibility bootstrap readiness', () => {
  const page = new EventEmitter();
  let location = 'https://example.test/app/org/boards/board';
  page.url = () => location;
  const count = trackBoardReads(page, 'board', '/app/org/boards/board/visibility');
  const request = (path = '/boards/board', method = 'GET') => ({ url: () => `https://example.test${path}`, method: () => method });
  const respond = (item, status = 200) => page.emit('response', { request: () => item, status: () => status });
  const previous = request(); page.emit('request', previous);
  location += '/visibility'; respond(previous);
  assert.equal(count(), 0);
  const initial = request(); page.emit('request', initial); respond(initial);
  assert.equal(count(), 1);
  const bootstrap = request(); page.emit('request', bootstrap); respond(bootstrap);
  assert.equal(count(), 2);
  // Failed responses/requests, other Boards and mutations cannot advance readiness.
  const failed = request(); page.emit('request', failed); respond(failed, 503); respond(failed);
  const aborted = request(); page.emit('request', aborted); page.emit('requestfailed', aborted); respond(aborted);
  const foreign = request('/boards/other'); page.emit('request', foreign); respond(foreign);
  const mutation = request('/boards/board', 'PATCH'); page.emit('request', mutation); respond(mutation);
  assert.equal(count(), 2);
});
