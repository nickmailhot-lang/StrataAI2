import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { basename, resolve } from 'node:path';
import test from 'node:test';
import { cases } from '../scripts/ci/verify-browser-shards.mjs';
import { verifyRecipientClockBrowser } from '../scripts/ci/verify-recipient-clock-browser.mjs';

test('recipient clock owns complete sequential desktop/phone cases without retries or skipped outcomes', () => {
  const result = spawnSync(process.execPath, [resolve('node_modules/@playwright/test/cli.js'), 'test',
    '--config', 'playwright.recipient-clock.config.ts', '--list', '--reporter=json'], { encoding: 'utf8', timeout: 30000 });
  assert.equal(result.status, 0, 'Recipient clock browser collection must succeed');
  const report = JSON.parse(result.stdout);
  const rows = cases(report);
  assert.equal(rows.length, 4);
  for (const row of rows) {
    assert.ok(['recipient-invitation-clock.case.ts', 'recipient-invitation-telemetry.case.ts'].includes(basename(row.file.split('\0')[0])));
    assert.equal(row.timeout, 120000); assert.equal(row.expectedStatus, 'passed');
  }
  for (const file of ['recipient-invitation-clock.case.ts', 'recipient-invitation-telemetry.case.ts'])
    assert.equal(rows.filter(row => basename(row.file.split('\0')[0]) === file).length, 2);
  const titles = report.suites.flatMap(suite => suite.specs.map(spec => spec.title));
  assert.deepEqual(titles.map(title => title.match(/at (1280|390)px$/)?.[1]).sort(), ['1280', '1280', '390', '390']);
});

function report() {
  return { errors: [], config: { workers: 1, fullyParallel: false, projects: [{ retries: 0 }] },
    stats: { expected: 4, unexpected: 0, flaky: 0, skipped: 0 }, suites: [{ specs: ['recipient-invitation-clock.case.ts', 'recipient-invitation-telemetry.case.ts'].flatMap(file => ['1280', '390'].map(width => ({
      id: file + width, file, title: `Recipient clock at ${width}px`,
      tests: [{ projectId: '', timeout: 120000, expectedStatus: 'passed', results: [{ status: 'passed', retry: 0 }] }],
    }))) }] };
}
test('runtime evidence requires both actual passes', () => {
  assert.deepEqual(verifyRecipientClockBrowser(report()), { passed: 4, skipped: 0, retries: 0 });
});
test('the CI wrapper validates a fresh runtime report before accepting the phase', () => {
  const wrapper = readFileSync('scripts/ci/test-recipient-clock-browser.sh', 'utf8').replace(/\r\n/g, '\n');
  assert.ok(wrapper.includes('report=$(mktemp '));
  assert.ok(wrapper.includes('PLAYWRIGHT_JSON_OUTPUT_FILE="$report" npx playwright test --config playwright.recipient-clock.config.ts --reporter=list,json'));
  assert.ok(wrapper.includes('node scripts/ci/verify-recipient-clock-browser.mjs "$report"'));
});
for (const [name, mutate] of [
  ['a missing case', value => value.suites[0].specs.pop()],
  ['runtime skips', value => { value.stats.expected = 1; value.stats.skipped = 1; value.suites[0].specs[0].tests[0].results[0].status = 'skipped'; }],
  ['expected failure', value => { value.suites[0].specs[0].tests[0].expectedStatus = 'failed'; value.suites[0].specs[0].tests[0].results[0].status = 'failed'; }],
  ['a retry despite passing counts', value => { value.suites[0].specs[0].tests[0].results.push({ status: 'passed', retry: 1 }); }],
  ['wrong viewport coverage', value => value.suites[0].specs[1].title = 'Recipient clock at 1280px'],
  ['changed deadline', value => value.suites[0].specs[0].tests[0].timeout = 30000],
  ['runner errors', value => value.errors.push({ message: 'synthetic fixture failure' })],
  ['telemetry file replaced while passing counts remain unchanged', value => { value.suites[0].specs[2].file = value.suites[0].specs[3].file = 'recipient-invitation-clock.case.ts'; }],
  ['duplicate telemetry viewport', value => { value.suites[0].specs[3].title = 'Recipient clock at 1280px'; }],
]) test(`runtime evidence refuses ${name}`, () => {
  const value = report(); mutate(value); assert.throws(() => verifyRecipientClockBrowser(value));
});
