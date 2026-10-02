import assert from 'node:assert/strict';
import test from 'node:test';
import PerformanceReporter, { performanceEntry } from './browser/performanceReporter.mjs';

const fixture = { lists: 2, cards: 0, viewport: '1280x844', topology: 'exact release images through Nginx' };
const sample = { fixture, feedbackObserved: true, feedbackMs: 43.5 };

test('retained performance evidence strips private and arbitrary fields', () => {
  const entry = performanceEntry('list-feedback-performance.json', { ...sample,
    identity: 'private-email@example.test', title: 'private-board', token: 'private-token',
    fixture: { ...fixture, organizationId: 'private-id' } }, 'passed');
  assert.equal(entry.feedbackMs, 43.5);
  assert.equal(entry.status, 'passed');
  assert.equal(entry.budgetsMs.feedback, 100);
  assert.ok(!JSON.stringify(entry).includes('private'));
});

test('unobserved feedback is explicit and failed status cannot become success', () => {
  const entry = performanceEntry('list-feedback-performance.json', { ...sample, feedbackObserved: false, feedbackMs: null }, 'failed');
  assert.equal(entry.status, 'failed'); assert.equal(entry.feedbackObserved, false); assert.equal(entry.feedbackMs, null);
});

test('unknown attachments, scopes and invalid samples do not fabricate evidence', () => {
  for (const value of [{ ...sample, feedbackMs: Infinity }, { ...sample, feedbackMs: -1 },
    { ...sample, feedbackMs: '43.5' }, { ...sample, fixture: { ...fixture, viewport: '390x844' } }]) {
    assert.equal(performanceEntry('list-feedback-performance.json', value, 'passed'), undefined);
  }
  assert.equal(performanceEntry('arbitrary.json', sample, 'passed'), undefined);
  assert.equal(performanceEntry('list-feedback-performance.json', sample, 'private-status'), undefined);
});

test('normal benchmark retains all twenty samples and its original budgets', () => {
  const value = { ...sample, fixture: { ...fixture, lists: 3, cards: 50, samples: 20, assets: 'warm' },
    usableMs: 510, detailMs: 140, mutationP95Ms: 49, mutationSamplesMs: Array.from({ length: 20 }, (_, index) => index + 31) };
  const entry = performanceEntry('kanban-performance.json', value, 'passed');
  assert.deepEqual(entry.mutationSamplesMs, value.mutationSamplesMs);
  assert.deepEqual(entry.budgetsMs, { usable: 1500, feedback: 100, detail: 200, mutationP95: 500 });
  assert.equal(performanceEntry('kanban-performance.json', { ...value, mutationSamplesMs: [50] }, 'passed'), undefined);
  assert.equal(performanceEntry('kanban-performance.json', { ...value, mutationP95Ms: 1 }, 'passed'), undefined);
});

test('reporter waits for attachment collection and ignores unrelated payloads', async () => {
  const reporter = new PerformanceReporter();
  reporter.onTestEnd({ title: 'private-title' }, { status: 'failed', errors: ['private-error'], attachments: [
    { name: 'list-feedback-performance.json', contentType: 'application/json', body: Buffer.from(JSON.stringify(sample)) },
    { name: 'unknown.json', contentType: 'application/json', path: 'must-not-be-read' },
    { name: 'kanban-performance.json', contentType: 'text/plain', path: 'must-not-be-read' },
  ] });
  await Promise.all(reporter.pending);
  assert.equal(reporter.entries.length, 1); assert.equal(reporter.entries[0].status, 'failed');
  assert.ok(!JSON.stringify(reporter.entries).includes('private'));
});
