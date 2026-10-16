import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { basename, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { cases } from './verify-browser-shards.mjs';

export function verifyCoreMailBrowser(report) {
  const rows = cases(report);
  assert.equal(rows.length, 2);
  for (const row of rows) {
    assert.equal(basename(row.file.split('\0')[0]), 'core-mail-outage.case.ts');
    assert.equal(row.timeout, 360000); assert.equal(row.expectedStatus, 'passed');
  }
  assert.deepEqual(Object.fromEntries(['expected', 'unexpected', 'flaky', 'skipped'].map(key => [key, report.stats?.[key]])),
    { expected: 2, unexpected: 0, flaky: 0, skipped: 0 });
  const executed = [];
  function visit(suite) {
    for (const spec of suite.specs ?? []) for (const result of spec.tests) executed.push({ title: spec.title, result });
    for (const child of suite.suites ?? []) visit(child);
  }
  for (const suite of report.suites) visit(suite);
  assert.equal(executed.length, 2);
  assert.deepEqual(executed.map(row => row.title.match(/at (1280|390)px$/)?.[1]).sort(), ['1280', '390']);
  for (const { result } of executed) {
    assert.equal(result.results.length, 1); assert.equal(result.results[0].status, 'passed');
    assert.equal(result.results[0].retry, 0);
  }
  return { passed: 2, skipped: 0, retries: 0 };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    assert.equal(process.argv.length, 3);
    verifyCoreMailBrowser(JSON.parse(readFileSync(process.argv[2], 'utf8')));
    console.log('Both complete mail-outage browser cases passed without skips or retries.');
  } catch {
    console.error('Mail-outage browser execution report verification failed.'); process.exitCode = 1;
  }
}
