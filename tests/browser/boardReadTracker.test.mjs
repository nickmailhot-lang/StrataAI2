import { EventEmitter } from 'node:events';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { trackBoardFilterChanges, trackBoardReads, trackCardVersion, waitForBoardReads } from './boardReadTracker.ts';

test('filter command observation counts the actual Cards Apply/Clear route exactly once per request start', () => {
  const page = new EventEmitter(); page.url = () => 'https://example.test/app/org/boards/board';
  const count = trackBoardFilterChanges(page, 'board');
  const request = change => ({ method: () => 'POST', url: () => `https://example.test/boards/board/cards/filter-change?change=${change}` });
  const apply = request('apply'); page.emit('request', apply); assert.equal(count(), 1);
  page.emit('response', { request: () => apply, status: () => 200 }); assert.equal(count(), 1);
  page.emit('request', request('clear')); assert.equal(count(), 2);
});
test('filter command observation rejects sibling routes, other Boards and reads', () => {
  const page = new EventEmitter(); page.url = () => 'https://example.test/app/org/boards/board';
  const count = trackBoardFilterChanges(page, 'board');
  for (const [path, method] of [['/boards/board/filter-change', 'POST'], ['/boards/other/cards/filter-change', 'POST'],
    ['/boards/board/cards/filter-change', 'GET'], ['/boards/board/cards/filter-change', 'PATCH'],
    ['/boards/board/cards/filter-change/other', 'POST']]) {
    page.emit('request', { method: () => method, url: () => 'https://example.test' + path });
  }
  assert.equal(count(), 0);
});
test('a foreign-origin filter request cannot satisfy same-origin command admission', () => {
  const page = new EventEmitter(); page.url = () => 'https://example.test/app/org/boards/board';
  const count = trackBoardFilterChanges(page, 'board');
  page.emit('request', { method: () => 'POST', url: () => 'https://foreign.test/boards/board/cards/filter-change?change=clear' });
  assert.equal(count(), 0);
});
test('a filter observer created before navigation qualifies the actual later page origin', () => {
  const page = new EventEmitter(); let location = 'about:blank'; page.url = () => location;
  const count = trackBoardFilterChanges(page, 'board');
  const request = () => ({ method: () => 'POST', url: () => 'https://example.test/boards/board/cards/filter-change?change=apply' });
  page.emit('request', request()); assert.equal(count(), 0);
  location = 'https://example.test/app/org/boards/board'; page.emit('request', request()); assert.equal(count(), 1);
});

test('event-driven readiness observes the qualified second response immediately and cleans up its listeners', async () => {
  const page = new EventEmitter(); let location = 'https://example.test/app'; page.url = () => location;
  const reads = trackBoardReads(page, 'board', '/app/org/boards/board');
  const request = () => ({ method: () => 'GET', url: () => 'https://example.test/boards/board' });
  const respond = item => page.emit('response', { request: () => item, status: () => 200 });
  const previous = request(); page.emit('request', previous);
  let ready = false; const waiting = waitForBoardReads(page, reads, 2).then(() => { ready = true; });
  location = 'https://example.test/app/org/boards/board'; respond(previous);
  const first = request(); page.emit('request', first); respond(first);
  await Promise.resolve(); assert.equal(ready, false);
  const second = request(); page.emit('request', second); respond(second);
  assert.equal(await Promise.race([waiting.then(() => 'ready'), new Promise(resolve => setImmediate(() => resolve('late')))]), 'ready');
  assert.equal(reads(), 2); assert.equal(page.listenerCount('response'), 1); assert.equal(page.listenerCount('close'), 0);
  await waitForBoardReads(page, reads, 2); assert.equal(page.listenerCount('response'), 1);
});
test('read observation still fails on timeout or page closure without leaving listeners', async () => {
  const page = new EventEmitter();
  await assert.rejects(waitForBoardReads(page, () => 0, 2, 10), /Required Board reads/);
  assert.equal(page.listenerCount('response'), 0); assert.equal(page.listenerCount('close'), 0);
  const closed = waitForBoardReads(page, () => 0, 2); page.emit('close');
  await assert.rejects(closed, /interrupted/);
  assert.equal(page.listenerCount('response'), 0); assert.equal(page.listenerCount('close'), 0);
});

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

test('Card revision admission ignores previous screens, foreign data, errors and late older revisions', async () => {
  const page = new EventEmitter(); let location = 'https://example.test/app/org/boards/board';
  page.url = () => location;
  const version = trackCardVersion(page, 'board', 'card', '/app/org/boards/board/cards/card');
  const request = (path = '/boards/board', method = 'GET') => ({ url: () => `https://example.test${path}`, method: () => method });
  const snapshot = (revision, board = 'board', card = 'card') => ({ board: { id: board }, lists: [{ cards: [{ id: card, version: revision }] }] });
  async function respond(item, value, status = 200) {
    page.emit('response', { request: () => item, status: () => status, json: async () => value });
    await new Promise(setImmediate);
  }
  const previous = request(); page.emit('request', previous);
  location += '/cards/card'; await respond(previous, snapshot(99)); assert.equal(version(), 0);
  const admitted = request(); page.emit('request', admitted); await respond(admitted, snapshot(3)); assert.equal(version(), 3);
  for (const value of [snapshot(2), snapshot(99, 'other'), snapshot(99, 'board', 'other'), snapshot('99'), snapshot(2.5)]) {
    const current = request(); page.emit('request', current); await respond(current, value); assert.equal(version(), 3);
  }
  const failed = request(); page.emit('request', failed); await respond(failed, snapshot(99), 503);
  const aborted = request(); page.emit('request', aborted); page.emit('requestfailed', aborted); await respond(aborted, snapshot(99));
  const mutation = request('/boards/board', 'PATCH'); page.emit('request', mutation); await respond(mutation, snapshot(99));
  const interrupted = request(); page.emit('request', interrupted);
  page.emit('response', { request: () => interrupted, status: () => 200, json: async () => { throw new Error('Interrupted fixture read'); } });
  await new Promise(setImmediate); assert.equal(version(), 3);
  const newer = request(); page.emit('request', newer); await respond(newer, snapshot(4)); assert.equal(version(), 4);
});
