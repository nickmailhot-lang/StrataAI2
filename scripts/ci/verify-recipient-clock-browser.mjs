import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { basename, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { cases } from './verify-browser-shards.mjs';

export function verifyRecipientClockBrowser(report) {
  const rows = cases(report);
  assert.equal(rows.length, 4);
  for (const row of rows) {
    assert.ok(['recipient-invitation-clock.case.ts', 'recipient-invitation-telemetry.case.ts'].includes(basename(row.file.split('\0')[0])));
    assert.equal(row.timeout, 120000); assert.equal(row.expectedStatus, 'passed');
  }
  assert.deepEqual(Object.fromEntries(['expected', 'unexpected', 'flaky', 'skipped'].map(key => [key, report.stats?.[key]])),
    { expected: 4, unexpected: 0, flaky: 0, skipped: 0 });
  const executed = [];
  function visit(suite) {
    for (const spec of suite.specs ?? []) for (const result of spec.tests) executed.push({ title: spec.title, result });
    for (const child of suite.suites ?? []) visit(child);
  }
  for (const suite of report.suites) visit(suite);
  assert.equal(executed.length, 4);
  for (const filename of ['recipient-invitation-clock.case.ts', 'recipient-invitation-telemetry.case.ts']) {
    const indices = rows.map((row, index) => basename(row.file.split('\0')[0]) === filename ? index : -1).filter(index => index >= 0);
    assert.equal(indices.length, 2);
    assert.deepEqual(indices.map(index => executed[index].title.match(/at (1280|390)px$/)?.[1]).sort(), ['1280', '390']);
  }
  for (const { result } of executed) {
    assert.equal(result.results.length, 1); assert.equal(result.results[0].status, 'passed');
    assert.equal(result.results[0].retry, 0);
  }
  return { passed: 4, skipped: 0, retries: 0 };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    assert.equal(process.argv.length, 3);
    verifyRecipientClockBrowser(JSON.parse(readFileSync(process.argv[2], 'utf8')));
    console.log('All recipient calendar and observation browser cases passed without skips or retries.');
  } catch {
    console.error('Recipient clock browser execution report verification failed.'); process.exitCode = 1;
  }
}
