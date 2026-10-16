import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { basename, resolve } from 'node:path';
import test from 'node:test';
import { cases } from '../scripts/ci/verify-browser-shards.mjs';
import { verifyCoreMailBrowser } from '../scripts/ci/verify-core-mail-browser.mjs';

test('mail outage owns complete sequential desktop/phone cases without retries or skipped outcomes', () => {
  const result = spawnSync(process.execPath, [resolve('node_modules/@playwright/test/cli.js'), 'test',
    '--config', 'playwright.mail-outage.config.ts', '--list', '--reporter=json'], { encoding: 'utf8', timeout: 30000 });
  assert.equal(result.status, 0, 'Mail outage browser collection must succeed');
  const report = JSON.parse(result.stdout);
  const rows = cases(report);
  assert.equal(rows.length, 2);
  for (const row of rows) {
    assert.equal(basename(row.file.split('\0')[0]), 'core-mail-outage.case.ts');
    assert.equal(row.timeout, 360000); assert.equal(row.expectedStatus, 'passed');
  }
  const titles = report.suites.flatMap(suite => suite.specs.map(spec => spec.title));
  assert.deepEqual(titles.map(title => title.match(/at (1280|390)px$/)?.[1]).sort(), ['1280', '390']);
});

function report() {
  return { errors: [], config: { workers: 1, fullyParallel: false, projects: [{ retries: 0 }] },
    stats: { expected: 2, unexpected: 0, flaky: 0, skipped: 0 }, suites: [{ specs: ['1280', '390'].map(width => ({
      id: width, file: 'core-mail-outage.case.ts', title: `Mail outage at ${width}px`,
      tests: [{ projectId: '', timeout: 360000, expectedStatus: 'passed', results: [{ status: 'passed', retry: 0 }] }],
    })) }] };
}
test('runtime evidence requires both actual passes', () => {
  assert.deepEqual(verifyCoreMailBrowser(report()), { passed: 2, skipped: 0, retries: 0 });
});
test('the CI wrapper validates a fresh runtime report before accepting the phase', () => {
  const wrapper = readFileSync('scripts/ci/test-core-mail-browser.sh', 'utf8').replace(/\r\n/g, '\n');
  assert.ok(wrapper.includes('report=$(mktemp '));
  assert.ok(wrapper.includes('PLAYWRIGHT_JSON_OUTPUT_FILE="$report" npx playwright test --config playwright.mail-outage.config.ts --reporter=list,json'));
  assert.ok(wrapper.includes('node scripts/ci/verify-core-mail-browser.mjs "$report"'));
});
for (const [name, mutate] of [
  ['a missing case', value => value.suites[0].specs.pop()],
  ['runtime skips', value => { value.stats.expected = 1; value.stats.skipped = 1; value.suites[0].specs[0].tests[0].results[0].status = 'skipped'; }],
  ['expected failure', value => { value.suites[0].specs[0].tests[0].expectedStatus = 'failed'; value.suites[0].specs[0].tests[0].results[0].status = 'failed'; }],
  ['a retry despite passing counts', value => { value.suites[0].specs[0].tests[0].results.push({ status: 'passed', retry: 1 }); }],
  ['wrong viewport coverage', value => value.suites[0].specs[1].title = 'Mail outage at 1280px'],
  ['changed deadline', value => value.suites[0].specs[0].tests[0].timeout = 30000],
  ['runner errors', value => value.errors.push({ message: 'synthetic fixture failure' })],
]) test(`runtime evidence refuses ${name}`, () => {
  const value = report(); mutate(value); assert.throws(() => verifyCoreMailBrowser(value));
});
