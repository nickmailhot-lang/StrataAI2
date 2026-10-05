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

test('phone Kanban evidence retains all samples and unchanged budgets without broadening other fixtures', () => {
  const value = { ...sample, fixture: { ...fixture, lists: 3, cards: 50, samples: 20, assets: 'warm', viewport: '390x844', input: 'chromium-touch', organizationId: 'private-id' },
    usableMs: 600, detailMs: 150, mutationP95Ms: 49, mutationSamplesMs: Array.from({ length: 20 }, (_, index) => index + 31),
    body: 'private-content', bearer: 'private-token' };
  const entry = performanceEntry('kanban-performance.json', value, 'failed');
  assert.equal(entry.metric, 'normal-phone-kanban'); assert.equal(entry.status, 'failed');
  assert.equal(entry.fixture.viewport, '390x844'); assert.deepEqual(entry.mutationSamplesMs, value.mutationSamplesMs);
  assert.equal(entry.fixture.input, 'chromium-touch');
  assert.deepEqual(entry.budgetsMs, { usable: 1500, feedback: 100, detail: 200, mutationP95: 500 });
  assert.ok(!JSON.stringify(entry).includes('private'));
  for (const invalid of [{ ...value, fixture: { ...value.fixture, viewport: '391x844' } },
    { ...value, fixture: { ...value.fixture, input: 'chromium-mouse' } },
    { ...value, fixture: { ...value.fixture, input: undefined } },
    { ...value, fixture: { ...value.fixture, topology: 'mocked reads' } },
    { ...value, mutationP95Ms: 1 }, { ...value, mutationSamplesMs: [50] }])
    assert.equal(performanceEntry('kanban-performance.json', invalid, 'passed'), undefined);
  assert.equal(performanceEntry('list-feedback-performance.json', { ...sample, fixture: { ...fixture, viewport: '390x844' } }, 'passed'), undefined);
  assert.equal(performanceEntry('checklist-performance.json', { ...value, fixture: { ...value.fixture, checklists: 2, items: 63, pageSize: 50 }, itemPageMs: 30, nextItemPageMs: 20 }, 'passed'), undefined);
});

test('dated Board evidence retains strict budgets and twenty valid samples without private fields', () => {
  const value = { fixture: { ...fixture, lists: 3, cards: 50, datedCards: 50, samples: 20, assets: 'warm', cardId: 'private-id' },
    usableMs: 600, detailMs: 150, mutationP95Ms: 49, mutationSamplesMs: Array.from({ length: 20 }, (_, index) => index + 31),
    profile: 'private-profile', content: 'private-content' };
  const entry = performanceEntry('card-dates-performance.json', value, 'failed');
  assert.equal(entry.metric, 'normal-desktop-card-dates'); assert.equal(entry.status, 'failed');
  assert.deepEqual(entry.budgetsMs, { usable: 1500, detail: 200, mutationP95: 500 });
  assert.deepEqual(entry.mutationSamplesMs, value.mutationSamplesMs); assert.ok(!JSON.stringify(entry).includes('private'));
  for (const invalid of [{ ...value, fixture: { ...value.fixture, datedCards: 0 } },
    { ...value, mutationP95Ms: 1 }, { ...value, mutationSamplesMs: [50] },
    { ...value, detailMs: Infinity }, { ...value, fixture: { ...value.fixture, topology: 'mocked reads' } }])
    assert.equal(performanceEntry('card-dates-performance.json', invalid, 'passed'), undefined);
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

test('checklist evidence retains paging and mutation samples, rejects malformed data and strips content', () => {
  const value = { ...sample, fixture: { ...fixture, lists: 3, cards: 50, samples: 20, assets: 'warm', checklists: 2, items: 63, pageSize: 50, cardId: 'private-id' },
    usableMs: 600, detailMs: 150, itemPageMs: 30, nextItemPageMs: 25, mutationP95Ms: 49,
    mutationSamplesMs: Array.from({ length: 20 }, (_, index) => index + 31), itemText: 'private-text' };
  const entry = performanceEntry('checklist-performance.json', value, 'failed');
  assert.equal(entry.metric, 'normal-desktop-checklist'); assert.equal(entry.status, 'failed');
  assert.equal(entry.fixture.items, 63); assert.equal(entry.fixture.pageSize, 50);
  assert.equal(entry.itemPageMs, 30); assert.equal(entry.nextItemPageMs, 25);
  assert.deepEqual(entry.budgetsMs, { usable: 1500, feedback: 100, detail: 200, mutationP95: 500 });
  assert.ok(!JSON.stringify(entry).includes('private'));
  for (const invalid of [{ ...value, fixture: { ...value.fixture, items: 0 } },
    { ...value, fixture: { ...value.fixture, pageSize: 63 } }, { ...value, mutationSamplesMs: [50] },
    { ...value, mutationP95Ms: 1 }, { ...value, itemPageMs: Infinity }, { ...value, nextItemPageMs: -1 }])
    assert.equal(performanceEntry('checklist-performance.json', invalid, 'passed'), undefined);
});
