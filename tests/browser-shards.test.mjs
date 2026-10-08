import assert from 'node:assert/strict';
import test from 'node:test';
import { cases, verifyPartition } from '../scripts/ci/verify-browser-shards.mjs';

const rows = [0, 1, 2, 3].map(index => ({ id: `case-${index}`, file: `file-${index}`, timeout: 30000, expectedStatus: 'passed' }));
const partitions = () => rows.map(row => [{ ...row }]);
test('complete disjoint partitions preserve every case and file', () => {
  assert.deepEqual(verifyPartition(rows, partitions()), { cases: 4, files: 4, counts: [1, 1, 1, 1] });
});
for (const [name, change] of [
  ['missing shard', parts => parts.pop()],
  ['omitted case', parts => parts[0] = []],
  ['duplicate case', parts => parts[1].push({ ...rows[0] })],
  ['foreign case', parts => parts[0][0].id = 'other'],
  ['changed timeout', parts => parts[0][0].timeout = 60000],
  ['changed expected outcome', parts => parts[0][0].expectedStatus = 'skipped'],
]) test(`rejects ${name}`, () => {
  const parts = partitions(); change(parts); assert.throws(() => verifyPartition(rows, parts));
});
test('same file cannot cross shard boundaries', () => {
  const full = rows.map(row => ({ ...row, file: 'shared' }));
  assert.throws(() => verifyPartition(full, full.map(row => [row])));
});
test('collection refuses ignored errors, retries and changed concurrency', () => {
  const report = { errors: [], config: { workers: 1, fullyParallel: false, projects: [{ retries: 0 }] },
    suites: [{ specs: [{ id: 'case', file: 'file', tests: [{ projectId: '', timeout: 30000, expectedStatus: 'passed' }] }] }] };
  assert.equal(cases(report).length, 1);
  for (const change of [r => r.errors.push({ message: 'private' }), r => r.config.workers = 2, r => r.config.fullyParallel = true, r => r.config.projects[0].retries = 1]) {
    const value = structuredClone(report); change(value); assert.throws(() => cases(value));
  }
});
