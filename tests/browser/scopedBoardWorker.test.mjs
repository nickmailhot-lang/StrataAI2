import { test } from 'node:test';
import assert from 'node:assert/strict';
import { waitForBoardDelivery } from './scopedBoardWorker.ts';

const page = (cursor, count, hasMore = false, pending = false, resetRequired = false) => ({
  cursor, hasMore, pending, resetRequired, events: Array.from({ length: count }, () => ({})),
});
function client(pages) {
  const reads = [];
  return { reads, get: async path => {
    reads.push(path); assert.ok(reads.length <= pages.length, 'Unexpected delivery read');
    const value = pages[reads.length - 1];
    return { status: () => 200, json: async () => value };
  } };
}

test('ready first page cannot hide pending delivery after its hundredth source', async () => {
  const c = client([page('100', 100, true), page('101', 1, false, true), page('201', 100, true), page('205', 4)]);
  await waitForBoardDelivery(c, 'board');
  assert.deepEqual(c.reads, ['/boards/board/sync', '/boards/board/sync?since=100',
    '/boards/board/sync?since=101', '/boards/board/sync?since=201']);
});

test('an empty head is not ready until genuine sources reach the final page', async () => {
  const c = client([page('0', 0), page('100', 100, true), page('101', 1)]);
  await waitForBoardDelivery(c, 'board');
  assert.equal(c.reads.length, 3); assert.equal(c.reads[2], '/boards/board/sync?since=100');
});

test('reset retires earlier source admission and starts again at the actual head', async () => {
  const c = client([page('1', 1, false, true), page('0', 0, false, false, true), page('0', 0), page('1', 1)]);
  await waitForBoardDelivery(c, 'board');
  assert.deepEqual(c.reads, ['/boards/board/sync', '/boards/board/sync?since=1', '/boards/board/sync', '/boards/board/sync']);
});

test('malformed or backward delivery progress cannot establish readiness', async () => {
  await assert.rejects(waitForBoardDelivery(client([page('100', 99, true)]), 'board'), /Invalid Board delivery progress/);
  await assert.rejects(waitForBoardDelivery(client([page('invalid', 1)]), 'board'), /Invalid Board delivery page/);
  await assert.rejects(waitForBoardDelivery(client([page('100', 100, true), page('99', 1)]), 'board'), /Invalid Board delivery progress/);
});
